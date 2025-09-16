// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using Microsoft.Extensions.Options;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Longbow.TcpSocket;

sealed class DefaultTcpSocketClient(
    IOptions<DataConverterCollection> converterCollection,
    IOptions<TcpSocketClientOptions> options) : ITcpSocketClient
{
    private TcpClient? _client;
    private IPEndPoint? _localEndPoint;
    private CancellationTokenSource? _autoReceiveTokenSource;
    private readonly SemaphoreSlim _semaphoreSlimForConnect = new(1, 1);
    private readonly TcpSocketClientOptions _options = options.Value.CopyTo();

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public bool IsConnected => _client?.Connected ?? false;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public IPEndPoint LocalEndPoint => _localEndPoint ?? _options.LocalEndPoint;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public Func<ReadOnlyMemory<byte>, ValueTask>? ReceivedCallback { get; set; }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public Func<Task>? OnConnecting { get; set; }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public Func<Task>? OnConnected { get; set; }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public TcpSocketClientOptions Options => _options;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="endPoint"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public async ValueTask<bool> ConnectAsync(IPEndPoint endPoint, CancellationToken token = default)
    {
        if (IsConnected)
        {
            return true;
        }

        var ret = false;
        try
        {
            await CloseAsync();

            if (OnConnecting != null)
            {
                await OnConnecting();
            }

            _client = new();
            await _client.ConnectAsync(endPoint, token);

            if (OnConnected != null)
            {
                await OnConnected();
            }

            if (IsConnected)
            {
                _localEndPoint = (IPEndPoint?)_client.Client.LocalEndPoint;
                ret = true;
            }

            if (_options.IsAutoReceive)
            {
                _ = Task.Run(AutoReceiveAsync, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            if (_semaphoreSlimForConnect.CurrentCount == 0)
            {
                _semaphoreSlimForConnect.Release();
            }
        }
        return ret;
    }

    private async ValueTask AutoReceiveAsync()
    {
        // 自动接收方法
        _autoReceiveTokenSource ??= new();

        using var block = MemoryPool<byte>.Shared.Rent(_options.ReceiveBufferSize);
        var buffer = block.Memory;
        while (_autoReceiveTokenSource is { IsCancellationRequested: false })
        {
            await ReceiveCoreAsync(block.Memory, _autoReceiveTokenSource.Token);
        }
    }

    private async ValueTask<int> ReceiveCoreAsync(Memory<byte> buffer, CancellationToken token)
    {
        var len = 0;
        try
        {
            if (_client is { Connected: true })
            {
                var receiveToken = token;
                if (_options.ReceiveTimeout > 0)
                {
                    // 设置接收超时时间
                    using var receiveTokenSource = new CancellationTokenSource(_options.ReceiveTimeout);
                    using var link = CancellationTokenSource.CreateLinkedTokenSource(receiveToken, receiveTokenSource.Token);
                    receiveToken = link.Token;
                }

                using var receiver = new Receiver(_client!.Client);
                len = await receiver.ReceiveAsync(buffer, receiveToken);
            }
        }
        catch (OperationCanceledException)
        {
            // canceled
        }
        finally
        {
            if (ReceivedCallback != null)
            {
                // 如果订阅回调则触发回调
                await ReceivedCallback(buffer[0..len]);
            }
        }
        return len;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public async ValueTask<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken token = default)
    {
        using var sender = new Sender(_client!.Client);
        return await sender.SendAsync(data, token);
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken token = default) => ReceiveCoreAsync(buffer, token);

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public ValueTask CloseAsync()
    {
        if (_client != null)
        {
            _client.Close();
            _client = null;
        }
        return ValueTask.CompletedTask;
    }

    private readonly ConcurrentDictionary<Func<ReadOnlyMemory<byte>, ValueTask>, Dictionary<IDataPackageAdapter, List<Func<ReadOnlyMemory<byte>, ValueTask>>>> DataPackageAdapterCache = [];

    /// <summary>
    /// 增加 <see cref="ITcpSocketClient"/> 数据适配器及其对应的回调方法
    /// </summary>
    /// <param name="adapter"></param>
    /// <param name="callback"></param>
    /// <remarks>支持同一个数据处理器 <see cref="IDataPackageAdapter"/> 添加多个回调方法</remarks>
    public void AddDataPackageAdapter(IDataPackageAdapter adapter, Func<ReadOnlyMemory<byte>, ValueTask> callback)
    {
        async ValueTask Proxy(ReadOnlyMemory<byte> buffer)
        {
            // 将接收到的数据传递给 DataPackageAdapter 进行数据处理合规数据触发 ReceivedCallBack 回调
            await adapter.HandlerAsync(buffer);
        }

        if (DataPackageAdapterCache.TryGetValue(callback, out var list))
        {
            if (list.TryGetValue(adapter, out var items))
            {
                items.Add(Proxy);
            }
            else
            {
                list.TryAdd(adapter, [Proxy]);
            }
        }
        else
        {
            var item = new Dictionary<IDataPackageAdapter, List<Func<ReadOnlyMemory<byte>, ValueTask>>>()
            {
                { adapter, [Proxy] }
            };
            DataPackageAdapterCache.TryAdd(callback, item);
        }

        ReceivedCallback += Proxy;

        // 设置 DataPackageAdapter 的回调函数
        adapter.ReceivedCallback += callback;
    }

    /// <summary>
    /// 移除 <see cref="ITcpSocketClient"/> 数据适配器及其对应的回调方法
    /// </summary>
    /// <param name="callback"></param>
    public void RemoveDataPackageAdapter(Func<ReadOnlyMemory<byte>, ValueTask> callback)
    {
        if (DataPackageAdapterCache.TryRemove(callback, out var list))
        {
            foreach (var item in list)
            {
                item.Key.ReceivedCallback -= callback;
                foreach (var proxy in item.Value)
                {
                    ReceivedCallback -= proxy;
                }
            }
        }
    }

    private readonly ConcurrentDictionary<Delegate, Dictionary<IDataPackageAdapter, List<(Func<ReadOnlyMemory<byte>, ValueTask> Proxy, Func<ReadOnlyMemory<byte>, ValueTask> AdapterProxy)>>> DataPackageAdapterEntityCache = [];

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public void AddDataPackageAdapter<TEntity>(IDataPackageAdapter adapter, Func<TEntity?, ValueTask> callback)
    {
        var converter = new DataConverter<TEntity>(converterCollection.Value);
        AddDataPackageAdapter(adapter, converter, callback);
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public void AddDataPackageAdapter<TEntity>(IDataPackageAdapter adapter, IDataConverter<TEntity> converter, Func<TEntity?, ValueTask> callback)
    {
        async ValueTask Proxy(ReadOnlyMemory<byte> buffer)
        {
            // 将接收到的数据传递给 DataPackageAdapter 进行数据处理合规数据触发 ReceivedCallBack 回调
            await adapter.HandlerAsync(buffer);
        }

        async ValueTask AdapterProxy(ReadOnlyMemory<byte> buffer)
        {
            TEntity? ret = default;
            if (converter.TryConvertTo(buffer, out var t))
            {
                ret = t;
            }
            await callback(ret);
        }

        if (DataPackageAdapterEntityCache.TryGetValue(callback, out var list))
        {
            if (list.TryGetValue(adapter, out var items))
            {
                items.Add((Proxy, AdapterProxy));
            }
            else
            {
                list.TryAdd(adapter, [(Proxy, AdapterProxy)]);
            }
        }
        else
        {
            var item = new Dictionary<IDataPackageAdapter, List<(Func<ReadOnlyMemory<byte>, ValueTask>, Func<ReadOnlyMemory<byte>, ValueTask>)>>()
            {
                { adapter, [(Proxy, AdapterProxy)] }
            };
            DataPackageAdapterEntityCache.TryAdd(callback, item);
        }

        ReceivedCallback += Proxy;

        // 设置 DataPackageAdapter 的回调函数
        adapter.ReceivedCallback = AdapterProxy;
    }

    /// <summary>
    /// 移除 <see cref="ITcpSocketClient"/> 数据适配器及其对应的回调方法
    /// </summary>
    /// <param name="callback"></param>
    public void RemoveDataPackageAdapter<TEntity>(Func<TEntity?, ValueTask> callback)
    {
        if (DataPackageAdapterEntityCache.TryRemove(callback, out var list))
        {
            foreach (var item in list)
            {
                var adapter = item.Key;
                foreach (var (proxy, adapterProxy) in item.Value)
                {
                    ReceivedCallback -= proxy;
                    adapter.ReceivedCallback -= adapterProxy;
                }
            }
        }
    }

    private async ValueTask DisposeAsync(bool disposing)
    {
        if (disposing)
        {
            await CloseAsync();
        }
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(true);
    }
}

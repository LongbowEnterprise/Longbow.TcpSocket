// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using System.Buffers;
using System.Collections.Concurrent;
using System.Text;

namespace Longbow.TcpSocket;

/// <summary>
/// <see cref="ITcpSocketClient"/> 扩展方法类
/// </summary>
public static class ITcpSocketClientExtensions
{
    /// <summary>
    /// Sends the specified string content to the connected TCP socket client asynchronously.
    /// </summary>
    /// <remarks>This method converts the provided string content into a byte array using the specified
    /// encoding  (or UTF-8 by default) and sends it to the connected TCP socket client. Ensure the client is connected
    /// before calling this method.</remarks>
    /// <param name="client">The TCP socket client to which the content will be sent. Cannot be <see langword="null"/>.</param>
    /// <param name="content">The string content to send. Cannot be <see langword="null"/> or empty.</param>
    /// <param name="encoding">The character encoding to use for converting the string content to bytes.  If <see langword="null"/>, UTF-8
    /// encoding is used by default.</param>
    /// <param name="token">A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> that represents the asynchronous operation.  The result is <see
    /// langword="true"/> if the content was sent successfully; otherwise, <see langword="false"/>.</returns>
    public static ValueTask<bool> SendAsync(this ITcpSocketClient client, string content, Encoding? encoding = null, CancellationToken token = default)
    {
        var buffer = encoding?.GetBytes(content) ?? Encoding.UTF8.GetBytes(content);
        return client.SendAsync(buffer, token);
    }

    /// <summary>
    /// Establishes an asynchronous connection to the specified host and port.
    /// </summary>
    /// <param name="client">The TCP socket client to which the content will be sent. Cannot be <see langword="null"/>.</param>
    /// <param name="ipString">The hostname or IP address of the server to connect to. Cannot be null or empty.</param>
    /// <param name="port">The port number on the server to connect to. Must be a valid port number between 0 and 65535.</param>
    /// <param name="token">An optional <see cref="CancellationToken"/> to cancel the connection attempt. Defaults to <see
    /// langword="default"/> if not provided.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the connection
    /// is successfully established; otherwise, <see langword="false"/>.</returns>
    public static ValueTask<bool> ConnectAsync(this ITcpSocketClient client, string ipString, int port, CancellationToken token = default)
    {
        var endPoint = TcpSocketUtility.ConvertToIpEndPoint(ipString, port);
        return client.ConnectAsync(endPoint, token);
    }

    /// <summary>
    /// 异步接收方法
    /// </summary>
    /// <param name="client"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public static async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(this ITcpSocketClient client, CancellationToken token = default)
    {
        using var memory = MemoryPool<byte>.Shared.Rent(client.Options.ReceiveBufferSize);
        var len = await client.ReceiveAsync(memory.Memory, token);
        if (len == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        return memory.Memory[0..len].ToArray();
    }

    private static readonly ConcurrentDictionary<Func<ReadOnlyMemory<byte>, ValueTask>, Dictionary<IDataPackageAdapter, List<Func<ReadOnlyMemory<byte>, ValueTask>>>> DataPackageAdapterCache = [];

    /// <summary>
    /// 增加数据处理器及其对应的回调方法
    /// </summary>
    /// <param name="client"></param>
    /// <param name="handler"></param>
    /// <param name="callback"></param>
    public static void AddDataPackageAdapter(this ITcpSocketClient client, IDataPackageHandler handler, Func<ReadOnlyMemory<byte>, ValueTask> callback) => client.AddDataPackageAdapter(new DataPackageAdapter(handler), callback);

    /// <summary>
    /// 增加数据适配器及其对应的回调方法
    /// </summary>
    /// <param name="client"></param>
    /// <param name="adapter"></param>
    /// <param name="callback"></param>
    /// <remarks>支持同一个数据处理器 <see cref="IDataPackageAdapter"/> 添加多个回调方法</remarks>
    public static void AddDataPackageAdapter(this ITcpSocketClient client, IDataPackageAdapter adapter, Func<ReadOnlyMemory<byte>, ValueTask> callback)
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

        client.ReceivedCallback += Proxy;

        // 设置 DataPackageAdapter 的回调函数
        adapter.ReceivedCallback += callback;
    }

    /// <summary>
    /// 移除 <see cref="ITcpSocketClient"/> 数据适配器及其对应的回调方法
    /// </summary>
    /// <param name="client"></param>
    /// <param name="callback"></param>
    public static void RemoveDataPackageAdapter(this ITcpSocketClient client, Func<ReadOnlyMemory<byte>, ValueTask> callback)
    {
        if (DataPackageAdapterCache.TryRemove(callback, out var list))
        {
            foreach (var item in list)
            {
                foreach (var proxy in item.Value)
                {
                    item.Key.ReceivedCallback -= callback;
                    client.ReceivedCallback -= proxy;
                }
            }
        }
    }

    private static readonly ConcurrentDictionary<Delegate, Dictionary<IDataPackageAdapter, List<(Func<ReadOnlyMemory<byte>, ValueTask> Proxy, Func<ReadOnlyMemory<byte>, ValueTask> AdapterProxy)>>> DataPackageAdapterEntityCache = [];

    /// <summary>
    /// 增加数据处理器及其对应的回调方法
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    /// <param name="client"></param>
    /// <param name="handler"></param>
    /// <param name="callback"></param>
    public static void AddDataPackageAdapter<TEntity>(this ITcpSocketClient client, IDataPackageHandler handler, Func<TEntity?, ValueTask> callback) => client.AddDataPackageAdapter(new DataPackageAdapter(handler), callback);

    /// <summary>
    /// 增加数据处理器及其对应的回调方法
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    /// <param name="client"></param>
    /// <param name="handler"></param>
    /// <param name="converter"></param>
    /// <param name="callback"></param>
    public static void AddDataPackageAdapter<TEntity>(this ITcpSocketClient client, IDataPackageHandler handler, IDataConverter<TEntity> converter, Func<TEntity?, ValueTask> callback) => client.AddDataPackageAdapter(new DataPackageAdapter(handler), converter, callback);

    /// <summary>
    /// 增加数据适配器及其对应的回调方法
    /// </summary>
    /// <typeparam name="TEntity"></typeparam>
    /// <param name="client"></param>
    /// <param name="adapter"></param>
    /// <param name="callback"></param>
    public static void AddDataPackageAdapter<TEntity>(this ITcpSocketClient client, IDataPackageAdapter adapter, Func<TEntity?, ValueTask> callback)
    {
        var converter = new DataConverter<TEntity>();
        client.AddDataPackageAdapter(adapter, converter, callback);
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public static void AddDataPackageAdapter<TEntity>(this ITcpSocketClient client, IDataPackageAdapter adapter, IDataConverter<TEntity> converter, Func<TEntity?, ValueTask> callback)
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

        client.ReceivedCallback += Proxy;

        // 设置 DataPackageAdapter 的回调函数
        adapter.ReceivedCallback = AdapterProxy;
    }

    /// <summary>
    /// 移除 <see cref="ITcpSocketClient"/> 数据适配器及其对应的回调方法
    /// </summary>
    /// <param name="client"></param>
    /// <param name="callback"></param>
    public static void RemoveDataPackageAdapter<TEntity>(this ITcpSocketClient client, Func<TEntity?, ValueTask> callback)
    {
        if (DataPackageAdapterEntityCache.TryRemove(callback, out var list))
        {
            foreach (var item in list)
            {
                var adapter = item.Key;
                foreach (var (proxy, adapterProxy) in item.Value)
                {
                    client.ReceivedCallback -= proxy;
                    adapter.ReceivedCallback -= adapterProxy;
                }
            }
        }
    }
}

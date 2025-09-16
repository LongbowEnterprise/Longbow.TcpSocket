// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using Microsoft.Extensions.Options;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Longbow.TcpSocket;

sealed class DefaultTcpSocketClient(IOptions<TcpSocketClientOptions> options) : ITcpSocketClient
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

            // 并发控制
            var connectToken = token;
            if (Options.ConnectTimeout > 0)
            {
                // 设置连接超时时间
                using var connectTokenSource = new CancellationTokenSource(Options.ConnectTimeout);
                using var link = CancellationTokenSource.CreateLinkedTokenSource(connectToken, connectTokenSource.Token);
                connectToken = link.Token;
            }
            await _semaphoreSlimForConnect.WaitAsync(connectToken).ConfigureAwait(false);

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
        if (_autoReceiveTokenSource != null)
        {
            _autoReceiveTokenSource.Cancel();
            _autoReceiveTokenSource.Dispose();
            _autoReceiveTokenSource = null;
        }

        if (_client != null)
        {
            _client.Close();
            _client = null;
        }
        return ValueTask.CompletedTask;
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

// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using Microsoft.Extensions.Options;
using System.Buffers;
using System.Net;
using System.Net.Sockets;

namespace Longbow.TcpSocket;

sealed class DefaultTcpSocketClient(IOptions<TcpSocketClientOptions> options) : ITcpSocketClient
{
    private TcpClient? _client;
    private IPEndPoint? _localEndPoint;
    private CancellationTokenSource? _autoReceiveTokenSource;
    private readonly SemaphoreSlim _semaphoreSlimForConnect = new(1, 1);

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public bool IsConnected => _client?.Connected ?? false;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public IPEndPoint LocalEndPoint => _localEndPoint ?? options.Value.LocalEndPoint;

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
    public TcpSocketClientOptions Options => options.Value;

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

            if (options.Value.IsAutoReceive)
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

        using var block = MemoryPool<byte>.Shared.Rent(options.Value.ReceiveBufferSize);
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
            var receiveToken = token;
            if (options.Value.ReceiveTimeout > 0)
            {
                // 设置接收超时时间
                using var receiveTokenSource = new CancellationTokenSource(options.Value.ReceiveTimeout);
                using var link = CancellationTokenSource.CreateLinkedTokenSource(receiveToken, receiveTokenSource.Token);
                receiveToken = link.Token;
            }

            using var receiver = new Receiver(_client!.Client);
            len = await receiver.ReceiveAsync(buffer, receiveToken);

            if (ReceivedCallback != null)
            {
                // 如果订阅回调则触发回调
                await ReceivedCallback(buffer[0..len]);
            }
        }
        catch (OperationCanceledException)
        {
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

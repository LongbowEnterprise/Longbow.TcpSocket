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
    private readonly TcpSocketClientOptions _options = options.Value.CopyTo();
    private Sender? _sender;
    private Receiver? _receiver;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public bool IsConnected => _client?.Connected ?? false;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public IPEndPoint LocalEndPoint => _localEndPoint ?? Options.LocalEndPoint;

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
            await _semaphoreSlimForConnect.WaitAsync(token).ConfigureAwait(false);

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

            if (Options.IsAutoReceive)
            {
                _ = Task.Run(AutoReceiveAsync, CancellationToken.None);
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
        ResetAutoReceiveTokenSource();

        _autoReceiveTokenSource = new();

        while (_autoReceiveTokenSource is { IsCancellationRequested: false })
        {
            var buffer = ArrayPool<byte>.Shared.Rent(Options.ReceiveBufferSize);
            try
            {
                await ReceiveCoreAsync(buffer);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private async ValueTask<int> ReceiveCoreAsync(Memory<byte> buffer)
    {
        var len = 0;
        try
        {
            if (_client is { Connected: true })
            {
                _receiver ??= new Receiver(_client.Client);
                len = await _receiver.ReceiveAsync(buffer);
            }
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
        if (_client != null)
        {
            _sender ??= new Sender(_client.Client);
        }

        var ret = false;
        if (_sender != null)
        {
            ret = await _sender.SendAsync(data);
        }

        return ret;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        if (Options.IsAutoReceive)
        {
            throw new InvalidOperationException("Cannot call ReceiveAsync when IsAutoReceive is enabled. Please set IsAutoReceive to false");
        }

        return ReceiveCoreAsync(buffer);
    }

    private void ResetAutoReceiveTokenSource()
    {
        if (_autoReceiveTokenSource != null)
        {
            _autoReceiveTokenSource.Cancel();
            _autoReceiveTokenSource.Dispose();
            _autoReceiveTokenSource = null;
        }
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public ValueTask CloseAsync()
    {
        ResetAutoReceiveTokenSource();

        _sender?.Dispose();
        _sender = null;

        _receiver?.Dispose();
        _receiver = null;

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

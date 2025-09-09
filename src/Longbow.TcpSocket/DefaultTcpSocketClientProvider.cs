// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace Longbow.TcpSocket;

/// <summary>
/// TcpSocket 客户端默认实现
/// </summary>
[UnsupportedOSPlatform("browser")]
sealed class DefaultTcpSocketClientProvider : ITcpSocketClientProvider
{
    private TcpClient? _client;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public bool IsConnected => _client?.Connected ?? false;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public IPEndPoint LocalEndPoint { get; set; } = new IPEndPoint(IPAddress.Any, 0);

    private readonly SemaphoreSlim _semaphoreSlimForConnect = new(1, 1);
    private readonly SemaphoreSlim _semaphoreSlimForSendAndReceive = new(1, 1);

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public async ValueTask<bool> ConnectAsync(IPEndPoint endPoint, CancellationToken token = default)
    {
        _client = new TcpClient(LocalEndPoint);
        try
        {
            await _semaphoreSlimForConnect.WaitAsync(token).ConfigureAwait(false);

            await _client.ConnectAsync(endPoint, token).ConfigureAwait(false);
            if (_client.Connected)
            {
                if (_client.Client.LocalEndPoint is IPEndPoint localEndPoint)
                {
                    LocalEndPoint = localEndPoint;
                }
            }
        }
        finally
        {
            _semaphoreSlimForConnect.Release();
        }
        return _client.Connected;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public async ValueTask<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken token = default)
    {
        var ret = false;
        if (_client != null)
        {
            try
            {
                await _semaphoreSlimForSendAndReceive.WaitAsync(token).ConfigureAwait(false);
                var stream = _client.GetStream();
                await stream.WriteAsync(data, token).ConfigureAwait(false);
                ret = true;
            }
            finally
            {
                _semaphoreSlimForSendAndReceive.Release();
            }
        }
        return ret;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        var len = 0;
        if (_client is { Connected: true })
        {
            try
            {
                await _semaphoreSlimForSendAndReceive.WaitAsync(token).ConfigureAwait(false);
                var stream = _client.GetStream();
                len = await stream.ReadAsync(buffer, token).ConfigureAwait(false);

                if (len == 0)
                {
                    _client.Close();
                }
            }
            finally
            {
                _semaphoreSlimForSendAndReceive.Release();
            }
        }
        return len;
    }

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
}

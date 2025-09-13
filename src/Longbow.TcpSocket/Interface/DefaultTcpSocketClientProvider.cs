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
    private TcpClient? _tcpClient;
    private Sender? _sender;
    private Receiver? _receiver;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public bool IsConnected => _tcpClient?.Connected ?? false;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public IPEndPoint LocalEndPoint { get; set; } = new IPEndPoint(IPAddress.Any, 0);

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public async ValueTask<bool> ConnectAsync(IPEndPoint endPoint, CancellationToken token = default)
    {
        await CloseAsync();

        _tcpClient = new TcpClient(LocalEndPoint);

        await _tcpClient.ConnectAsync(endPoint, token).ConfigureAwait(false);
        if (_tcpClient.Connected)
        {
            _sender = new Sender(_tcpClient.Client);
            _receiver = new Receiver(_tcpClient.Client);

            if (_tcpClient.Client.LocalEndPoint is IPEndPoint localEndPoint)
            {
                LocalEndPoint = localEndPoint;
            }
        }
        return _tcpClient.Connected;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public async ValueTask<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken token = default)
    {
        var ret = false;
        if (_sender != null)
        {
            await _sender.SendAsync(data, token);
            ret = true;
        }
        return ret;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        var len = 0;
        if (_tcpClient is { Connected: true })
        {
            if (_receiver != null)
            {
                len = await _receiver.ReceiveAsync(buffer, token);
            }

            if (len == 0)
            {
                _tcpClient.Close();
            }
        }
        return len;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public ValueTask CloseAsync()
    {
        if (_tcpClient != null)
        {
            _tcpClient.Close();
            _tcpClient = null;
        }

        if (_sender != null)
        {
            _sender.Dispose();
            _sender = null;
        }

        if (_receiver != null)
        {
            _receiver.Dispose();
            _receiver = null!;
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
        GC.SuppressFinalize(this);
    }
}

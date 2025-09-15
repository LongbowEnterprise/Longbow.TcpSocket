// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using System.Net;

namespace Longbow.TcpSocket;

sealed class DefaultTcpSocketClient(ITcpSocketClientProvider provider) : ITcpSocketClient
{
    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public bool IsConnected => provider.IsConnected;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public TcpSocketClientOptions Options => provider.Options;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public IPEndPoint LocalEndPoint => provider.LocalEndPoint;

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

    private readonly SemaphoreSlim _semaphoreSlimForConnect = new(1, 1);

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

            if (OnConnecting != null)
            {
                await OnConnecting();
            }

            await provider.ConnectAsync(endPoint, token);

            if (OnConnected != null)
            {
                await OnConnected();
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

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public ValueTask<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken token = default)
    {
        provider.ThrowIfNotConnected();

        return provider.SendAsync(data, token);
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        provider.ThrowIfNotConnected();

        return provider.ReceiveAsync(buffer, token);
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public ValueTask CloseAsync()
    {
        return provider.CloseAsync();
    }

    private async ValueTask DisposeAsync(bool disposing)
    {
        if (disposing)
        {
            await CloseAsync();
            await provider.DisposeAsync();
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

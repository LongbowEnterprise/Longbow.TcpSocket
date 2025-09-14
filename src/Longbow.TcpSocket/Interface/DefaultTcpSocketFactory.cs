// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using System.Collections.Concurrent;

namespace Longbow.TcpSocket;

sealed class DefaultTcpSocketFactory(IServiceProvider provider) : ITcpSocketFactory
{
    private readonly ConcurrentDictionary<string, ITcpSocketClient> _pool = new();

    public ITcpSocketClient GetOrCreate(string? name = null, Action<TcpSocketClientOptions>? valueFactory = null) => string.IsNullOrEmpty(name)
        ? CreateClient(valueFactory)
        : _pool.GetOrAdd(name, key => CreateClient(valueFactory));

    private DefaultTcpSocketClient CreateClient(Action<TcpSocketClientOptions>? valueFactory = null)
    {
        var options = new TcpSocketClientOptions();
        valueFactory?.Invoke(options);
        return new DefaultTcpSocketClient(options);
    }

    public ITcpSocketClient? Remove(string name)
    {
        ITcpSocketClient? client = null;
        if (_pool.TryRemove(name, out var c))
        {
            client = c;
        }
        return client;
    }

    private async ValueTask DisposeAsync(bool disposing)
    {
        if (disposing)
        {
            // 释放托管资源
            foreach (var socket in _pool.Values)
            {
                await socket.DisposeAsync();
            }
            _pool.Clear();
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

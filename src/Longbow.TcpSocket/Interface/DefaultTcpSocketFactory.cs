// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Longbow.TcpSocket;

sealed class DefaultTcpSocketFactory(IOptions<TcpSocketClientOptions> options) : ITcpSocketFactory
{
    private readonly ConcurrentDictionary<string, ITcpSocketClient> _pool = new();

    public ITcpSocketClient GetOrCreate(string? name = null, Action<TcpSocketClientOptions>? configureOptions = null) => string.IsNullOrEmpty(name)
        ? CreateClient(configureOptions)
        : _pool.GetOrAdd(name, key => CreateClient(configureOptions));

    private DefaultTcpSocketClient CreateClient(Action<TcpSocketClientOptions>? configureOptions = null)
    {
        configureOptions?.Invoke(options.Value);
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

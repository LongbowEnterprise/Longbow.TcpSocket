// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

namespace Longbow.TcpSocket;

/// <summary>
/// ITcpSocketFactory Interface
/// </summary>
public interface ITcpSocketFactory : IAsyncDisposable
{
    /// <summary>
    /// Retrieves an existing TCP socket client by name or creates a new one using the specified configuration.
    /// </summary>
    /// <param name="name">The unique name of the TCP socket client to retrieve or create. if value is null or empty do not use cache</param>
    /// <param name="configureOptions">A delegate used to configure the <see cref="TcpSocketClientOptions"/> for the new TCP socket client if it does not
    /// already exist. This delegate is invoked only when a new client is created.</param>
    /// <returns>An instance of <see cref="ITcpSocketClient"/> corresponding to the specified name. If the client already exists,
    /// the existing instance is returned; otherwise, a new instance is created and returned.</returns>
    ITcpSocketClient GetOrCreate(string name, Action<TcpSocketClientOptions>? configureOptions = null);

    /// <summary>
    /// Retrieves an existing TCP socket client by name or creates a new one using the specified configuration.
    /// </summary>
    /// <param name="configureOptions">A delegate used to configure the <see cref="TcpSocketClientOptions"/> for the new TCP socket client if it does not
    /// already exist. This delegate is invoked only when a new client is created.</param>
    /// <returns>An instance of <see cref="ITcpSocketClient"/> corresponding to the specified name. If the client already exists,
    /// the existing instance is returned; otherwise, a new instance is created and returned.</returns>
    ITcpSocketClient GetOrCreate(Action<TcpSocketClientOptions>? configureOptions = null);

    /// <summary>
    /// Removes the TCP socket client associated with the specified name.
    /// </summary>
    /// <param name="name">The name of the TCP socket client to remove. Cannot be <see langword="null"/> or empty.</param>
    /// <returns>The removed <see cref="ITcpSocketClient"/> instance if a client with the specified name exists;  otherwise, <see
    /// langword="null"/>.</returns>
    ITcpSocketClient? Remove(string name);
}

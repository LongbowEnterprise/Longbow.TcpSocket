// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using System.Net;

namespace Longbow.TcpSocket;

/// <summary>
/// Represents configuration options for a socket client, including buffer sizes, timeouts, and endpoints.
/// </summary>
/// <remarks>Use this class to configure various settings for a socket client, such as connection timeouts,
/// buffer sizes, and local or remote endpoints. These options allow fine-tuning of socket behavior  to suit specific
/// networking scenarios.</remarks>
public class TcpSocketClientOptions
{
    /// <summary>
    /// Gets or sets the size, in bytes, of the receive buffer used by the connection.
    /// </summary>
    public int ReceiveBufferSize { get; set; } = 1024 * 64;

    /// <summary>
    /// Gets or sets a value indicating whether automatic receiving data is enabled. Default is true.
    /// </summary>
    public bool IsAutoReceive { get; set; } = true;

    /// <summary>
    /// Gets or sets the local endpoint for the socket client. Default value is <see cref="IPAddress.Any"/>
    /// </summary>
    /// <remarks>This property specifies the local network endpoint that the socket client will bind to when establishing a connection.</remarks>
    public IPEndPoint LocalEndPoint { get; set; } = new(IPAddress.Any, 0);

    /// <summary>
    /// Gets or sets a value indicating whether the system should automatically attempt to reconnect  after a connection is lost. Default value is false.
    /// </summary>
    public bool IsAutoReconnect { get; set; }

    /// <summary>
    /// Gets or sets the interval, in milliseconds, between reconnection attempts. Default value is 5000.
    /// </summary>
    public int ReconnectInterval { get; set; } = 5000;

    /// <summary>
    /// Gets or sets a value indicating whether the Nagle algorithm is disabled for the socket connection. Default value is false.
    /// </summary>
    public bool NoDelay { get; set; }
}

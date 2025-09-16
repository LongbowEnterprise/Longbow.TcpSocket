// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

namespace Longbow.TcpSocket;

static class TcpSocketClientOptionsExtensions
{
    public static TcpSocketClientOptions CopyTo(this TcpSocketClientOptions source) => new()
    {
        ReceiveBufferSize = source.ReceiveBufferSize,
        IsAutoReceive = source.IsAutoReceive,
        ConnectTimeout = source.ConnectTimeout,
        SendTimeout = source.SendTimeout,
        ReceiveTimeout = source.ReceiveTimeout,
        LocalEndPoint = source.LocalEndPoint,
        IsAutoReconnect = source.IsAutoReconnect,
        ReconnectInterval = source.ReconnectInterval,
        NoDelay = source.NoDelay
    };
}

// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

namespace Longbow.TcpSocket;

static class TcpSocketClientOptionsExtensions
{
    public static void CopyTo(this TcpSocketClientOptions source, TcpSocketClientOptions target)
    {
        target.ReceiveBufferSize = source.ReceiveBufferSize;
        target.IsAutoReceive = source.IsAutoReceive;
        target.ConnectTimeout = source.ConnectTimeout;
        target.SendTimeout = source.SendTimeout;
        target.ReceiveTimeout = source.ReceiveTimeout;
        target.LocalEndPoint = source.LocalEndPoint;
        target.IsAutoReconnect = source.IsAutoReconnect;
        target.ReconnectInterval = source.ReconnectInterval;
        target.NoDelay = source.NoDelay;
    }
}

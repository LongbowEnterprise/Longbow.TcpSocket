// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

namespace Longbow.TcpSocket;

static class ITcpSocketClientProviderExtensions
{
    public static void ThrowIfNotConnected(this ITcpSocketClientProvider provider)
    {
        if (provider is not { IsConnected: true })
        {
            throw new InvalidOperationException("TCP Socket is not connected");
        }
    }
}

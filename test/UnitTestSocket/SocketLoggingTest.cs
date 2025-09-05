// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using Longbow.Socket.Logging;

namespace UnitTestSocket;

public class SocketLoggingTest
{
    [Fact]
    public void Logger_Ok()
    {
        SocketLogging.LogError(new Exception());
        SocketLogging.LogInformation("Information");
        SocketLogging.LogWarning("Warning");
        SocketLogging.LogDebug("Debug");
    }
}

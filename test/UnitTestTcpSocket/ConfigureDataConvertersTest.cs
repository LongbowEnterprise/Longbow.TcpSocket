// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using Microsoft.Extensions.DependencyInjection;

namespace UnitTestTcpSocket;

public class ConfigureDataConvertersTest
{
    [Fact]
    public void ConfigureDataConverters_Ok()
    {
        var sc = new ServiceCollection();
        sc.ConfigureDataConverters(options =>
        {
        });
    }
}

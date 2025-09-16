// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Runtime.Versioning;

namespace Longbow.TcpSocket;

/// <summary>
/// TcpSocket 扩展方法
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 增加 ITcpSocketFactory 服务
    /// </summary>
    /// <param name="services"></param>
    /// <param name="configureOptions"></param>
    /// <returns></returns>
    [UnsupportedOSPlatform("browser")]
    public static IServiceCollection AddTcpSocketFactory(this IServiceCollection services, Action<TcpSocketClientOptions>? configureOptions = null)
    {
        // 添加 ITcpSocketFactory 服务
        services.TryAddSingleton<ITcpSocketFactory, DefaultTcpSocketFactory>();

        // 增加 ISocketClientProvider 服务
        services.TryAddTransient<ITcpSocketClient, DefaultTcpSocketClient>();

        // 增加全局配置
        services.Configure<TcpSocketClientOptions>(op => configureOptions?.Invoke(op));

        return services;
    }
}

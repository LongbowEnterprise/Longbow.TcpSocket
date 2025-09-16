// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using System.Text;

namespace Longbow.TcpSocket;

/// <summary>
/// <see cref="ITcpSocketClient"/> 扩展方法类
/// </summary>
public static class ITcpSocketClientExtensions
{
    /// <summary>
    /// Sends the specified string content to the connected TCP socket client asynchronously.
    /// </summary>
    /// <remarks>This method converts the provided string content into a byte array using the specified
    /// encoding  (or UTF-8 by default) and sends it to the connected TCP socket client. Ensure the client is connected
    /// before calling this method.</remarks>
    /// <param name="client">The TCP socket client to which the content will be sent. Cannot be <see langword="null"/>.</param>
    /// <param name="content">The string content to send. Cannot be <see langword="null"/> or empty.</param>
    /// <param name="encoding">The character encoding to use for converting the string content to bytes.  If <see langword="null"/>, UTF-8
    /// encoding is used by default.</param>
    /// <param name="token">A <see cref="CancellationToken"/> to observe while waiting for the operation to complete.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> that represents the asynchronous operation.  The result is <see
    /// langword="true"/> if the content was sent successfully; otherwise, <see langword="false"/>.</returns>
    public static ValueTask<bool> SendAsync(this ITcpSocketClient client, string content, Encoding? encoding = null, CancellationToken token = default)
    {
        var buffer = encoding?.GetBytes(content) ?? Encoding.UTF8.GetBytes(content);
        return client.SendAsync(buffer, token);
    }

    /// <summary>
    /// Establishes an asynchronous connection to the specified host and port.
    /// </summary>
    /// <param name="client">The TCP socket client to which the content will be sent. Cannot be <see langword="null"/>.</param>
    /// <param name="ipString">The hostname or IP address of the server to connect to. Cannot be null or empty.</param>
    /// <param name="port">The port number on the server to connect to. Must be a valid port number between 0 and 65535.</param>
    /// <param name="token">An optional <see cref="CancellationToken"/> to cancel the connection attempt. Defaults to <see
    /// langword="default"/> if not provided.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the connection
    /// is successfully established; otherwise, <see langword="false"/>.</returns>
    public static ValueTask<bool> ConnectAsync(this ITcpSocketClient client, string ipString, int port, CancellationToken token = default)
    {
        var endPoint = TcpSocketUtility.ConvertToIpEndPoint(ipString, port);
        return client.ConnectAsync(endPoint, token);
    }

    /// <summary>
    /// 通过指定 <see cref="IDataPackageHandler"/> 数据处理实例，设置数据适配器并配置回调方法，切记使用 <see cref="ITcpSocketClient.RemoveDataPackageAdapter(Func{ReadOnlyMemory{byte}, ValueTask})"/> 移除数据处理委托防止内存泄露
    /// </summary>
    /// <param name="client"><see cref="ITcpSocketClient"/> 实例</param>
    /// <param name="handler"><see cref="IDataPackageHandler"/> 数据处理实例</param>
    /// <param name="callback">回调方法</param>
    public static void AddDataPackageAdapter(this ITcpSocketClient client, IDataPackageHandler handler, Func<ReadOnlyMemory<byte>, ValueTask> callback)
    {
        client.AddDataPackageAdapter(new DataPackageAdapter(handler), callback);
    }

    /// <summary>
    /// 通过指定 <see cref="IDataPackageHandler"/> 数据处理实例，设置数据适配器并配置回调方法。切记使用 <see cref="ITcpSocketClient.RemoveDataPackageAdapter(Func{ReadOnlyMemory{byte}, ValueTask})"/> 移除数据处理委托防止内存泄露
    /// </summary>
    /// <param name="client"><see cref="ITcpSocketClient"/> 实例</param>
    /// <param name="handler"><see cref="IDataPackageHandler"/> 数据处理实例</param>
    /// <param name="callback">回调方法</param>
    public static void AddDataPackageAdapter<TEntity>(this ITcpSocketClient client, IDataPackageHandler handler, Func<TEntity?, ValueTask> callback)
    {
        client.AddDataPackageAdapter(new DataPackageAdapter(handler), callback);
    }

    /// <summary>
    /// 通过指定 <see cref="IDataPackageHandler"/> 数据处理实例，设置数据适配器并配置回调方法。切记使用 <see cref="ITcpSocketClient.RemoveDataPackageAdapter(Func{ReadOnlyMemory{byte}, ValueTask})"/> 移除数据处理委托防止内存泄露
    /// </summary>
    /// <param name="client"><see cref="ITcpSocketClient"/> 实例</param>
    /// <param name="handler"><see cref="IDataPackageHandler"/> 数据处理实例</param>
    /// <param name="converter"><see cref="IDataConverter{TEntity}"/>实例</param>
    /// <param name="callback">回调方法</param>
    public static void AddDataPackageAdapter<TEntity>(this ITcpSocketClient client, IDataPackageHandler handler, IDataConverter<TEntity> converter, Func<TEntity?, ValueTask> callback)
    {
        client.AddDataPackageAdapter(new DataPackageAdapter(handler), converter, callback);
    }
}

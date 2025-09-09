// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Net;
using System.Runtime.Versioning;

namespace Longbow.TcpSocket;

[UnsupportedOSPlatform("browser")]
sealed class DefaultTcpSocketClient(TcpSocketClientOptions options) : IServiceProvider, ITcpSocketClient
{
    /// <summary>
    /// Gets or sets the service provider used to resolve dependencies.
    /// </summary>
    [NotNull]
    public IServiceProvider? ServiceProvider { get; set; }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public bool IsConnected => _socketProvider?.IsConnected ?? false;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public IPEndPoint LocalEndPoint => _socketProvider?.LocalEndPoint ?? new IPEndPoint(IPAddress.Any, 0);

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public Func<ReadOnlyMemory<byte>, ValueTask>? ReceivedCallback { get; set; }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public Func<Task>? OnConnecting { get; set; }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public Func<Task>? OnConnected { get; set; }

    private ILogger? _logger;
    private ITcpSocketClientProvider? _socketProvider;
    private IPEndPoint? _remoteEndPoint;
    private IPEndPoint? _localEndPoint;
    private CancellationTokenSource? _autoReceiveTokenSource;
    private CancellationTokenSource? _reconnectTokenSource;
    private readonly SemaphoreSlim _semaphoreSlimForConnect = new(1, 1);

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="endPoint"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public async ValueTask<bool> ConnectAsync(IPEndPoint endPoint, CancellationToken token = default)
    {
        if (IsConnected)
        {
            return true;
        }

        var reconnect = true;
        var ret = false;

        try
        {
            await _semaphoreSlimForConnect.WaitAsync(token).ConfigureAwait(false);

            if (OnConnecting != null)
            {
                await OnConnecting();
            }

            var connectionToken = GenerateConnectionToken(token);
            _socketProvider ??= ServiceProvider.GetRequiredService<ITcpSocketClientProvider>();
            ret = await ConnectCoreAsync(_socketProvider, endPoint, connectionToken);

            if (OnConnected != null)
            {
                await OnConnected();
            }
        }
        catch (OperationCanceledException ex)
        {
            if (token.IsCancellationRequested)
            {
                Log(LogLevel.Warning, ex, $"TCP Socket connect operation was canceled from {LocalEndPoint} to {endPoint}");
                reconnect = false;
            }
            else
            {
                Log(LogLevel.Warning, ex, $"TCP Socket connect operation timed out from {LocalEndPoint} to {endPoint}");
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, ex, $"TCP Socket connection failed from {LocalEndPoint} to {endPoint}");
        }
        finally
        {
            if (_semaphoreSlimForConnect.CurrentCount == 0)
            {
                _semaphoreSlimForConnect.Release();
            }
        }

        if (reconnect && !ret)
        {
            Reconnect();
        }
        return ret;
    }

    private void Reconnect()
    {
        if (options.IsAutoReconnect == false)
        {
            return;
        }

        if (_remoteEndPoint != null)
        {
            Task.Run(async () =>
            {
                try
                {
                    _reconnectTokenSource ??= new();
                    await Task.Delay(options.ReconnectInterval, _reconnectTokenSource.Token).ConfigureAwait(false);
                    await ConnectAsync(_remoteEndPoint, _reconnectTokenSource.Token).ConfigureAwait(false);
                }
                catch { }
            }, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async ValueTask<bool> ConnectCoreAsync(ITcpSocketClientProvider provider, IPEndPoint endPoint, CancellationToken token)
    {
        // 取消自动接收任务
        if (_autoReceiveTokenSource != null)
        {
            _autoReceiveTokenSource.Cancel();
            _autoReceiveTokenSource.Dispose();
            _autoReceiveTokenSource = null;
        }

        provider.LocalEndPoint = options.LocalEndPoint;

        _localEndPoint = options.LocalEndPoint;
        _remoteEndPoint = endPoint;

        var ret = await provider.ConnectAsync(endPoint, token);

        if (ret)
        {
            _localEndPoint = provider.LocalEndPoint;

            // 开启自动接收数据功能
            if (options.IsAutoReceive)
            {
                _ = Task.Run(AutoReceiveAsync, CancellationToken.None).ConfigureAwait(false);
            }
        }
        return ret;
    }

    private CancellationToken GenerateConnectionToken(CancellationToken token)
    {
        var connectionToken = token;
        if (options.ConnectTimeout > 0)
        {
            // 设置连接超时时间
            var connectTokenSource = new CancellationTokenSource(options.ConnectTimeout);
            connectionToken = CancellationTokenSource.CreateLinkedTokenSource(token, connectTokenSource.Token).Token;
        }
        return connectionToken;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="data"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public async ValueTask<bool> SendAsync(ReadOnlyMemory<byte> data, CancellationToken token = default)
    {
        if (_socketProvider is not { IsConnected: true })
        {
            throw new InvalidOperationException($"TCP Socket is not connected {LocalEndPoint}");
        }

        var ret = false;
        var reconnect = true;
        try
        {
            var sendToken = token;
            if (options.SendTimeout > 0)
            {
                // 设置发送超时时间
                var sendTokenSource = new CancellationTokenSource(options.SendTimeout);
                sendToken = CancellationTokenSource.CreateLinkedTokenSource(token, sendTokenSource.Token).Token;
            }
            ret = await _socketProvider.SendAsync(data, sendToken);
        }
        catch (OperationCanceledException ex)
        {
            if (token.IsCancellationRequested)
            {
                reconnect = false;
                Log(LogLevel.Warning, ex, $"TCP Socket send operation was canceled from {_localEndPoint} to {_remoteEndPoint}");
            }
            else
            {
                Log(LogLevel.Warning, ex, $"TCP Socket send operation timed out from {_localEndPoint} to {_remoteEndPoint}");
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, ex, $"TCP Socket send failed from {_localEndPoint} to {_remoteEndPoint}");
        }

        Log(LogLevel.Information, null, $"Sending data from {_localEndPoint} to {_remoteEndPoint}, Data Length: {data.Length} Data Content: {BitConverter.ToString(data.ToArray())} Result: {ret}");

        if (!ret && reconnect)
        {
            // 如果发送失败并且需要重连则尝试重连
            Reconnect();
        }
        return ret;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public async ValueTask<Memory<byte>> ReceiveAsync(CancellationToken token = default)
    {
        if (_socketProvider is not { IsConnected: true })
        {
            throw new InvalidOperationException($"TCP Socket is not connected {LocalEndPoint}");
        }

        if (options.IsAutoReceive)
        {
            throw new InvalidOperationException("Cannot call ReceiveAsync when IsAutoReceive is true. Use the auto-receive mechanism instead.");
        }

        using var block = MemoryPool<byte>.Shared.Rent(options.ReceiveBufferSize);
        var buffer = block.Memory;
        var len = await ReceiveCoreAsync(_socketProvider, buffer, token);
        if (len == 0)
        {
            Reconnect();
        }
        return buffer[..len];
    }

    private async ValueTask AutoReceiveAsync()
    {
        // 自动接收方法
        _autoReceiveTokenSource ??= new();
        while (_autoReceiveTokenSource is { IsCancellationRequested: false })
        {
            if (_socketProvider is not { IsConnected: true })
            {
                throw new InvalidOperationException($"TCP Socket is not connected {LocalEndPoint}");
            }

            using var block = MemoryPool<byte>.Shared.Rent(options.ReceiveBufferSize);
            var buffer = block.Memory;
            var len = await ReceiveCoreAsync(_socketProvider, buffer, _autoReceiveTokenSource.Token);
            if (len == 0)
            {
                // 远端关闭或者 DisposeAsync 方法被调用时退出
                break;
            }
        }
    }

    private async ValueTask<int> ReceiveCoreAsync(ITcpSocketClientProvider client, Memory<byte> buffer, CancellationToken token)
    {
        var reconnect = true;
        var len = 0;
        try
        {
            var receiveToken = token;
            if (options.ReceiveTimeout > 0)
            {
                // 设置接收超时时间
                var receiveTokenSource = new CancellationTokenSource(options.ReceiveTimeout);
                receiveToken = CancellationTokenSource.CreateLinkedTokenSource(receiveToken, receiveTokenSource.Token).Token;
            }

            len = await client.ReceiveAsync(buffer, receiveToken);
            if (len == 0)
            {
                // 远端主机关闭链路
                Log(LogLevel.Information, null, $"TCP Socket {_localEndPoint} received 0 data closed by {_remoteEndPoint}");
                buffer = Memory<byte>.Empty;
            }
            else
            {
                buffer = buffer[..len];
            }

            if (ReceivedCallback != null)
            {
                // 如果订阅回调则触发回调
                await ReceivedCallback(buffer);
            }
        }
        catch (OperationCanceledException ex)
        {
            if (token.IsCancellationRequested)
            {
                Log(LogLevel.Warning, ex, $"TCP Socket receive operation canceled from {_localEndPoint} to {_remoteEndPoint}");
                reconnect = false;
            }
            else
            {
                Log(LogLevel.Warning, ex, $"TCP Socket receive operation timed out from {_localEndPoint} to {_remoteEndPoint}");
            }
        }
        catch (Exception ex)
        {
            Log(LogLevel.Error, ex, $"TCP Socket receive failed from {_localEndPoint} to {_remoteEndPoint}");
        }

        Log(LogLevel.Information, null, $"Receiving data from {_localEndPoint} to {_remoteEndPoint}, Data Length: {len} Data Content: {BitConverter.ToString(buffer.ToArray())}");

        if (len == 0 && reconnect)
        {
            // 如果接收数据长度为 0 并且需要重连则尝试重连
            Reconnect();
        }
        return len;
    }

    /// <summary>
    /// Logs a message with the specified log level, exception, and additional context.
    /// </summary>
    private void Log(LogLevel logLevel, Exception? ex, string? message)
    {
        if (options.EnableLog)
        {
            _logger ??= ServiceProvider.GetRequiredService<ILogger<DefaultTcpSocketClient>>();
            _logger.Log(logLevel, ex, "{Message}", message);
        }
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public async ValueTask CloseAsync()
    {
        // 取消重连任务
        if (_reconnectTokenSource != null)
        {
            _reconnectTokenSource.Cancel();
            _reconnectTokenSource.Dispose();
            _reconnectTokenSource = null;
        }

        // 取消接收数据的任务
        if (_autoReceiveTokenSource != null)
        {
            _autoReceiveTokenSource.Cancel();
            _autoReceiveTokenSource.Dispose();
            _autoReceiveTokenSource = null;
        }

        if (_socketProvider != null)
        {
            await _socketProvider.CloseAsync();
        }
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    /// <param name="serviceType"></param>
    /// <returns></returns>
    public object? GetService(Type serviceType) => ServiceProvider.GetService(serviceType);

    /// <summary>
    /// Releases the resources used by the current instance of the class.
    /// </summary>
    /// <remarks>This method is called to free both managed and unmanaged resources. If the <paramref
    /// name="disposing"/> parameter is <see langword="true"/>, the method releases managed resources in addition to
    /// unmanaged resources. Override this method in a derived class to provide custom cleanup logic.</remarks>
    /// <param name="disposing"><see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only
    /// unmanaged resources.</param>
    private async ValueTask DisposeAsync(bool disposing)
    {
        if (disposing)
        {
            await CloseAsync();

            if (_socketProvider != null)
            {
                await _socketProvider.DisposeAsync();
                _socketProvider = null;
            }
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

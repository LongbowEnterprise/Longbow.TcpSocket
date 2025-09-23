// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UnitTestTcpSocket;

public class TcpSocketFactoryTest
{
    [Fact]
    public async Task GetOrCreate_Ok()
    {
        // 测试 GetOrCreate 方法创建的 Client 销毁后继续 GetOrCreate 得到的对象是否可用
        var sc = new ServiceCollection();
        sc.AddTcpSocketFactory(op =>
        {
            op.ConnectTimeout = 1000;
        });
        var provider = sc.BuildServiceProvider();
        var factory = provider.GetRequiredService<ITcpSocketFactory>();
        var client1 = factory.GetOrCreate("demo");
        Assert.Equal(client1.Options.LocalEndPoint, client1.LocalEndPoint);
        await client1.CloseAsync();

        var client2 = factory.GetOrCreate("demo", op => op.LocalEndPoint = TcpSocketUtility.ConvertToIpEndPoint("localhost", 0));
        Assert.Equal(client1, client2);

        var ip = Dns.GetHostAddresses(Dns.GetHostName(), AddressFamily.InterNetwork).FirstOrDefault() ?? IPAddress.Loopback;
        var client3 = factory.GetOrCreate("demo1", op => op.LocalEndPoint = TcpSocketUtility.ConvertToIpEndPoint(ip.ToString(), 0));

        // 测试不合格 IP 地址
        var client4 = factory.GetOrCreate("demo2", op => op.LocalEndPoint = TcpSocketUtility.ConvertToIpEndPoint("256.0.0.1", 0));

        var client5 = factory.Remove("demo2");
        Assert.Equal(client4, client5);
        Assert.NotNull(client5);

        await using var client6 = factory.GetOrCreate();
        Assert.NotEqual(client5, client6);

        await using var client7 = factory.GetOrCreate("");
        Assert.NotEqual(client7, client6);

        await client5.DisposeAsync();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task SendAsync_Ok()
    {
        var port = 8881;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        var client = CreateClient();
        Assert.False(client.IsConnected);

        // 连接 TCP Server
        client.Options.ConnectTimeout = 1000;
        await client.ConnectAsync("localhost", port);
        Assert.True(client.IsConnected);
        Assert.NotEqual(client.Options.LocalEndPoint, client.LocalEndPoint);

        // 测试 ConnectAsync 重复连接
        await client.ConnectAsync("localhost", port);

        // 测试 SendAsync 方法发送取消逻辑
        var cst = new CancellationTokenSource();
        cst.Cancel();

        var ex = Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.SendAsync("test", null, cst.Token));
        Assert.NotNull(ex);

        // 测试正常电文
        cst.Dispose();
        cst = new();
        client.Options.SendTimeout = 1000;
        var result = await client.SendAsync("test", Encoding.UTF8, cst.Token);
        Assert.True(result);

        // 关闭连接
        StopTcpServer(server);
    }

    [Fact]
    public async Task ReceiveAsync_Timeout()
    {
        var port = 8888;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        await using var client = CreateClient(configureOptions: op => op.ReceiveTimeout = 100);

        await client.ConnectAsync("localhost", port);

        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);

        // 等待接收超时
        await Task.Delay(120);
        await client.CloseAsync();
    }

    [Fact]
    public async Task ReceiveAsync_Zero()
    {
        var client = CreateClient(configureOptions: op => op.IsAutoReceive = false);

        // 已连接但是启用了自动接收功能时调用 ReceiveAsync 方法会抛出 InvalidOperationException 异常
        var port = 8894;
        var server = StartTcpServer(port, MockZeroPackageAsync);
        await client.ConnectAsync("localhost", port);

        await Assert.ThrowsAnyAsync<Exception>(async () => await client.ReceiveAsync());
        server.Stop();
    }

    [Fact]
    public async Task ReceiveAsync_NotConnected()
    {
        // 未连接时调用 ReceiveAsync 方法会抛出 InvalidOperationException 异常
        var client = CreateClient(configureOptions: op => op.IsAutoReceive = true);
        // 内部未连接 返回 0 字节
        var len = await client.ReceiveAsync(new byte[1024]);
        Assert.Equal(0, len);

        // 已连接但是启用了自动接收功能时调用 ReceiveAsync 方法会抛出 InvalidOperationException 异常
        var port = 8893;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        var connected = await client.ConnectAsync("localhost", port);
        Assert.True(connected);
    }

    [Fact]
    public async Task ReceiveAsync_Ok()
    {
        var onConnecting = false;
        var onConnected = false;
        var port = 8891;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        var client = CreateClient(configureOptions: op => op.IsAutoReceive = false);
        client.OnConnecting = () =>
        {
            onConnecting = true;
            return Task.CompletedTask;
        };
        client.OnConnected = () =>
        {
            onConnected = true;
            return Task.CompletedTask;
        };
        var connected = await client.ConnectAsync("localhost", port);
        Assert.True(connected);
        Assert.True(onConnecting);
        Assert.True(onConnected);

        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        var send = await client.SendAsync(data);
        Assert.True(send);

        // 未设置数据处理器未开启自动接收时，调用 ReceiveAsync 方法获取数据
        // 需要自己处理粘包分包和业务问题
        var buffer = new byte[1024];
        var len = await client.ReceiveAsync(buffer);
        Assert.Equal([1, 2, 3, 4, 5], buffer[0..len]);

        // 由于服务器端模拟了拆包发送第二段数据，所以这里可以再次调用 ReceiveAsync 方法获取第二段数据
        // 调用扩展方法直接获得到接收数据
        var payload = await client.ReceiveAsync();
        Assert.Equal([3, 4], payload.ToArray());
    }

    [Fact]
    public async Task AddDataPackageAdapter_Ok()
    {
        var port = 8896;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        await using var client = CreateClient();
        var tcs = new TaskCompletionSource();
        var receivedBuffer = new byte[128];
        var receivedBuffer2 = new byte[128];

        // 连接 TCP Server
        var connect = await client.ConnectAsync("localhost", port);

        ValueTask ReceivedCallBack(ReadOnlyMemory<byte> buffer)
        {
            // buffer 即是接收到的数据
            buffer.CopyTo(receivedBuffer);
            receivedBuffer = receivedBuffer[..buffer.Length];
            tcs.SetResult();
            return ValueTask.CompletedTask;
        }

        ValueTask ReceivedCallBack2(ReadOnlyMemory<byte> buffer)
        {
            // buffer 即是接收到的数据
            buffer.CopyTo(receivedBuffer2);
            receivedBuffer2 = receivedBuffer2[..buffer.Length];
            return ValueTask.CompletedTask;
        }

        client.AddDataPackageAdapter(new DataPackageAdapter(new FixLengthDataPackageHandler(7)), ReceivedCallBack);

        // 相同 adapter 添加多次
        var adapter = new DataPackageAdapter(new FixLengthDataPackageHandler(7));
        client.AddDataPackageAdapter(adapter, ReceivedCallBack2);
        client.AddDataPackageAdapter(adapter, ReceivedCallBack2);

        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);

        // 等待接收数据处理完成
        await tcs.Task;
        client.RemoveDataPackageAdapter(ReceivedCallBack);
        client.RemoveDataPackageAdapter(ReceivedCallBack2);

        Assert.Null(adapter.ReceivedCallback);
        Assert.Null(client.ReceivedCallback);
    }

    [Fact]
    public async Task SetDataPackageAdapter_Ok()
    {
        var port = 8897;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        var client = CreateClient();
        var tcs = new TaskCompletionSource();
        var receivedBuffer = new byte[128];

        // 连接 TCP Server
        var connect = await client.ConnectAsync("localhost", port);

        client.AddDataPackageAdapter(new DataPackageAdapter(new FixLengthDataPackageHandler(7)), ReceivedCallBack);
        client.AddDataPackageAdapter(new FixLengthDataPackageHandler(7), ReceivedCallBack);

        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);

        // 等待接收数据处理完成
        await tcs.Task;

        ValueTask ReceivedCallBack(ReadOnlyMemory<byte> buffer)
        {
            // buffer 即是接收到的数据
            buffer.CopyTo(receivedBuffer);
            receivedBuffer = receivedBuffer[..buffer.Length];
            tcs.SetResult();
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task SetDataPackageAdapter_Generic()
    {
        var port = 8898;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        await using var client = CreateClient();
        var tcs = new TaskCompletionSource();
        var receivedBuffer = new byte[128];
        MockEntity? entity = null;

        // 连接 TCP Server
        var connect = await client.ConnectAsync("localhost", port);
        ValueTask ReceivedEntityCallBack(MockEntity? t)
        {
            entity = t;
            tcs.TrySetResult();
            return ValueTask.CompletedTask;
        }

        client.AddDataPackageAdapter<MockEntity>(new FixLengthDataPackageHandler(7), ReceivedEntityCallBack);
        client.AddDataPackageAdapter(new FixLengthDataPackageHandler(7), new FloatConverter(), ReceivedEntityCallBack);

        // 相同 adapter 添加多次
        var adapter = new DataPackageAdapter(new FixLengthDataPackageHandler(7));
        client.AddDataPackageAdapter<MockEntity>(adapter, ReceivedEntityCallBack);
        client.AddDataPackageAdapter<MockEntity>(adapter, ReceivedEntityCallBack);

        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);

        // 等待接收数据处理完成
        await tcs.Task;

        client.RemoveDataPackageAdapter<MockEntity>(ReceivedEntityCallBack);
        Assert.Null(adapter.ReceivedCallback);
        Assert.Null(client.ReceivedCallback);
    }

    class FloatConverter : DataConverter<MockEntity>
    {
        public override bool TryConvertTo(ReadOnlyMemory<byte> data, [NotNullWhen(true)] out MockEntity? entity)
        {
            entity = new MockEntity
            {
                Header = data[..2].ToArray(),
                Body = data[2..3].ToArray(),
                Value = 0.1f
            };
            return true;
        }
    }

    private static TcpListener StartTcpServer(int port, Func<TcpClient, Task> handler)
    {
        var server = new TcpListener(IPAddress.Loopback, port);
        server.Start();
        Task.Run(() => AcceptClientsAsync(server, handler));
        return server;
    }

    private static async Task AcceptClientsAsync(TcpListener server, Func<TcpClient, Task> handler)
    {
        while (true)
        {
            var client = await server.AcceptTcpClientAsync();
            _ = Task.Run(() => handler(client));
        }
    }

    private static Task MockMutePackageAsync(TcpClient client)
    {
        return Task.CompletedTask;
    }

    private static Task MockZeroPackageAsync(TcpClient client)
    {
        client.Close();
        return Task.CompletedTask;
    }

    private static async Task MockSplitPackageAsync(TcpClient client)
    {
        using var stream = client.GetStream();
        while (true)
        {
            var buffer = new byte[1024];
            var len = await stream.ReadAsync(buffer);
            if (len == 0)
            {
                break;
            }

            // 回写数据到客户端
            var block = new ReadOnlyMemory<byte>(buffer, 0, len);
            await stream.WriteAsync(block, CancellationToken.None);

            // 模拟延时
            await Task.Delay(50);

            // 模拟拆包发送第二段数据
            await stream.WriteAsync(new byte[] { 0x3, 0x4 }, CancellationToken.None);
        }
    }

    private static void StopTcpServer(TcpListener server)
    {
        server?.Stop();
    }

    private static ITcpSocketClient CreateClient(Action<ServiceCollection>? builder = null, Action<TcpSocketClientOptions>? configureOptions = null)
    {
        var sc = new ServiceCollection();
        sc.AddTcpSocketFactory();
        builder?.Invoke(sc);

        var provider = sc.BuildServiceProvider();
        var factory = provider.GetRequiredService<ITcpSocketFactory>();
        var client = factory.GetOrCreate("test", op =>
        {
            op.LocalEndPoint = TcpSocketUtility.ConvertToIpEndPoint("localhost", 0);
            configureOptions?.Invoke(op);
        });

        return client;
    }

    class MockEntity
    {
        [DataPropertyConverter(Offset = 0, Length = 2)]
        public byte[]? Header { get; set; }

        [DataPropertyConverter(Offset = 2, Length = 3)]
        public byte[]? Body { get; set; }

        [DataPropertyConverter(Offset = 2, Length = 1)]
        public float Value { get; set; }
    }
}

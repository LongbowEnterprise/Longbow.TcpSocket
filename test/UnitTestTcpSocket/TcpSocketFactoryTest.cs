// Copyright (c) Argo Zhang (argo@live.ca). All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
// Website: https://github.com/LongbowExtensions/

using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using UnitTestSocket;

namespace UnitTestTcpSocket;

public class TcpSocketFactoryTest
{
    [Fact]
    public async Task GetOrCreate_Ok()
    {
        // 测试 GetOrCreate 方法创建的 Client 销毁后继续 GetOrCreate 得到的对象是否可用
        var sc = new ServiceCollection();
        sc.AddTcpSocketFactory();
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
        await client.ConnectAsync("localhost", port);
        Assert.True(client.IsConnected);
        Assert.NotEqual(client.Options.LocalEndPoint, client.LocalEndPoint);

        // 测试 SendAsync 方法发送取消逻辑
        var cst = new CancellationTokenSource();
        cst.Cancel();

        var ex = Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.SendAsync("test", null, cst.Token));
        Assert.NotNull(ex);

        // 测试正常电文
        cst.Dispose();
        cst = new();
        var result = await client.SendAsync("test", null, cst.Token);
        Assert.True(result);

        // 关闭连接
        StopTcpServer(server);
    }

    [Fact]
    public async Task ReceiveAsync_Timeout()
    {
        var port = 8888;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        var client = CreateClient(configureOptions: op => op.ReceiveTimeout = 100);

        await client.ConnectAsync("localhost", port);

        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);
        await Task.Delay(220); // 等待接收超时
    }

    [Fact]
    public async Task ReceiveAsync_Cancel()
    {
        var port = 8889;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        var client = CreateClient();
        await client.ConnectAsync("localhost", port);

        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);

        // 通过反射取消令牌
        var type = client.GetType();
        Assert.NotNull(type);

        var fieldInfo = type.GetField("_autoReceiveTokenSource", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(fieldInfo);
        var tokenSource = fieldInfo.GetValue(client) as CancellationTokenSource;
        Assert.NotNull(tokenSource);
        tokenSource.Cancel();
        await Task.Delay(50);
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
        len = await client.ReceiveAsync(buffer);
        Assert.Equal([3, 4], buffer[0..len]);
    }

    [Fact]
    public async Task FixLengthDataPackageHandler_Ok()
    {
        var port = 8884;
        var server = StartTcpServer(port, MockSplitPackageAsync);
        var client = CreateClient();
        var tcs = new TaskCompletionSource();
        var receivedBuffer = new byte[1024];

        // 设置数据适配器
        var adapter = new DataPackageAdapter(new FixLengthDataPackageHandler(7));
        var callback = new Func<ReadOnlyMemory<byte>, ValueTask>(buffer =>
        {
            // buffer 即是接收到的数据
            buffer.CopyTo(receivedBuffer);
            receivedBuffer = receivedBuffer[..buffer.Length];
            tcs.SetResult();
            return ValueTask.CompletedTask;
        });
        client.AddDataPackageAdapter(adapter, callback);

        // 测试 ConnectAsync 方法
        var connect = await client.ConnectAsync("localhost", port);
        Assert.True(connect);
        Assert.True(client.IsConnected);

        // 测试 SendAsync 方法
        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        var result = await client.SendAsync(data);
        Assert.True(result);

        await tcs.Task;
        Assert.Equal([1, 2, 3, 4, 5, 3, 4], receivedBuffer.ToArray());

        // 关闭连接
        await client.CloseAsync();
        StopTcpServer(server);
    }

    [Fact]
    public async Task FixLengthDataPackageHandler_Sticky()
    {
        var port = 8885;
        var server = StartTcpServer(port, MockStickyPackageAsync);
        var client = CreateClient();
        var tcs = new TaskCompletionSource();
        var receivedBuffer = new byte[128];

        // 连接 TCP Server
        var connect = await client.ConnectAsync("localhost", port);

        // 设置数据适配器
        var adapter = new DataPackageAdapter(new FixLengthDataPackageHandler(7));
        client.AddDataPackageAdapter(adapter, buffer =>
        {
            // buffer 即是接收到的数据
            buffer.CopyTo(receivedBuffer);
            receivedBuffer = receivedBuffer[..buffer.Length];
            tcs.SetResult();
            return ValueTask.CompletedTask;
        });

        // 发送数据
        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);

        // 等待接收数据处理完成
        await tcs.Task;

        // 验证接收到的数据
        Assert.Equal([1, 2, 3, 4, 5, 3, 4], receivedBuffer.ToArray());

        // 重置接收缓冲区
        receivedBuffer = new byte[1024];
        tcs = new TaskCompletionSource();

        // 等待第二次数据
        await tcs.Task;

        // 验证第二次收到的数据
        Assert.Equal([2, 2, 3, 4, 5, 6, 7], receivedBuffer.ToArray());
        tcs = new TaskCompletionSource();
        await tcs.Task;

        // 验证第三次收到的数据
        Assert.Equal([3, 2, 3, 4, 5, 6, 7], receivedBuffer.ToArray());

        // 关闭连接
        await client.CloseAsync();
        StopTcpServer(server);
    }

    [Fact]
    public async Task DelimiterDataPackageHandler_Ok()
    {
        var port = 8883;
        var server = StartTcpServer(port, MockDelimiterPackageAsync);
        var client = CreateClient();
        var tcs = new TaskCompletionSource();
        var receivedBuffer = new byte[128];

        // 设置数据适配器
        var adapter = new DataPackageAdapter(new DelimiterDataPackageHandler([13, 10]));
        client.AddDataPackageAdapter(adapter, buffer =>
        {
            // buffer 即是接收到的数据
            buffer.CopyTo(receivedBuffer);
            receivedBuffer = receivedBuffer[..buffer.Length];
            tcs.SetResult();
            return ValueTask.CompletedTask;
        });

        // 连接 TCP Server
        var connect = await client.ConnectAsync("localhost", port);

        // 发送数据
        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);

        // 等待接收数据处理完成
        await tcs.Task;

        // 验证接收到的数据
        Assert.Equal([1, 2, 3, 4, 5, 13, 10], receivedBuffer.ToArray());

        // 等待第二次数据
        receivedBuffer = new byte[1024];
        tcs = new TaskCompletionSource();
        await tcs.Task;

        // 验证接收到的数据
        Assert.Equal([5, 6, 13, 10], receivedBuffer.ToArray());

        // 关闭连接
        await client.CloseAsync();
        StopTcpServer(server);

        var handler = new DelimiterDataPackageHandler("\r\n");
        var ex = Assert.Throws<ArgumentNullException>(() => new DelimiterDataPackageHandler(string.Empty));
        Assert.NotNull(ex);

        ex = Assert.Throws<ArgumentNullException>(() => new DelimiterDataPackageHandler(null!));
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task TryConvertTo_Ok()
    {
        var port = 8886;
        var server = StartTcpServer(port, MockEntityPackageAsync);
        var client = CreateClient();
        var tcs = new TaskCompletionSource();
        MockEntity? entity = null;

        // 设置数据适配器
        var adapter = new DataPackageAdapter(new FixLengthDataPackageHandler(29));
        var callback = new Func<MockEntity?, ValueTask>(t =>
        {
            entity = t;
            tcs.SetResult();
            return ValueTask.CompletedTask;
        });
        client.AddDataPackageAdapter(adapter, callback);

        // 连接 TCP Server
        var connect = await client.ConnectAsync("localhost", port);

        // 发送数据
        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);
        await tcs.Task;

        Assert.NotNull(entity);
        Assert.Equal([1, 2, 3, 4, 5], entity.Header);
        Assert.Equal([3, 4], entity.Body);

        // byte
        Assert.Equal(0x1, entity.Value15);

        // string
        Assert.Equal("1", entity.Value1);

        // string
        Assert.Equal("1", entity.Value14);

        // int
        Assert.Equal(9, entity.Value2);

        // long
        Assert.Equal(16, entity.Value3);

        // double
        Assert.Equal(3.14, entity.Value4);

        // single
        Assert.NotEqual(0, entity.Value5);

        // short
        Assert.Equal(0x23, entity.Value6);

        // ushort
        Assert.Equal(0x24, entity.Value7);

        // uint
        Assert.Equal((uint)0x25, entity.Value8);

        // ulong
        Assert.Equal((ulong)0x26, entity.Value9);

        // bool
        Assert.True(entity.Value10);

        // enum
        Assert.Equal(EnumEducation.Middle, entity.Value11);

        // foo
        Assert.NotNull(entity.Value12);
        Assert.Equal(0x29, entity.Value12.Id);
        Assert.Equal("test", entity.Value12.Name);

        // no attribute
        Assert.Null(entity.Value13);
        client.RemoveDataPackageAdapter(callback);

        // 测试 SocketDataConverter 标签功能
        tcs = new TaskCompletionSource();
        client.AddDataPackageAdapter<MockEntity>(adapter, t =>
        {
            entity = t;
            tcs.SetResult();
            return ValueTask.CompletedTask;
        });
        await client.SendAsync(data);
        await tcs.Task;

        Assert.NotNull(entity);
        Assert.Equal([1, 2, 3, 4, 5], entity.Header);

        // 测试 SetDataPackageAdapter 泛型无标签情况，内部使用 DataConverter 转换器
        tcs = new TaskCompletionSource();
        NoConvertEntity? noConvertEntity = null;
        client.AddDataPackageAdapter<NoConvertEntity>(adapter, t =>
        {
            noConvertEntity = t;
            tcs.SetResult();
            return ValueTask.CompletedTask;
        });
        await client.SendAsync(data);
        await tcs.Task;
        Assert.NotNull(noConvertEntity);

        var converter = new MockSocketDataConverter();
        var result = converter.TryConvertTo(new byte[] { 0x1, 0x2 }, out var t);
        Assert.False(result);

        server.Stop();
    }

    [Fact]
    public async Task TryGetTypeConverter_Ok()
    {
        // 测试服务配置转换器
        var port = 8895;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        var client = CreateClient();
        var tcs = new TaskCompletionSource();
        var receivedBuffer = new byte[128];

        // 连接 TCP Server
        var connect = await client.ConnectAsync("localhost", port);

        // 设置数据适配器
        var adapter = new DataPackageAdapter(new FixLengthDataPackageHandler(7));

        OptionConvertEntity? entity = null;
        client.AddDataPackageAdapter<OptionConvertEntity>(adapter, data =>
        {
            // buffer 即是接收到的数据
            entity = data;
            tcs.SetResult();
            return ValueTask.CompletedTask;
        });

        // 发送数据
        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);

        // 等待接收数据处理完成
        await tcs.Task;
        Assert.NotNull(entity);
        Assert.Equal([1, 2], entity.Header);
        Assert.Equal([3, 4, 5], entity.Body);

        server.Stop();
    }

    [Fact]
    public async Task AddDataPackageAdapter_Ok()
    {
        var port = 8896;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        var client = CreateClient();
        var tcs = new TaskCompletionSource();
        var receivedBuffer = new byte[128];
        var receivedBuffer2 = new byte[128];

        // 连接 TCP Server
        var connect = await client.ConnectAsync("localhost", port);

        client.AddDataPackageAdapter(new DataPackageAdapter(new FixLengthDataPackageHandler(7)), ReceivedCallBack);
        client.AddDataPackageAdapter(new DataPackageAdapter(new FixLengthDataPackageHandler(7)), ReceivedCallBack2);

        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);

        // 等待接收数据处理完成
        await tcs.Task;
        client.RemoveDataPackageAdapter(ReceivedCallBack);
        client.RemoveDataPackageAdapter(ReceivedCallBack2);

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

        var client = CreateClient();
        var tcs = new TaskCompletionSource();
        var receivedBuffer = new byte[128];

        // 连接 TCP Server
        var connect = await client.ConnectAsync("localhost", port);
        ValueTask ReceivedEntityCallBack(MockEntity? entity)
        {
            tcs.TrySetResult();
            return ValueTask.CompletedTask;
        }

        client.AddDataPackageAdapter<MockEntity>(new FixLengthDataPackageHandler(7), ReceivedEntityCallBack);

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

    [Fact]
    public async Task Convert_Ok()
    {
        var port = 8899;
        var server = StartTcpServer(port, MockSplitPackageAsync);

        var client = CreateClient();
        var tcs = new TaskCompletionSource();
        var receivedBuffer = new byte[128];
        MockConverterEntity? entity = null;

        // 连接 TCP Server
        var connect = await client.ConnectAsync("localhost", port);

        client.AddDataPackageAdapter<MockConverterEntity>(new FixLengthDataPackageHandler(7), ReceivedCallBack);

        var data = new ReadOnlyMemory<byte>([1, 2, 3, 4, 5]);
        await client.SendAsync(data);

        // 等待接收数据处理完成
        await tcs.Task;

        // 验证实体类不为空
        Assert.NotNull(entity);
        Assert.Equal("3.14", entity.Value1.ToString("#.##"));

        ValueTask ReceivedCallBack(MockConverterEntity? data)
        {
            entity = data;
            tcs.SetResult();
            return ValueTask.CompletedTask;
        }
    }

    class MockConverterEntity
    {
        [DataPropertyConverter(Offset = 0, Length = 5)]
        public byte[]? Header { get; set; }

        [DataPropertyConverter(Offset = 5, Length = 2)]
        public byte[]? Body { get; set; }

        [DataPropertyConverter(Offset = 5, Length = 1, ConverterType = typeof(FloatConverter), ConverterParameters = [0.01f])]
        public float Value1 { get; set; }
    }

    class FloatConverter(float rate) : IDataPropertyConverter
    {
        public object? Convert(ReadOnlyMemory<byte> data)
        {
            return (float)Math.Round(314 * rate, 2);
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

    private static async Task MockDelimiterPackageAsync(TcpClient client)
    {
        using var stream = client.GetStream();
        while (true)
        {
            var buffer = new byte[10240];
            var len = await stream.ReadAsync(buffer);
            if (len == 0)
            {
                break;
            }

            // 回写数据到客户端
            var block = new ReadOnlyMemory<byte>(buffer, 0, len);
            await stream.WriteAsync(block, CancellationToken.None);

            await Task.Delay(20);

            // 模拟拆包发送第二段数据
            await stream.WriteAsync(new byte[] { 13, 10, 0x5, 0x6, 13, 10 }, CancellationToken.None);
        }
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

    private static async Task MockEntityPackageAsync(TcpClient client)
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
            await stream.WriteAsync(new byte[] {
                0x01, 0x02, 0x03, 0x04, 0x05,
                0x03, 0x04, 0x31, 0x09, 0x10,
                0x40, 0x09, 0x1E, 0xB8, 0x51,
                0xEB, 0x85, 0x1F, 0x40, 0x49,
                0x0F, 0xDB, 0x23, 0x24, 0x25,
                0x26, 0x01, 0x01, 0x29
            }, CancellationToken.None);
        }
    }

    private static async Task MockStickyPackageAsync(TcpClient client)
    {
        using var stream = client.GetStream();
        while (true)
        {
            var buffer = new byte[10240];
            var len = await stream.ReadAsync(buffer);
            if (len == 0)
            {
                break;
            }

            // 回写数据到客户端
            var block = new ReadOnlyMemory<byte>(buffer, 0, len);
            await stream.WriteAsync(block, CancellationToken.None);

            // 模拟延时
            await Task.Delay(10);

            // 模拟拆包发送第二段数据
            await stream.WriteAsync(new byte[] { 0x3, 0x4, 0x2, 0x2 }, CancellationToken.None);

            // 模拟延时
            await Task.Delay(10);

            // 模拟粘包发送后续数据
            await stream.WriteAsync(new byte[] { 0x3, 0x4, 0x5, 0x6, 0x7, 0x3, 0x2, 0x3, 0x4, 0x5, 0x6, 0x7, 0x1 }, CancellationToken.None);
        }
    }

    private static async Task LoopSendPackageAsync(TcpClient client)
    {
        using var stream = client.GetStream();
        while (true)
        {
            // 模拟发送数据
            var data = new byte[] { 1, 2, 3, 4, 5 };
            await stream.WriteAsync(data, CancellationToken.None);
            // 模拟延时
            await Task.Delay(500);
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
        [DataPropertyConverter(Offset = 0, Length = 5)]
        public byte[]? Header { get; set; }

        [DataPropertyConverter(Offset = 5, Length = 2)]
        public byte[]? Body { get; set; }

        [DataPropertyConverter(Offset = 7, Length = 1, EncodingName = "utf-8")]
        public string? Value1 { get; set; }

        [DataPropertyConverter(Offset = 8, Length = 1)]
        public int Value2 { get; set; }

        [DataPropertyConverter(Offset = 9, Length = 1)]
        public long Value3 { get; set; }

        [DataPropertyConverter(Offset = 10, Length = 8)]
        public double Value4 { get; set; }

        [DataPropertyConverter(Offset = 18, Length = 4)]
        public float Value5 { get; set; }

        [DataPropertyConverter(Offset = 22, Length = 1)]
        public short Value6 { get; set; }

        [DataPropertyConverter(Offset = 23, Length = 1)]
        public ushort Value7 { get; set; }

        [DataPropertyConverter(Offset = 24, Length = 1)]
        public uint Value8 { get; set; }

        [DataPropertyConverter(Offset = 25, Length = 1)]
        public ulong Value9 { get; set; }

        [DataPropertyConverter(Offset = 26, Length = 1)]
        public bool Value10 { get; set; }

        [DataPropertyConverter(Offset = 27, Length = 1)]
        public EnumEducation Value11 { get; set; }

        [DataPropertyConverter(Offset = 28, Length = 1, ConverterType = typeof(FooConverter), ConverterParameters = ["test"])]
        public Foo? Value12 { get; set; }

        [DataPropertyConverter(Offset = 7, Length = 1)]
        public string? Value14 { get; set; }

        public string? Value13 { get; set; }

        [DataPropertyConverter(Offset = 0, Length = 1)]
        public byte Value15 { get; set; }
    }

    class MockSocketDataConverter : DataConverter<MockEntity>
    {
        protected override bool Parse(ReadOnlyMemory<byte> data, MockEntity entity)
        {
            return false;
        }
    }

    class MockNullConverter : IDataPropertyConverter
    {
        public object? Convert(ReadOnlyMemory<byte> data)
        {
            return null;
        }
    }

    class FooConverter(string name) : IDataPropertyConverter
    {
        public object? Convert(ReadOnlyMemory<byte> data)
        {
            return new Foo() { Id = data.Span[0], Name = name };
        }
    }

    class NoConvertEntity
    {
        public byte[]? Header { get; set; }

        public byte[]? Body { get; set; }
    }

    class OptionConvertEntity
    {
        public byte[]? Header { get; set; }

        public byte[]? Body { get; set; }
    }
}

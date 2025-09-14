# Longbow.TcpSocket

`Longbow.TcpSocket` 是一个基于 .NET 平台的 TCP 套接字通信库，提供了简单易用的 API 来进行异步 TCP 通信。它支持自动接收、自动重连、数据包适配器等功能，适用于构建高性能的网络通信应用。

## 🚀 特性

- **异步通信**：使用 `ValueTask` 提供高性能的异步 TCP 通信。
- **自动接收**：支持自动接收数据流，简化数据处理逻辑。
- **自动重连**：在连接断开后可以自动尝试重新连接。
- **数据包处理器**：处理粘包、分包等问题。
- **数据包适配器**：支持自定义数据包解析逻辑。
- **日志支持**：可选的日志记录功能，便于调试和监控。
- **依赖注入集成**：与 .NET 依赖注入框架无缝集成。

## 📦 安装

你可以通过 NuGet 安装 `Longbow.TcpSocket`：

```bash
dotnet add package Longbow.TcpSocket
```

## 🛠️ 快速开始

### 1. 注册服务

在 `Startup.cs` 或 `Program.cs` 中注册服务：

```csharp
services.AddTcpSocketFactory();
```

然后通过 `ITcpSocketFactory` 获取或创建客户端实例：

```csharp
var factory = serviceProvider.GetRequiredService<ITcpSocketFactory>();
var client = factory.GetOrCreate("myClient", options => 
{
    options.IsAutoReconnect = true;
});
```

### 创建 TCP 客户端并连接服务器

```csharp
using Longbow.TcpSocket;

var factory = serviceProvider.GetRequiredService<ITcpSocketFactory>();
var client = factory.GetOrCreate("myClient", options => 
{
    options.IsAutoReconnect = true;
});

client.ReceivedCallback = async (data) =>
{
    Console.WriteLine($"Received: {Encoding.UTF8.GetString(data)}");
};

await client.ConnectAsync(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8080));
```

### 使用数据包适配器处理自定义数据格式

```csharp

using Longbow.TcpSocket;

var factory = serviceProvider.GetRequiredService<ITcpSocketFactory>();
var client = factory.GetOrCreate("myClient", options => 
{
    options.IsAutoReconnect = true;
});

// 设置数据适配器
client.AddDataPackageAdapter<MockEntity>(new FixLengthDataPackageHandler(12), OnReceive);

// 连接远端
await client.ConnectAsync(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8080));

Task OnReceive(MockEntity entity)
{
    Console.WriteLine($"Received Entity: Id={entity.Id}, Name={entity.Name}");
    return Task.CompletedTask;
}

[DataTypeConverter(Type = typeof(DataConverter<MockEntity>))]
class MockEntity
{
    [DataPropertyConverter(Type = typeof(int), Offset = 4, Length = 2)]
    public int Id { get; set; }

    [DataPropertyConverter(Type = typeof(string), Offset = 6, Length = 4, EncodingName = "utf-8")]
    public string? Name { get; set; }
}
```

## 🤝 贡献

欢迎贡献代码和文档！请参考 [CONTRIBUTING.md](CONTRIBUTING.md) 获取更多信息。

## 📄 许可证

本项目采用 [Apache License](LICENSE)，请查看 `LICENSE` 文件以获取详细信息。

## 🔗 相关链接

- [Github 项目主页](https://github.com/LongbowEnterprise/Longbow.TcpSocket?wt.mc_id=DT-MVP-5004174)
- [NuGet 包](https://www.nuget.org/packages/Longbow.TcpSocket?wt.mc_id=DT-MVP-5004174)

## 📞 联系方式

如需联系开发者，请查看项目主页或提交问题到 [Github Issues](https://github.com/LongbowEnterprise/Longbow.TcpSocket/issues?wt.mc_id=DT-MVP-5004174)。

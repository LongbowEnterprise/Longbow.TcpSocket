# Longbow.TcpSocket

`Longbow.TcpSocket` is a TCP socket communication library based on the .NET platform, providing a simple and easy-to-use API for asynchronous TCP communication. It supports automatic receiving, auto-reconnect, packet adapters, and more, making it suitable for building high-performance network communication applications.

## 🚀 Features

- **Asynchronous Communication**: Uses `ValueTask` for high-performance asynchronous TCP communication.
- **Automatic Receiving**: Supports automatic data stream reception, simplifying data processing logic.
- **Auto-Reconnect**: Automatically attempts to reconnect when the connection is lost.
- **Packet Handler**: Handles sticky packets and packet splitting issues.
- **Packet Adapter**: Supports custom packet parsing logic.
- **Logging Support**: Optional logging functionality for debugging and monitoring.
- **Dependency Injection Integration**: Seamlessly integrates with .NET dependency injection frameworks.

## 📦 Installation

You can install `Longbow.TcpSocket` via NuGet:

```bash
dotnet add package Longbow.TcpSocket
```

## 🛠️ Quick Start

### 1. Register Services

Register the service in `Startup.cs` or `Program.cs`:

```csharp
services.AddTcpSocketFactory();
```

Then obtain or create a client instance via `ITcpSocketFactory`:

```csharp
var factory = serviceProvider.GetRequiredService<ITcpSocketFactory>();
var client = factory.GetOrCreate("myClient", options => 
{
    options.IsAutoReconnect = true;
});
```

### Create a TCP Client and Connect to Server

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

### Use Packet Adapter for Custom Data Formats

```csharp
using Longbow.TcpSocket;

var factory = serviceProvider.GetRequiredService<ITcpSocketFactory>();
var client = factory.GetOrCreate("myClient", options => 
{
    options.IsAutoReconnect = true;
});

// Set packet adapter
client.AddDataPackageAdapter<MockEntity>(new FixLengthDataPackageHandler(12), OnReceive);

// Connect to remote
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

## 🤝 Contributing

Contributions to code and documentation are welcome! Please refer to [CONTRIBUTING.md](CONTRIBUTING.md) for more information.

## 📄 License

This project is licensed under the [Apache License](LICENSE). Please see the `LICENSE` file for details.

## 🔗 Related Links

- [Gitee Project Homepage](https://gitee.com/LongbowEnterprise/Longbow.TcpSocket)
- [Github Project Homepage](https://github.com/LongbowEnterprise/Longbow.TcpSocket)
- [NuGet Package](https://www.nuget.org/packages/Longbow.TcpSocket)

## 📞 Contact

To contact the developers, please visit the project homepage or submit issues to [Gitee Issues](https://gitee.com/LongbowEnterprise/Longbow.TcpSocket/issues)
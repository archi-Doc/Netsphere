## Netsphere is a network library for C#
![Nuget](https://img.shields.io/nuget/v/Netsphere) ![Build and Test](https://github.com/archi-Doc/Netsphere/workflows/Build%20and%20Test/badge.svg)

- **Netsphere** is a transport protocol based on UDP.

- Very versatile and easy to use.

- Covers a wide range of network needs.

- Full serialization features integrated with [Tinyhand](https://github.com/archi-Doc/Tinyhand).

  


## Table of Contents

- [Requirements](#requirements)
- [Quick Start](#quick-start)
- [Configuration](#configuration)



## Requirements

**Visual Studio 2026** or later for Source Generator V2.

**C# 14** or later for generated codes.

**.NET 10** or later target framework.



## Quick start

Install **Netsphere** using Package Manager Console.

```
Install-Package Netsphere
```



This is a small example code to use Netsphere.

First, define an interface shared between the client and server.

```csharp
[NetService] // Annotate NetService attribute.
public interface ITestService : INetService // NetService must inherit from INetService.
{
    Task<string?> DoubleString(string input); // Declare the service method.
    // Ensure that both arguments and return values are serializable by Tinyhand serializer, and the return type must be Task or Task<T> or Task or Task<TResult>.

    Task<int> Sum(int x, int y); // Calculates the sum of two integers.

    Task<NetResultAndValue<int>> Random(); // Gets a random integer value wrapped in a <see cref="NetResultAndValue{T}"/>.

    Task<NetResult> Disable(); // Disables the service.
}
```



On the client side:

Create an instance, connect to the server, obtain the service interface, and call the function.

```csharp
var unit = builder.Build(); // Create a NetUnit unit that implements communication functionality.
await unit.Run(new NetOptions(), true); // Execute the created unit with default options.

var netUnit = unit.Context.ServiceProvider.GetRequiredService<NetUnit>(); // Get a NetUnit instance.
// using (var connection = await netUnit.NetTerminal.UnsafeConnect(new(IPAddress.Loopback, 1981)))
var netNode = NetNode.Loopback(1981, "(e:XWLus_KiQ3AaNVeBDBp3qaot8wQEbmzlHD3Wkg8cWmXZ5egP)");
using (var connection = await netUnit.NetTerminal.Connect(netNode!))
{// Connect to the server's address (loopback address).
    // All communication in Netsphere is encrypted, and connecting by specifying only the address is not recommended due to the risk of man-in-the-middle attacks.
    if (connection is null)
    {
        await Console.Out.WriteLineAsync("No connection");
    }
    else
    {
        var service = connection.GetService<ITestService>(); // Retrieve an instance of the target service.
        var input = "Nupo";
        var output = await service.DoubleString(input); // Arguments are sent to the server through the Tinyhand serializer, processed, and the results are received.
        await Console.Out.WriteLineAsync($"{input} -> {output}");

        var sum = await service.Sum(1, 2); // // Get the sum of 1 and 2, but it is not implemented on the server side.
        await Console.Out.WriteLineAsync($"1 + 2 = {sum}"); // 0

        var result = await service.Random();
        await Console.Out.WriteLineAsync($"{result}");
        await service.Disable();
        result = await service.Random();
        await Console.Out.WriteLineAsync($"{result}");
    }
}

await unit.Terminate(); // Perform the termination process for the unit.
```



On the server side:

Define a class that implements the interface and annotate it with `NetObject` attribute.

```csharp
[NetObject] // Annotate NetObject attribute.
internal class TestServiceAgent : ITestService
{
    private readonly int number = RandomVault.Default.NextInt31();

    async Task<string?> ITestService.DoubleString(string input)
        => input + input;

    async Task<int> ITestService.Sum(int x, int y)
        => x + y;

    Task<NetResultAndValue<int>> ITestService.Random()
        => Task.FromResult(new NetResultAndValue<int>(this.number));

    async Task<NetResult> ITestService.Disable()
    {
        TransmissionContext.Current.ServerConnection.GetContext().DisableNetService<ITestService>();
        return NetResult.Success;
    }
}
```

Create a builder to instantiate, register Options and Services. From the builder, you create a Unit and execute it.

```csharp
// Create a NetUnit builder.
var builder = new NetUnit.Builder()
    .Configure(context =>
    {
    })
    .PostConfigure(context =>
    {
        context.SetOptions(context.GetOptions<NetOptions>() with
        {
            NodeName = "Test server",
            Port = 1981, // Specify the port number.
            NodeSecretKey = "!!!m6Ao8Rkgsrn1-EqG_kzZgrKmWXt5orPpHAz6DbSaAfUmlLCN!!!(e:XWLus_KiQ3AaNVeBDBp3qaot8wQEbmzlHD3Wkg8cWmXZ5egP)", // Test Private key.
            EnablePing = true,
            EnableServer = true,
        });
    });

var unit = builder.Build(); // Create a unit that provides network functionality.
var options = unit.Context.ServiceProvider.GetRequiredService<NetOptions>();
await unit.Run(options, true); // Execute the created unit with the specified options.

await Console.Out.WriteLineAsync(options.ToString()); // Display the NetOptions.
var netBase = unit.Context.ServiceProvider.GetRequiredService<NetBase>();
var node = new NetNode(new(IPAddress.Loopback, (ushort)options.Port), netBase.NodePublicKey);

// Specify which NetService should be enabled by default when a client connects.
var netTerminal = unit.Context.ServiceProvider.GetRequiredService<NetTerminal>();
netTerminal.Services.EnableNetService<ITestService>();

await Console.Out.WriteLineAsync($"{options.NodeName}: {node.ToString()}");
await Console.Out.WriteLineAsync("Ctrl+C to exit");
await ThreadCore.Root.Delay(Timeout.InfiniteTimeSpan); // Wait until the server shuts down.
await unit.Terminate(); // Perform the termination process for the unit.
```



## Instance management

- **Service object**: An instance is created for each connection by the DI container.
- **Service filter**: Generated for each agent type (not for each instance).



## Adding NetService

**NetService** is the core functionality of Netsphere and is designed to be as easy to use as possible. However, due to its nature, several steps are required when using it:

1. Define the interface shared between the client and server.

   ```csharp
   [NetService]
   public interface ITestService : INetService
   {
       Task<string?> DoubleString(string input);
   }
   ```

   

2. Implement the NetService agent (implementation class) on the server side.
   ```csharp
   [NetObject]
   internal class TestServiceAgent : ITestService
   {
       async Task<string?> ITestService.DoubleString(string input)
           => input + input;
   }
   ```

   

3. Specify which NetService should be enabled by default when a client connects.

   ```csharp
   var netTerminal = unit.Context.ServiceProvider.GetRequiredService<NetTerminal>();
   netTerminal.Services.EnableNetService<ITestService>();
   ```
   
   

4. You can also disable or enable the NetService on the server side.
   ```csharp
   async Task<NetResult> ITestService.Disable()
   {
       TransmissionContext.Current.ServerConnection.GetContext().DisableNetService<ITestService>();
       return NetResult.Success;
   }
   ```

   

## Checklist for NetService

- Check whether the service object can be created by the DI container.

- Check whether the NetService is enabled via `NetTerminal.Services`.

  

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
var builder = new NetUnit.Builder() // Create a NetUnit builder.
    .SetupOptions<NetOptions>((context, options) =>
    {// Modify NetOptions.
        options.NodeName = "Test server";
        options.Port = 1999; // Specify the port number.
        options.EnableEssential = true; // Required when using functions such as UnsafeGetNetNode() or Ping.
        options.EnableServer = true;
    })
    .Configure(context =>
    {
        context.Services.AddTransient<TestServiceAgent>(); // Register the service implementation. If a default constructor is available, an instance will be automatically created.
    })
    .ConfigureNetsphere(context =>
    {// Register the services provided by the server.
        context.AddNetService<ITestService, TestServiceAgent>();
        context.AddNetService<ITestService2, TestServiceAgent>();
    });

var unit = builder.Build(); // Create a unit that provides network functionality.
var options = unit.Context.ServiceProvider.GetRequiredService<NetOptions>();
await Console.Out.WriteLineAsync(options.ToString()); // Display the NetOptions.

// It is possible to unregister services, but frequent changes are not recommended (as the service table will be rebuilt). If frequent changes are necessary, consider using NetFilter or modifying the processing in the implementation class.
var netTerminal = unit.Context.ServiceProvider.GetRequiredService<NetTerminal>();
netTerminal.Services.Unregister<ITestService2>();

await unit.Run(options, true); // Execute the created unit with the specified options.
await Console.Out.WriteLineAsync("Server: Ctrl+C to exit");
await ThreadCore.Root.Delay(100_000); // Wait until the server shuts down.
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
   
   

4. On the server side, you can also disable or enable the NetService.
   ```csharp
   async Task<NetResult> ITestService.Disable()
   {
       TransmissionContext.Current.ServerConnection.GetContext().DisableNetService<ITestService>();
       return NetResult.Success;
   }
   ```

   

## Checklist for NetService

- Ensure that NetService is registered on the server (`Context.AddNetService()` or `NetTerminal.Services.Register()`).
- Verify that the Service object is registered in the DI container.
- Check if the DI container can create instances (ensure no injection errors occur).

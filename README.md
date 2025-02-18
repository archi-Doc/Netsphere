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

**Visual Studio 2022** or later for Source Generator V2.

**C# 13** or later for generated codes.

**.NET 9** or later target framework.



## Quick start

Install **Netsphere** using Package Manager Console.

```
Install-Package Netsphere
```



This is a small example code to use Netsphere.

First, define an interface shared between the client and server.

```csharp
[NetServiceInterface] // Annotate NetServiceInterface attribute.
public interface ITestService : INetService // An interface for NetService must inherit from INetService.
{
    NetTask<string?> DoubleString(string input); // Declare the service method.
    // Ensure that both arguments and return values are serializable by Tinyhand serializer, and the return type must be NetTask or NetTask<T> or Task or Task<TResult>.
}
```



On the client side:

Create an instance, connect to the server, obtain the service interface, and call the function.

```csharp
var unit = new NetControl.Builder().Build(); // Create a NetControl unit that implements communication functionality.
await unit.Run(new NetOptions(), true); // Execute the created unit with default options.

var netControl = unit.Context.ServiceProvider.GetRequiredService<NetControl>(); // Get a NetControl instance.
using (var connection = await netControl.NetTerminal.UnsafeConnect(new(IPAddress.Loopback, 1981)))
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
    }
}

await unit.Terminate(); // Perform the termination process for the unit.
```



On the server side:

Define a class that implements the interface and annotate it with `NetServiceObject` attribute.

```csharp
[NetServiceObject] // Annotate NetServiceObject attribute.
internal class TestServiceImpl : ITestService
{
    async NetTask<string?> ITestService.DoubleString(string input)
        => input + input; // Simply repeat a string twice and return it.
}
```

Create a builder to instantiate, register Options and Services. From the builder, you create a Unit and execute it.

```csharp
var builder = new NetControl.Builder() // Create a NetControl builder.
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

- **Service agent**: An instance is created for each connection by the DI container.
- **Service filter**: Generated for each agent type (not for each instance).



## Adding NetService

**NetService** is the core functionality of Netsphere and is designed to be as easy to use as possible. However, due to its nature, several steps are required when using it:

1. Define the interface shared between the client and server.

   ```csharp
   [NetServiceInterface]
   public interface ITestService : INetService
   {
       Task<string?> DoubleString(string input);
   }
   ```

   

2. Implement the NetService agent (implementation class) on the server side.
   ```csharp
   [NetServiceObject]
   internal class TestServiceAgent : ITestService
   {
       async NetTask<string?> ITestService.DoubleString(string input)
           => input + input;
   }
   ```

   

3. Add the network service to the server.

   ```csharp
   .ConfigureNetsphere(context =>
   {
       context.AddNetService<ITestService, TestServiceAgent>();
   });
   ```

   It can also be dynamically added via NetTerminal.
   ```csharp
   netTerminal.Services.Register<ITestService, TestServiceAgent>();
   ```

   

4. Register the agent class in the DI container. If forgotten, it will be registered as Transient when the network service is registered.
   ```csharp
   .Configure(context =>
   {
       context.Services.AddTransient<TestServiceAgent>();
   })
   ```

   


// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using Arc.Threading;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;

namespace QuickStart;

public class Program
{
    public static async Task Main()
    {
        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {// Console window closing or process terminated.
            ThreadCore.Root.Terminate(); // Send a termination signal to the root.
            ThreadCore.Root.TerminationEvent.WaitOne(2_000); // Wait until the termination process is complete (#1).
        };

        Console.CancelKeyPress += (s, e) =>
        {// Ctrl+C pressed
            e.Cancel = true;
            ThreadCore.Root.Terminate(); // Send a termination signal to the root.
        };

        var builder = new NetControl.Builder() // Create a NetControl builder.
            .SetupOptions<NetOptions>((context, options) =>
            {// Modify NetOptions
                options.NodeName = "Test server";
                options.Port = 1981; // Specify the port number.
                options.NodeSecretKey = "!!!m6Ao8Rkgsrn1-EqG_kzZgrKmWXt5orPpHAz6DbSaAfUmlLCN!!!(e:XWLus_KiQ3AaNVeBDBp3qaot8wQEbmzlHD3Wkg8cWmXZ5egP)"; // Test Private key.
                options.EnablePing = true;
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
        await unit.Run(options, true); // Execute the created unit with the specified options.

        await Console.Out.WriteLineAsync(options.ToString()); // Display the NetOptions.
        var netBase = unit.Context.ServiceProvider.GetRequiredService<NetBase>();
        var node = new NetNode(new(IPAddress.Loopback, (ushort)options.Port), netBase.NodePublicKey);

        // It is possible to unregister services, but frequent changes are not recommended (as the service table will be rebuilt). If frequent changes are necessary, consider using NetFilter or modifying the processing in the implementation class.
        var netTerminal = unit.Context.ServiceProvider.GetRequiredService<NetTerminal>();
        netTerminal.Services.Unregister<ITestService2>();

        await Console.Out.WriteLineAsync($"{options.NodeName}: {node.ToString()}");
        await Console.Out.WriteLineAsync("Ctrl+C to exit");
        await ThreadCore.Root.Delay(Timeout.InfiniteTimeSpan); // Wait until the server shuts down.
        await unit.Terminate(); // Perform the termination process for the unit.

        ThreadCore.Root.Terminate();
        await ThreadCore.Root.WaitForTerminationAsync(-1); // Wait for the termination infinitely.
        ThreadCore.Root.TerminationEvent.Set(); // The termination process is complete (#1).
    }
}

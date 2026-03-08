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

        // Create a NetUnit builder.
        var builder = new NetUnit.Builder()
            .Configure(context =>
            {
                // context.Services.AddTransient<TestServiceAgent>(); // Register the service implementation. If a default constructor is available, an instance will be automatically created.
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

        ThreadCore.Root.Terminate();
        await ThreadCore.Root.WaitForTerminationAsync(-1); // Wait for the termination infinitely.
        ThreadCore.Root.TerminationEvent.Set(); // The termination process is complete (#1).
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using Arc;
using Arc.Threading;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;

namespace QuickStart;

public class Program
{
    private static ExecutionRoot? root;

    public static async Task Main()
    {
        AppCloseHandler.Set(() =>
        {// Closing the console window or terminating the process.
            root?.RequestTermination(); // Send a termination signal to the root.
            root?.WaitForTermination(TimeSpan.FromSeconds(2)).Wait();
        });

        Console.CancelKeyPress += (s, e) =>
        {// Ctrl+C pressed.
            e.Cancel = true;
            root?.RequestTermination(); // Send a termination signal to the root.
        };

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
        root = unit.Context.Root;
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
        await root.Delay(Timeout.InfiniteTimeSpan); // Wait until the server shuts down.
        await unit.Terminate(); // Perform the termination process for the unit.

        root.RequestTermination();
        await root.WaitForTermination(); // Wait for the termination infinitely.
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using Arc.Unit;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;

namespace QuickStart;

public class Program
{
    public static async Task Main()
    {
        var builder = new NetControl.Builder().Configure(context =>
        {
            context.AddLoggerResolver(x =>
            {// Log source/level -> Resolver() -> Output/filter
                if (x.LogLevel == LogLevel.Debug)
                {
                    x.ClearOutput();
                    return;
                }

                x.SetOutput<ConsoleLogger>();
            });
        });

        var unit = builder.Build(); // Create a NetControl unit that implements communication functionality.
        await unit.Run(new NetOptions(), true); // Execute the created unit with default options.

        var netControl = unit.Context.ServiceProvider.GetRequiredService<NetControl>(); // Get a NetControl instance.
        // using (var connection = await netControl.NetTerminal.UnsafeConnect(new(IPAddress.Loopback, 1981)))
        NetNode.TryParse("127.0.0.1:1981(e:XWLus_KiQ3AaNVeBDBp3qaot8wQEbmzlHD3Wkg8cWmXZ5egP)", out var netNode, out _);
        using (var connection = await netControl.NetTerminal.Connect(netNode!))
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
                var r = await service.Sum(1, 2).ResponseAsync; // If the function fails, it returns the default value, so if detailed information is needed, please refer to ResponseAsync.
                await Console.Out.WriteLineAsync($"{r.ToString()}"); // NetResult.NoNetService

                var service2 = connection.GetService<ITestService2>();
                var result = await service2.Random();
                await Console.Out.WriteLineAsync($"{result}");
                result = await service2.Random();
                await Console.Out.WriteLineAsync($"{result}");
            }
        }

        await unit.Terminate(); // Perform the termination process for the unit.
    }
}

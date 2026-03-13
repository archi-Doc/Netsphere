// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Unit;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;

namespace QuickStart;

public class Program
{
    public static async Task Main()
    {
        var builder = new NetUnit.Builder().Configure(context =>
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

        var unit = builder.Build(); // Create a NetUnit unit that implements communication functionality.
        await unit.Run(new NetOptions(), true); // Execute the created unit with default options.

        var netUnit = unit.Context.ServiceProvider.GetRequiredService<NetUnit>(); // Get a NetUnit instance.
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
    }
}

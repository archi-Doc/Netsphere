// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc;
using Arc.Threading;
using Arc.Unit;
using BigMachines;
using Microsoft.Extensions.DependencyInjection;
using SimpleCommandLine;

namespace Netsphere.Runner;

[BigMachineObject(IncludeAllMachines = true)]
public partial class BigMachine;

public class Program
{
    private static ExecutionRoot? root;

    public static async Task Main()
    {
        AppCloseHandler.Register(() =>
        {// Closing the console window or terminating the process.
            root?.RequestTermination(); // Send a termination signal to the root.
            root?.WaitForTerminationAsync(TimeSpan.FromSeconds(2)).Wait();
        });

        Console.CancelKeyPress += (s, e) =>
        {// Ctrl+C pressed.
            e.Cancel = true;
            root?.RequestTermination(); // Send a termination signal to the root.
        };

        var builder = new RunnerUnit.Builder()
            .Configure(context =>
            {
                // Add Command
            });

        var unit = builder.Build();
        root = unit.Context.ExecutionRoot;
        await unit.RunAsync();

        root.RequestTermination();
        if (unit.Context.ServiceProvider.GetService<LogUnit>() is { } unitLogger)
        {
            await unitLogger.FlushAndTerminateAsync();
        }

        await root.WaitForTerminationAsync(); // Wait for the termination infinitely.
    }
}

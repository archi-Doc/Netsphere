// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1210 // Using directives should be ordered alphabetically by namespace

global using Arc.Crypto;
global using Arc.Threading;
global using Arc.Unit;
global using Netsphere;
global using SimpleCommandLine;
using Arc;
using Microsoft.Extensions.DependencyInjection;

namespace Netsphere.Version;

// [BigMachineObject(Inclusive = true)]
// public partial class BigMachine;

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

        var builder = new ProgramUnit.Builder()
            .Configure(context =>
            {
                // Add Command
            });

        var unit = builder.Build();
        root = unit.Context.Root;
        await unit.RunAsync();

        root.RequestTermination();
        if (unit.Context.ServiceProvider.GetService<LogUnit>() is { } unitLogger)
        {
            await unitLogger.FlushAndTerminate();
        }

        await root.WaitForTermination(); // Wait for the termination infinitely.
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

global using Arc.Threading;
global using Tinyhand;
using System.Runtime.Serialization;
using Arc;
using Arc.Unit;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;
using Netsphere.Crypto;
using Netsphere.Relay;
using SimpleCommandLine;
using static SimpleCommandLine.SimpleParser;

namespace Playground;

public class Program
{
    public static async Task Main()
    {
        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {// Console window closing or process terminated.
            ThreadCore.Root.Terminate(); // Send a termination signal to the root.
            ThreadCore.Root.TerminationEvent.WaitOne(2000); // Wait until the termination process is complete (#1).
        };

        Console.CancelKeyPress += (s, e) =>
        {// Ctrl+C pressed
            e.Cancel = true;
            ThreadCore.Root.Terminate(); // Send a termination signal to the root.
        };

        var builder = new NetControl.Builder()
            .Configure(context =>
            {
                context.AddSingleton<IRelayControl, CertificateRelayControl>();

                // Command
                context.AddCommand(typeof(BasicCommand));

                context.AddLoggerResolver(context =>
                {// Logger
                    if (context.LogLevel == LogLevel.Debug)
                    {
                        context.SetOutput<FileLogger<FileLoggerOptions>>();
                        return;
                    }

                    context.SetOutput<ConsoleAndFileLogger>();
                });
            })
             .ConfigureNetsphere(context =>
             {// Register the services provided by the server.
                 // context.AddNetService<ITestService, TestServiceImpl>();
             })
             .PostConfigure(context =>
             {
                 // FileLoggerOptions
                 var logfile = "Logs/Debug.txt";
                 var fileLoggerOptions = context.GetOptions<FileLoggerOptions>();
                 context.SetOptions(fileLoggerOptions with
                 {
                     Path = Path.Combine(context.DataDirectory, logfile),
                     MaxLogCapacity = 1,
                     Formatter = fileLoggerOptions.Formatter with { TimestampFormat = "yyyy-MM-dd HH:mm:ss.ffffff K", },
                     ClearLogsAtStartup = true,
                     MaxQueue = 100_000,
                 });

                 // NetsphereOptions
                 context.SetOptions(context.GetOptions<NetOptions>() with
                 {
                     // NodeName = "test",
                     // EnablePing = true,
                     EnableServer = true,
                     EnableAlternative = true,
                 });
             });

        // Netsphere
        var unit = builder.Build();
        var options = unit.Context.ServiceProvider.GetRequiredService<NetOptions>();
        await Console.Out.WriteLineAsync($"Port: {options.Port.ToString()}");

        var netBase = unit.Context.ServiceProvider.GetRequiredService<NetBase>();
        if (BaseHelper.TryParseFromEnvironmentVariable<SeedKey>("nodesecretkey", out var seedKey))
        {
            netBase.SetNodeSeedKey(seedKey);
        }

        await unit.Run(options, true);

        var parserOptions = SimpleParserOptions.Standard with
        {
            ServiceProvider = unit.Context.ServiceProvider,
            RequireStrictCommandName = false,
            RequireStrictOptionName = false,
        };

        await SimpleParser.ParseAndRunAsync(unit.Context.Commands, SimpleParserHelper.GetCommandLineArguments(), parserOptions); // Main process

        await unit.Terminate();

        ThreadCore.Root.Terminate();
        await ThreadCore.Root.WaitForTerminationAsync(-1); // Wait for the termination infinitely.
        if (unit.Context.ServiceProvider.GetService<UnitLogger>() is { } unitLogger)
        {
            await unitLogger.FlushAndTerminate();
        }

        ThreadCore.Root.TerminationEvent.Set(); // The termination process is complete (#1).
    }
}

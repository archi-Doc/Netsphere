// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1210 // Using directives should be ordered alphabetically by namespace

global using Arc.Crypto;
global using Arc.Threading;
global using Arc.Unit;
global using BigMachines;
global using Netsphere;
global using Tinyhand;
global using ValueLink;
using Arc;
using Microsoft.Extensions.DependencyInjection;
using SimpleCommandLine;

namespace RemoteDataServer;

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

        var builder = new NetUnit.Builder() // Create a NetUnit builder.
            .Configure(context =>
            {
                context.AddSingleton<RemoteDataControl>();
                context.AddTransient<RemoteDataAgent>();

                // Command
                context.AddCommand(typeof(DefaultCommand));

                // context.AddLogOutputResolver(NetUnit.LowLevelLoggerResolver<EmptyLogOutput>);
                context.AddLogOutputResolver(context =>
                {// Logger
                    if (context.LogLevel == LogLevel.Debug)
                    {
                        // if (context.LogOutputType is null)
                        {
                            context.SetOutput<FileLogOutput<FileLogOutputOptions>>(); // EmptyLogOutput
                        }

                        return;
                    }

                    context.SetOutput<ConsoleLogOutput>();
                });
            })
            .ConfigureNetsphere(context =>
            {// Register the services provided by the server.
                // context.AddNetService<Netsphere.Interfaces.IRemoteData, RemoteDataAgent>();
            })
            .PostConfigure(context =>
            {
                // FileLogOutputOptions
                var logfile = "Logs/Net.txt";
                var fileLoggerOptions = context.GetOrCreateOptions<FileLogOutputOptions>();
                context.SetOptions(fileLoggerOptions with
                {
                    FilePath = Path.Combine(context.DataDirectory, logfile),
                    MaxLogCapacityInMegabytes = 100,
                    FormatterOptions = fileLoggerOptions.FormatterOptions with { TimestampFormat = "mm:ss.ffffff K", },
                    ClearLogsAtStartup = true,
                    MaxQueueLength = 100_000,
                });

                // NetsphereOptions
                context.SetOptions(context.GetOrCreateOptions<NetOptions>() with
                {
                    NodeName = "RemoteDataServer",
                    // Port = 50000, // Specify the port number.
                    EnablePing = true,
                    EnableServer = true,
                });
            });

        var unit = builder.Build(); // Create a unit that provides network functionality.
        root = unit.Context.ExecutionRoot;

        var parserOptions = SimpleParserOptions.Standard with
        {
            ServiceProvider = unit.Context.ServiceProvider,
            RequireCommandName = false,
            RejectUnknownOptionNames = false,
        };

        await SimpleParser.ParseAndExecute(unit.Context.CommandTypes, SimpleParserHelper.GetCommandLineArguments(), parserOptions); // Main process

        await unit.Terminate(); // Perform the termination process for the unit.
        root.RequestTermination();
        await root.WaitForTerminationAsync(); // Wait for the termination infinitely.
    }
}

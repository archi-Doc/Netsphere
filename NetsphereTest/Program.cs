// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

global using System;
global using System.Threading;
global using System.Threading.Tasks;
global using Arc.Threading;
global using Netsphere;
using Arc.Unit;
using Lp.NetServices;
using Microsoft.Extensions.DependencyInjection;
using Netsphere.Misc;
using SimpleCommandLine;

namespace NetsphereTest;

public class Program
{
    // basic -node alternative -ns{-alternative true -logger true}
    // stress -ns{-alternative true -logger true}

    public static async Task Main()
    {
        // 1st: DI Container
        /*var commandTypes = new List<Type>();
        commandTypes.Add(typeof(BasicTestSubcommand));
        commandTypes.Add(typeof(NetbenchSubcommand));

        NetControl.Register(Container, commandTypes);
        foreach (var x in commandTypes)
        {
            Container.Register(x, Reuse.Singleton);
        }

        // Services
        Container.Register<ExternalServiceImpl>(Reuse.Singleton);

        Container.Register<TestFilterB>(Reuse.Singleton);

        Container.ValidateAndThrow();*/

        // 2nd: ServiceCollection
        /*var services = new ServiceCollection();
        NetControl.Register(services, commandTypes);
        foreach (var x in commandTypes)
        {
            services.AddSingleton(x);
        }

        services.AddSingleton<ExternalServiceImpl>();

        services.AddSingleton<TestFilterB>();

        var serviceProvider = services.BuildServiceProvider();
        NetControl.SetServiceProvider(serviceProvider);*/

        // NetControl.QuickStart(true, () => new TestServerContext(), () => new TestCallContext(), "test", options, true);

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

        var args = SimpleParserHelper.GetCommandLineArguments();
        SimpleParserHelper.AddEnvironmentVariable(ref args, "lpargs");
        if (args.Length == 0)
        {
            Console.Write("Arguments: ");
            var arguments = Console.ReadLine();
            if (arguments != null)
            {
                args = arguments;
            }
        }

        // Secrets
        // BaseHelper.TryParseFromEnvironmentVariable<NodeSecretKey>("k", out var privateKey);

        // 3rd: Builder pattern
        var builder = new NetControl.Builder()
            .PreConfigure(context =>
            {
                var originalOptions = context.GetOptions<NetOptions>();
                NetOptions? options = default;
                if (context.Arguments.TryGetOptionValue("ns", out var nsArg))
                {
                    SimpleParser.TryParseOptions(nsArg.UnwrapBracket(), out options, originalOptions);
                }

                options ??= originalOptions;
                context.SetOptions(options);
            })
            .Configure(context =>
            {
                // Command
                context.AddCommand(typeof(DeliveryTestSubcommand));
                context.AddCommand(typeof(BasicTestSubcommand));
                context.AddCommand(typeof(NetbenchSubcommand));
                context.AddCommand(typeof(TaskScalingSubcommand));
                context.AddCommand(typeof(StressSubcommand));
                context.AddCommand(typeof(RemoteBenchSubcommand));
                context.AddCommand(typeof(StreamTestSubcommand));

                // NetService
                context.AddSingleton<RemoteBenchHostAgent>();
                context.AddSingleton<RemoteBenchRunnerAgent>();

                // ServiceFilter

                // Other

                // Resolver
                context.ClearLoggerResolver();
                context.AddLoggerResolver(context =>
                {
                    if (context.LogLevel == LogLevel.Debug)
                    {
                        context.SetOutput<FileLogger<FileLoggerOptions>>();
                        // context.SetOutput<EmptyLogger>();
                        return;
                    }

                    /*if (context.LogSourceType == typeof(ClientTerminal))
                    {// ClientTerminal
                        context.SetOutput<StreamLogger<ClientTerminalLoggerOptions>>();
                        return;
                    }
                    else if (context.LogSourceType == typeof(ServerTerminal))
                    {// ServerTerminal
                        context.SetOutput<StreamLogger<ServerTerminalLoggerOptions>>();
                        return;
                    }
                    else if (context.LogSourceType == typeof(Terminal))
                    {// Terminal
                        context.SetOutput<StreamLogger<TerminalLoggerOptions>>();
                        return;
                    }
                    else*/
                    {
                        context.SetOutput<ConsoleLogger>();
                    }
                });
            })
            .PostConfigure(context =>
            {
                var netOptions = context.GetOptions<NetOptions>();
                if (string.IsNullOrEmpty(netOptions.NodeSecretKey) &&
                Environment.GetEnvironmentVariable("node_privatekey") is { } nodePrivateKey)
                {
                    netOptions.NodeSecretKey = nodePrivateKey;
                }

                netOptions.Port = 50000;
                context.SetOptions(netOptions);

                var logfile = "Logs/Debug.txt";
                var fileLoggerOptions = context.GetOptions<FileLoggerOptions>();
                context.SetOptions(fileLoggerOptions with
                {
                    Path = Path.Combine(context.DataDirectory, logfile),
                    MaxLogCapacity = 10,
                    Formatter = fileLoggerOptions.Formatter with { TimestampFormat = "mm:ss.ffffff K", },// "yyyy-MM-dd HH:mm:ss.ffffff K";
                    ClearLogsAtStartup = true,
                    MaxQueue = 100_000,
                });

                var consoleLoggerOptions = context.GetOptions<ConsoleLoggerOptions>();
                context.SetOptions(consoleLoggerOptions with
                {
                    EnableBuffering = true,
                    FormatterOptions = consoleLoggerOptions.FormatterOptions with
                    {
                        EnableColor = false,
                        TimestampFormat = "mm:ss.ffffff K", // "yyyy-MM-dd HH:mm:ss.ffffff K"
                    },
                });
            });

        Console.WriteLine(string.Join(' ', args));
        var unit = builder.Build(args);

        var options = unit.Context.ServiceProvider.GetRequiredService<NetOptions>();
        options.EnableServer = true;
        options.EnableAlternative = true;
        await Console.Out.WriteLineAsync(options.ToString());
        await unit.Run(options, true, x => new TestConnectionContext(x));

        var netControl = unit.Context.ServiceProvider.GetRequiredService<NetControl>();
        netControl.Services.Register<IRemoteBenchHost, RemoteBenchHostAgent>();
        netControl.Services.Register<IRemoteBenchRunner, RemoteBenchRunnerAgent>();

        var parserOptions = SimpleParserOptions.Standard with
        {
            ServiceProvider = unit.Context.ServiceProvider,
            RequireStrictCommandName = false,
            RequireStrictOptionName = false,
        };

        await SimpleParser.ParseAndRunAsync(unit.Context.Commands, args, parserOptions); // Main process

        ThreadCore.Root.Terminate();
        await ThreadCore.Root.WaitForTerminationAsync(-1); // Wait for the termination infinitely.
        unit.Context.ServiceProvider.GetService<UnitLogger>()?.FlushAndTerminate();
        ThreadCore.Root.TerminationEvent.Set(); // The termination process is complete (#1).
    }
}

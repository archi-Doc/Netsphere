// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Threading;
using Arc.Unit;
using BigMachines;
using Lp.Subcommands;
using Microsoft.Extensions.DependencyInjection;
using Netsphere.Misc;
using Netsphere.Packet;
using SimpleCommandLine;
using Tinyhand;

namespace Netsphere.Version;

internal class ProgramUnit : UnitBase, IUnitPreparable, IUnitExecutable
{
    public class Builder : UnitBuilder<Unit>
    {// Builder class for customizing dependencies.
        public Builder()
            : base()
        {
            // Configuration for Unit.
            this.Configure(context =>
            {
                context.AddSingleton<ProgramUnit>();
                context.AddSingleton<Unit>();
                context.AddSingleton<GetOptions>();
                context.CreateInstance<ProgramUnit>();
                // context.AddSingleton<BigMachine>();

                // Command
                context.AddCommand(typeof(ServerCommand));
                context.AddCommand(typeof(GetCommand));
                context.AddCommand(typeof(UpdateCommand));
                context.AddCommand(typeof(RestartCommand));

                // Machines
                // context.AddTransient<RunnerMachine>();

                // Net Services
                // context.AddSingleton<RemoteControlAgent>();

                // Logger
                context.ClearLoggerResolver();
                context.AddLoggerResolver(x =>
                {// Log source/level -> Resolver() -> Output/filter
                    if (x.LogLevel == LogLevel.Debug)
                    {
                        x.ClearOutput();
                        return;
                    }

                    x.SetOutput<ConsoleAndFileLogger>();
                });
            });

            this.SetupOptions<FileLoggerOptions>((context, options) =>
            {// FileLoggerOptions
                var logfile = "Logs/Log.txt";
                options.Path = Path.Combine(context.RootDirectory, logfile);
                options.MaxLogCapacity = 2;
            });

            this.SetupOptions<ConsoleLoggerOptions>((context, options) =>
            {// ConsoleLoggerOptions
                options.Formatter.EnableColor = true;
            });

            this.SetupOptions<NetOptions>((context, options) =>
            {// NetOptions
                var args = SimpleParserHelper.GetCommandLineArguments();
                var cmd = SimpleParserHelper.PeekCommand(args);
                if (string.IsNullOrEmpty(cmd) || cmd == "server")
                {// Server command (default)
                    options.EnableServer = true;
                    if (SimpleParser.TryParseOptions<ServerOptions>(args, out var serverOptions))
                    {
                        options.Port = serverOptions.Port;
                    }
                }
                else
                {
                    options.Port = 0;
                    options.EnableServer = false;
                }
            });

            this.AddBuilder(new NetControl.Builder());
        }
    }

    public class Unit : NetControl.Unit
    {// Unit class for customizing behaviors.
        public Unit(UnitContext context)
            : base(context)
        {
            TinyhandSerializer.ServiceProvider = context.ServiceProvider;
        }

        public async Task RunAsync()
        {
            // Create optional instances
            this.Context.CreateInstances();

            /*var args = SimpleParserHelper.GetCommandLineArguments();
            int port = 0;
            bool enableServer = false;
            var cmd = SimpleParserHelper.PeekCommand(args);
            if (string.IsNullOrEmpty(cmd) || cmd == "server")
            {// Server command (default)
                enableServer = true;
                if (SimpleParser.TryParseOptions<ServerOptions>(args, out var options))
                {
                    port = options.Port;
                }
            }

            var netOptions = new NetOptions() with
            {
                Port = port,
                EnableServer = enableServer,
            };*/
            var netOptions = this.Context.ServiceProvider.GetRequiredService<NetOptions>();
            await this.Run(netOptions, false);

            var args = SimpleParserHelper.GetCommandLineArguments();
            var parserOptions = SimpleParserOptions.Standard with
            {
                ServiceProvider = this.Context.ServiceProvider,
                RequireStrictCommandName = false,
                RequireStrictOptionName = false,
            };
            await SimpleParser.ParseAndRunAsync(this.Context.Commands, args, parserOptions);

            await this.Terminate();
        }
    }

    public ProgramUnit(UnitContext context, ILogger<ProgramUnit> logger)
        : base(context)
    {
        this.logger = logger;
    }

    void IUnitPreparable.Prepare(UnitMessage.Prepare message)
    {
    }

    async Task IUnitExecutable.StartAsync(UnitMessage.StartAsync message, CancellationToken cancellationToken)
    {
    }

    void IUnitExecutable.Stop(UnitMessage.Stop message)
    {
    }

    async Task IUnitExecutable.TerminateAsync(UnitMessage.TerminateAsync message, CancellationToken cancellationToken)
    {
    }

    private readonly ILogger logger;
}

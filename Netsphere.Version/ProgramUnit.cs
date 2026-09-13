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
    public class Builder : UnitBuilder<Product>
    {// Builder class for customizing dependencies.
        public Builder()
            : base()
        {
            // Configuration for Unit.
            this.Configure(context =>
            {
                context.AddSingletonUnit<ProgramUnit>();
                context.AddSingleton<Product>();
                context.AddSingleton<GetOptions>();
                // context.RegisterInstanceCreation<ProgramUnit>();
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
                context.ClearLogOutputResolvers();
                context.AddLogOutputResolver(x =>
                {// Log source/level -> Resolver() -> Output/filter
                    if (x.LogLevel == LogLevel.Debug)
                    {
                        x.ClearOutput();
                        return;
                    }

                    x.SetOutput<ConsoleAndFileLogOutput>();
                });
            });

            this.PostConfigure(context =>
            {
                var logfile = "Logs/Log.txt";
                context.SetOptions(context.GetOrCreateOptions<FileLogOutputOptions>() with
                {// FileLogOutputOptions
                    FilePath = Path.Combine(context.DataDirectory, logfile),
                    MaxLogCapacityInMegabytes = 2,
                });

                context.SetOptions(context.GetOrCreateOptions<ConsoleLogOutputOptions>() with
                {// ConsoleLogOutputOptions
                });

                var netOptions = context.GetOrCreateOptions<NetOptions>();
                var args = SimpleParserHelper.GetCommandLineArguments();
                var cmd = SimpleParserHelper.PeekCommandName(args);
                if (string.IsNullOrEmpty(cmd) || cmd == "server")
                {// Server command (default)
                    netOptions = netOptions with { EnableServer = true, };
                    if (SimpleParser.TryParseOptions<ServerOptions>(args, out var serverOptions))
                    {
                        netOptions = netOptions with { Port = serverOptions.Port, };
                    }
                }
                else
                {
                    netOptions = netOptions with { Port = 0, EnableServer = false, };
                }
            });

            this.AddBuilder(new NetUnit.Builder());
        }
    }

    public class Product : NetUnit.Product
    {// Unit class for customizing behaviors.
        public Product(UnitContext context)
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
            var cmd = SimpleParserHelper.PeekCommandName(args);
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
                RequireCommandName = false,
                RejectUnknownOptionNames = false,
            };
            await SimpleParser.ParseAndExecute(this.Context.CommandTypes, args, parserOptions);

            await this.Terminate();
        }
    }

    public ProgramUnit(UnitContext context, ILogger<ProgramUnit> logger)
        : base(context)
    {
        this.logger = logger;
    }

    async Task IUnitPreparable.PrepareAsync(UnitContext unitContext, CancellationToken cancellationToken)
    {
    }

    async Task IUnitExecutable.StartAsync(UnitContext unitContext, CancellationToken cancellationToken)
    {
    }

    async Task IUnitExecutable.StopAsync(UnitContext unitContext, CancellationToken cancellationToken)
    {
    }

    async Task IUnitExecutable.TerminateAsync(UnitContext unitContext, CancellationToken cancellationToken)
    {
    }

    private readonly ILogger logger;
}

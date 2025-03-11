// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Threading;
using Arc.Unit;
using BigMachines;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;
using Netsphere.Interfaces;
using SimpleCommandLine;
using Tinyhand;

namespace Netsphere.Runner;

public class RunnerUnit : UnitBase, IUnitPreparable, IUnitExecutable
{
    public class Builder : UnitBuilder<Unit>
    {// Builder class for customizing dependencies.
        public Builder()
            : base()
        {
            // Configuration for Unit.
            this.Configure(context =>
            {
                context.AddSingleton<RunnerUnit>();
                context.AddSingleton<RunOptions>();
                context.AddSingleton<RestartOptions>();
                context.CreateInstance<RunnerUnit>();
                context.AddSingleton<BigMachine>();

                // Command
                context.AddSingleton<RunCommand>();
                context.AddSingleton<RestartCommand>();

                // Machines
                context.AddTransient<RunMachine>();
                context.AddTransient<RestartMachine>();

                // Net Services
                context.AddSingleton<RemoteControlBase>();
                context.AddSingleton<RemoteControlAgent>();

                // Log filter
                // context.AddSingleton<ExampleLogFilter>();

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
                options.Path = Path.Combine(context.DataDirectory, logfile);
                options.MaxLogCapacity = 2;
            });

            this.SetupOptions<ConsoleLoggerOptions>((context, options) =>
            {// ConsoleLoggerOptions
                options.Formatter.EnableColor = true;
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
            var parserOptions = SimpleParserOptions.Standard with
            {
                ServiceProvider = this.Context.ServiceProvider,
                RequireStrictCommandName = false,
                RequireStrictOptionName = false,
            };

            // Create optional instances
            this.Context.CreateInstances();

            var args = SimpleParserHelper.GetCommandLineArguments();
            await SimpleParser.ParseAndRunAsync([typeof(RunCommand), typeof(RestartCommand),], args, parserOptions);
        }
    }

    public RunnerUnit(UnitContext context, ILogger<RunnerUnit> logger)
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

    private ILogger<RunnerUnit> logger;
}

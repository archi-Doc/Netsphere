// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Threading;
using Arc.Unit;
using BigMachines;
using Microsoft.Extensions.DependencyInjection;
using SimpleCommandLine;
using Tinyhand;

namespace Netsphere.Runner;

public class RunnerUnit : UnitBase, IUnitPreparable, IUnitExecutable
{
    public class Builder : UnitBuilder<Product>
    {// Builder class for customizing dependencies.
        public Builder()
            : base()
        {
            // Configuration for Unit.
            this.Configure(context =>
            {
                context.AddSingletonUnit<RunnerUnit>();
                context.AddSingleton<RunOptions>();
                context.AddSingleton<RestartOptions>();
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

                var consoleLoggerOptions = context.GetOrCreateOptions<ConsoleLogOutputOptions>();
                context.SetOptions(consoleLoggerOptions with
                {// ConsoleLogOutputOptions
                    FormatterOptions = consoleLoggerOptions.FormatterOptions with { EnableColor = true, },
                });
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
            var parserOptions = SimpleParserOptions.Standard with
            {
                ServiceProvider = this.Context.ServiceProvider,
                RequireCommandName = false,
                RejectUnknownOptionNames = false,
            };

            // Create optional instances
            this.Context.CreateInstances();

            var args = SimpleParserHelper.GetCommandLineArguments();
            await SimpleParser.ParseAndExecute([typeof(RunCommand), typeof(RestartCommand),], args, parserOptions);
        }
    }

    public RunnerUnit(UnitContext context, ILogger<RunnerUnit> logger)
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

    private ILogger<RunnerUnit> logger;
}

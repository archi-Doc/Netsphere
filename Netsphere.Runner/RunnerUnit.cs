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
                context.AddSingleton<RunnerOptions>();
                context.CreateInstance<RunnerUnit>();
                context.AddSingleton<BigMachine>();

                // Command

                // Machines
                context.AddTransient<RunnerMachine>();

                // Net Services
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
                options.Path = Path.Combine(context.RootDirectory, logfile);
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
            // Create optional instances
            this.Context.CreateInstances();

            var args = SimpleParserHelper.GetCommandLineArguments();
            var options = this.Context.ServiceProvider.GetRequiredService<RunnerOptions>();
            SimpleParser.TryParseOptions<RunnerOptions>(args, out _, options);
            options.Prepare();

            var netOptions = new NetOptions()
            {
                NodeName = "Netsphere.Runner",
                Port = options.Port,
                NodeSecretKey = options.NodeSecretKeyString,
                EnablePing = false,
                EnableServer = true,
                EnableAlternative = false,
            };

            options.NodeSecretKeyString = string.Empty;

            var netControl = this.Context.ServiceProvider.GetRequiredService<NetControl>();
            netControl.Services.Register<IRemoteControl, RemoteControlAgent>();

            await this.Run(netOptions, true);

            var bigMachine = this.Context.ServiceProvider.GetRequiredService<BigMachine>();
            var runner = bigMachine.RunnerMachine.GetOrCreate(options);
            bigMachine.Start(ThreadCore.Root);

            _ = Task.Run(async () =>
            {
                while (!ThreadCore.Root.IsTerminated)
                {
                    var keyInfo = Console.ReadKey(true);
                    if (keyInfo.Key == ConsoleKey.R && keyInfo.Modifiers == ConsoleModifiers.Control)
                    {// Restart
                        await runner.Command.Restart();
                    }
                    else if (keyInfo.Key == ConsoleKey.Q && keyInfo.Modifiers == ConsoleModifiers.Control)
                    {// Stop and quit
                        await runner.Command.StopAll();
                        runner.TerminateMachine();
                    }
                }
            });

            while (!((IBigMachine)bigMachine).Core.IsTerminated)
            {
                if (!((IBigMachine)bigMachine).CheckActiveMachine())
                {
                    break;
                }
                else
                {
                    // await runner.Command.Restart();
                    await ((IBigMachine)bigMachine).Core.WaitForTerminationAsync(1000);
                }
            }

            await this.Context.SendTerminateAsync(new());
        }

        /*private async Task<RunnerInformation?> LoadInformation(ILogger logger, string path)
        {
            try
            {
                var utf8 = await File.ReadAllBytesAsync(path);
                var information = TinyhandSerializer.DeserializeFromUtf8<RunnerInformation>(utf8);
                if (information != null)
                {// Success
                 // Update RunnerInformation
                    information.SetDefault();
                    var update = TinyhandSerializer.SerializeToUtf8(information);
                    if (!update.SequenceEqual(utf8))
                    {
                        await File.WriteAllBytesAsync(path, update);
                    }

                    return information;
                }
            }
            catch
            {
            }

            var newInformation = new RunnerInformation().SetDefault();
            await File.WriteAllBytesAsync(path, TinyhandSerializer.SerializeToUtf8(newInformation));

            logger.TryGet(LogLevel.Error)?.Log($"'{path}' could not be found and was created.");
            logger.TryGet(LogLevel.Error)?.Log($"Modify '{RunnerInformation.Path}', and restart LpRunner.");

            return null;
        }*/
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
        throw new NotImplementedException();
    }

    async Task IUnitExecutable.TerminateAsync(UnitMessage.TerminateAsync message, CancellationToken cancellationToken)
    {
    }

    private ILogger<RunnerUnit> logger;
}

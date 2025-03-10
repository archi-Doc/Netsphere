// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Threading;
using Arc.Unit;
using BigMachines;
using Microsoft.Extensions.DependencyInjection;
using Netsphere.Interfaces;
using SimpleCommandLine;

namespace Netsphere.Runner;

public abstract class RunnerCommand
{
    private readonly UnitContext unitContext;
    private readonly RunnerUnit.Unit unit;

    protected IServiceProvider serviceProvider { get; }

    protected BigMachine bigMachine { get; }

    public RunnerCommand(IServiceProvider serviceProvider, UnitContext unitContext, RunnerUnit.Unit unit, BigMachine bigMachine)
    {
        this.serviceProvider = serviceProvider;
        this.unitContext = unitContext;
        this.unit = unit;
        this.bigMachine = bigMachine;
    }

    public async Task Run(RunnerOptions options)
    {
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

        var netControl = this.serviceProvider.GetRequiredService<NetControl>();
        netControl.Services.Register<IRemoteControl, RemoteControlAgent>();

        await this.unit.Run(netOptions, true);

        var runner = this.bigMachine.RunnerMachine.GetOrCreate(options);
        this.bigMachine.Start(ThreadCore.Root);

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
    }

    public async Task Loop()
    {
        while (!((IBigMachine)this.bigMachine).Core.IsTerminated)
        {
            if (!((IBigMachine)this.bigMachine).CheckActiveMachine())
            {
                break;
            }
            else
            {
                // await runner.Command.Restart();
                await ((IBigMachine)this.bigMachine).Core.WaitForTerminationAsync(1000);
            }
        }

        await this.unitContext.SendTerminateAsync(new());
    }
}

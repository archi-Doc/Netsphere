// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Threading;
using Arc.Unit;
using BigMachines;
using Microsoft.Extensions.DependencyInjection;
using Netsphere.Interfaces;
using SimpleCommandLine;

namespace Netsphere.Runner;

[SimpleCommand("run")]
public class RunCommand : ISimpleCommandAsync<RunOptions>
{
    private readonly IServiceProvider serviceProvider;
    private readonly UnitContext unitContext;
    private readonly RunnerUnit.Unit unit;

    public RunCommand(IServiceProvider serviceProvider, UnitContext unitContext, RunnerUnit.Unit unit)
    {
        this.serviceProvider = serviceProvider;
        this.unitContext = unitContext;
        this.unit = unit;
    }

    public async Task RunAsync(RunOptions options, string[] args)
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

        var bigMachine = this.serviceProvider.GetRequiredService<BigMachine>();
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
                await((IBigMachine)bigMachine).Core.WaitForTerminationAsync(1000);
            }
        }

        await this.unitContext.SendTerminateAsync(new());
    }
}

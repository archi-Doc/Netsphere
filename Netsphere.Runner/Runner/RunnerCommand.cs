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
    private readonly RunnerUnit.Product unit;

    protected IServiceProvider serviceProvider { get; }

    protected BigMachine bigMachine { get; }

    public RunnerCommand(IServiceProvider serviceProvider, UnitContext unitContext, RunnerUnit.Product unit, BigMachine bigMachine)
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

        var netUnit = this.serviceProvider.GetRequiredService<NetUnit>();
        netUnit.Services.Register<IRemoteControl, RemoteControlAgent>();

        var remoteControlBase = this.serviceProvider.GetRequiredService<RemoteControlBase>();
        remoteControlBase.RemotePublicKey = options.RemotePublicKey;

        await this.unit.Run(netOptions, true);
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

        await this.unitContext.SendTerminate();
    }
}

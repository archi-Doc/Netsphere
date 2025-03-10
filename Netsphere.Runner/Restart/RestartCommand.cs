// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Threading;
using Arc.Unit;
using BigMachines;
using Microsoft.Extensions.DependencyInjection;
using Netsphere.Interfaces;
using SimpleCommandLine;
using Tinyhand;

namespace Netsphere.Runner;

[SimpleCommand("restart")]
public class RestartCommand : RunnerCommand, ISimpleCommandAsync<RunOptions>
{
    public RestartCommand(IServiceProvider serviceProvider, UnitContext unitContext, RunnerUnit.Unit unit, BigMachine bigMachine)
        : base(serviceProvider, unitContext, unit, bigMachine)
    {
    }

    public async Task RunAsync(RunOptions options, string[] args)
    {
        await this.Run(options);

        var runner = this.bigMachine.RunMachine.GetOrCreate(options);
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

        await this.Loop();
    }
}

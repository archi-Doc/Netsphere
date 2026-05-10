// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Threading;
using Arc.Unit;
using SimpleCommandLine;

namespace Netsphere.Runner;

[SimpleCommand("restart")]
public class RestartCommand : RunnerCommand, ISimpleCommand<RestartOptions>
{
    public RestartCommand(IServiceProvider serviceProvider, UnitContext unitContext, RunnerUnit.Product unit, BigMachine bigMachine)
        : base(serviceProvider, unitContext, unit, bigMachine)
    {
    }

    public async Task Execute(RestartOptions options, string[] args, CancellationToken cancellationToken)
    {
        await this.Run(options);

        var machine = this.bigMachine.RestartMachine.GetOrCreate(options);
        this.bigMachine.Start();

        _ = Task.Run(async () =>
        {
            while (!this.root.IsTerminated)
            {
                var keyInfo = Console.ReadKey(true);
                if (keyInfo.Key == ConsoleKey.R && keyInfo.Modifiers == ConsoleModifiers.Control)
                {// Restart
                    await machine.Command.Restart();
                }
                else if (keyInfo.Key == ConsoleKey.Q && keyInfo.Modifiers == ConsoleModifiers.Control)
                {// Stop and quit
                    machine.TerminateMachine();
                }
            }
        });

        await this.Loop();
    }
}

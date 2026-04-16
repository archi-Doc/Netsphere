// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Unit;
using CrossChannel;
using Netsphere;
using SimpleCommandLine;

namespace Playground;

[SimpleCommand("ntp")]
public class NtpCommand : ISimpleCommand<NtpCommandOptions>
{
    public NtpCommand(ILogger<NtpCommand> logger, NetUnit netUnit, BigMachine bigMachine)
    {
        this.logger = logger;
        this.netUnit = netUnit;
        this.bigMachine = bigMachine;

        _ = this.bigMachine.NtpMachine.GetOrCreate().RunAsync();
    }

    public async Task Execute(NtpCommandOptions options, string[] args, CancellationToken cancellationToken)
    {
        Console.WriteLine("Ntp test");

        await Task.Delay(3_000);
    }

    private readonly NetUnit netUnit;
    private readonly ILogger logger;
    private readonly BigMachine bigMachine;
}

public record NtpCommandOptions
{
}

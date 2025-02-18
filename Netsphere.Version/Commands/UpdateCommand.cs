// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;
using Netsphere.Misc;
using Netsphere.Relay;

namespace Netsphere.Version;

[SimpleCommand("update")]
internal class UpdateCommand : ISimpleCommandAsync<UpdateOptions>
{
    public UpdateCommand(ILogger<UpdateCommand> logger, NetTerminal netTerminal, NtpCorrection ntpCorrection)
    {
        this.logger = logger;
        this.netTerminal = netTerminal;
        this.ntpCorrection = ntpCorrection;
    }

    public async Task RunAsync(UpdateOptions options, string[] args)
    {
        options.Prepare();
        this.logger.TryGet()?.Log($"{options.ToString()}");

        if (options.remoteSecretKey is not { } seedKey)
        {
            this.logger.TryGet(LogLevel.Fatal)?.Log($"Could not parse remote secret key");
            return;
        }

        if (!NetAddress.TryParse(options.Address, out var address, out _))
        {
            this.logger.TryGet(LogLevel.Fatal)?.Log($"Could not parse address: {options.Address}");
            return;
        }

        await this.ntpCorrection.CorrectMicsAndUnitLogger(this.logger);

        var versionInfo = new VersionInfo(options.VersionIdentifier, options.VersionKind, Mics.GetCorrected(), 0);
        var token = new CertificateToken<VersionInfo>(versionInfo);
        seedKey.Sign(token);
        this.logger.TryGet()?.Log($"{versionInfo.ToString()}");

        var p = new UpdateVersionPacket(token);
        var result = await this.netTerminal.PacketTerminal.SendAndReceive<UpdateVersionPacket, UpdateVersionResponse>(address, p);
        if (result.Value is { } value)
        {
            this.logger.TryGet()?.Log($"{value.Result.ToString()}");
        }
        else
        {
            this.logger.TryGet()?.Log($"{result.ToString()}");
        }
    }

    /*public async Task RunAsync(GetOptions options, string[] args)
    {
        this.logger.TryGet()?.Log($"{options.ToString()}");

        if (!NetNode.TryParseNetNode(this.logger, options.Node, out var node))
        {
            this.logger.TryGet(LogLevel.Fatal)?.Log($"Cannot parse node: {options.Node}");
            return;
        }

        var netOptions = new NetOptions() with
        {
        };

        await this.unit.Run(netOptions, false);

        var p = new PingPacket("test56789");
        var netTerminal = this.unit.Context.ServiceProvider.GetRequiredService<NetTerminal>();
        var result = await netTerminal.PacketTerminal.SendAndReceive<PingPacket, PingPacketResponse>(node.Address, p);

        await this.unit.Terminate();
    }*/

    private readonly ILogger logger;
    // private readonly ProgramUnit.Unit unit;
    private readonly NetTerminal netTerminal;
    private readonly NtpCorrection ntpCorrection;
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Netsphere.Crypto;
using Netsphere.Misc;
using Netsphere.Packet;
using Netsphere.Relay;
using Netsphere.Stats;
using Tinyhand;

namespace Netsphere.Version;

[SimpleCommand("server", Default = true)]
internal class ServerCommand : ISimpleCommandAsync<ServerOptions>
{
    private const int DelayMilliseconds = 1_000; // 1 second
    private const int NtpCorrectionCount = 3600; // 3600 x 1000ms = 1 hour

    public ServerCommand(ILogger<ServerCommand> logger, NetControl netControl, IRelayControl relayControl, NtpCorrection ntpCorrection)
    {
        staticInstance = this;
        this.logger = logger;
        this.netControl = netControl;
        this.relayControl = relayControl;
        this.ntpCorrection = ntpCorrection;

        this.versionData = VersionData.Load();
    }

    public async Task RunAsync(ServerOptions options, string[] args)
    {
        this.logger.TryGet()?.Log($"{options.ToString()}");

        if (!options.Check(this.logger))
        {
            return;
        }

        this.versionIdentifier = options.VersionIdentifier;
        this.publicKey = options.remotePublicKey;

        await this.ntpCorrection.CorrectMicsAndUnitLogger(this.logger);
        // Console.WriteLine($"{Mics.ToDateTime(Mics.GetCorrected())}");

        this.netControl.NetBase.SetRespondPacketFunc(RespondPacketFunc);
        var address = await NetStatsHelper.GetOwnAddress((ushort)options.Port);

        this.logger.TryGet()?.Log($"{address.ToString()}");
        this.versionData.Log(this.logger);
        this.logger.TryGet()?.Log("Press Ctrl+C to exit");

        var ntpCorrectionCount = 0;
        while (await ThreadCore.Root.Delay(1_000))
        {
            /*var keyInfo = Console.ReadKey(true);
            if (keyInfo.Key == ConsoleKey.R && keyInfo.Modifiers == ConsoleModifiers.Control)
            {// Restart
                await runner.Command.Restart();
            }
            else if (keyInfo.Key == ConsoleKey.Q && keyInfo.Modifiers == ConsoleModifiers.Control)
            {// Stop and quit
                await runner.Command.StopAll();
                runner.TerminateMachine();
            }*/

            if (ntpCorrectionCount++ >= NtpCorrectionCount)
            {
                ntpCorrectionCount = 0;
                await this.ntpCorrection.CorrectMicsAndUnitLogger(this.logger);
            }
        }
    }

    private static BytePool.RentMemory? RespondPacketFunc(ulong packetId, PacketType packetType, ReadOnlyMemory<byte> packet)
    {
        UpdateVersionResponse? updateResponse = default;

        if (packetType == PacketType.GetVersion)
        {
            var versionKind = VersionInfo.Kind.Development;
            if (packet.Length >= 2)
            {
                versionKind = (VersionInfo.Kind)packet.Span[1];
            }

            if (staticInstance?.versionData.GetVersionResponse(versionKind) is { } response)
            {
                PacketTerminal.CreatePacket(packetId, response, out var rentMemory);
                return rentMemory;
            }
        }
        else if (packetType == PacketType.UpdateVersion)
        {
            if (staticInstance is not { } instance)
            {
                return default;
            }

            if (!TinyhandSerializer.TryDeserialize<UpdateVersionPacket>(packet.Span, out var updateVersionPacket) ||
                updateVersionPacket.Token is not { } token)
            {
                updateResponse = new(UpdateVersionResult.DeserializationFailed);
            }
            else
            {
                updateResponse = instance.CreateResponse(token);
            }

            if (updateResponse is not null)
            {
                PacketTerminal.CreatePacket(packetId, updateResponse, out var rentMemory);
                return rentMemory;
            }
        }

        return default;
    }

    private UpdateVersionResponse CreateResponse(CertificateToken<VersionInfo> token)
    {
        var versionInfo = token.Target;
        if (versionInfo.VersionIdentifier != this.versionIdentifier)
        {// Wrong version identifier
            return new(UpdateVersionResult.WrongVersionIdentifier);
        }

        if (!token.PublicKey.Equals(this.publicKey))
        {// Wrong public key
            return new(UpdateVersionResult.WrongPublicKey);
        }

        if (!token.ValidateAndVerify(0))
        {// Wrong signature
            return new(UpdateVersionResult.WrongSignature);
        }

        // Check mics
        var currentMics = this.versionData.GetCurrentMics(versionInfo.VersionKind);
        if (currentMics >= versionInfo.VersionMics)
        {
            return new(UpdateVersionResult.OldMics);
        }

        if (versionInfo.VersionMics > Mics.GetCorrected() + Mics.FromSeconds(5))
        {
            return new(UpdateVersionResult.FutureMics);
        }

        this.versionData.Update(token);
        this.logger.TryGet()?.Log($"Updated: {token.Target.ToString()}");
        return new(UpdateVersionResult.Success);
    }

    private static ServerCommand? staticInstance;

    private readonly ILogger logger;
    private readonly NetControl netControl;
    private readonly IRelayControl relayControl;
    private readonly VersionData versionData;
    private readonly NtpCorrection ntpCorrection;
    private int versionIdentifier;
    private SignaturePublicKey publicKey;
}

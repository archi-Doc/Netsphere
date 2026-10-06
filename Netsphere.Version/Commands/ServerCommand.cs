// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Netsphere.Crypto;
using Netsphere.Misc;
using Netsphere.Packet;
using Netsphere.Relay;
using Netsphere.Stats;
using Tinyhand;

namespace Netsphere.Version;

[SimpleCommand("server", IsDefault = true)]
internal class ServerCommand : ISimpleCommand<ServerOptions>
{
    private const int DelayMilliseconds = 1_000; // 1 second
    private const int NtpCorrectionCount = 3600; // 3600 x 1000ms = 1 hour

    public ServerCommand(UnitContext unitContext, ILogger<ServerCommand> logger, NetUnit netUnit, IRelayControl relayControl, NtpCorrection ntpCorrection)
    {
        staticInstance = this;
        this.unitContext = unitContext;
        this.logger = logger;
        this.netUnit = netUnit;
        this.relayControl = relayControl;
        this.ntpCorrection = ntpCorrection;

        this.versionData = VersionData.Load();
    }

    public async Task Execute(ServerOptions options, string[] args, CancellationToken cancellationToken)
    {
        this.logger.GetWriter()?.Write($"{options.ToString()}");

        if (!options.Check(this.logger))
        {
            return;
        }

        this.versionIdentifier = options.VersionIdentifier;
        this.publicKey = options.remotePublicKey;

        await this.ntpCorrection.CorrectMicsAndUnitLogger(this.logger);
        // Console.WriteLine($"{Mics.ToDateTime(Mics.GetCorrected())}");

        this.netUnit.NetBase.SetRespondPacketFunc(RespondPacketFunc);
        var address = await NetStatsHelper.GetOwnAddress((ushort)options.Port, cancellationToken);

        this.logger.GetWriter()?.Write($"{address.ToString()}");
        this.versionData.Log(this.logger);
        this.logger.GetWriter()?.Write("Press Ctrl+C to exit");

        var ntpCorrectionCount = 0;
        while (true)
        {
            try
            {// The execution root is terminated by Ctrl+C; the command token is not linked to it.
                if (!await this.unitContext.ExecutionRoot.TryDelay(DelayMilliseconds, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch
            {
                return;
            }

            /*var keyInfo = Console.ReadKey(true);
            if (keyInfo.Key == ConsoleKey.R && keyInfo.Modifiers == ConsoleModifiers.Control)
            {// Restart
                await runner.Command.Restart();
            }
            else if (keyInfo.Key == ConsoleKey.Q && keyInfo.Modifiers == ConsoleModifiers.Control)
            {// Stop and quit
                await runner.Command.StopAll();
                runner.Terminate();
            }*/

            if (ntpCorrectionCount++ >= NtpCorrectionCount)
            {
                ntpCorrectionCount = 0;
                await this.ntpCorrection.CorrectMicsAndUnitLogger(this.logger);
            }
        }
    }

    private static BytePool.RentedMemory? RespondPacketFunc(ulong packetId, PacketType packetType, ReadOnlyMemory<byte> packet)
    {
        UpdateVersionResponse? updateResponse = default;

        if (packetType == PacketType.GetVersion)
        {
            var versionKind = VersionInfo.Kind.Development;
            if (TinyhandSerializer.TryDeserialize<GetVersionPacket>(packet.Span, out var getVersionPacket))
            {// Deserialize instead of reading the wire layout, which would break silently if the packet gained a field.
                versionKind = getVersionPacket.VersionKind;
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

        if (versionInfo.VersionMics > Mics.GetCorrected() + Mics.FromSeconds(5))
        {
            return new(UpdateVersionResult.FutureMics);
        }

        // Check mics and update atomically.
        if (!this.versionData.TryUpdate(token))
        {
            return new(UpdateVersionResult.OldMics);
        }

        this.logger.GetWriter()?.Write($"Updated: {token.Target.ToString()}");
        return new(UpdateVersionResult.Success);
    }

    private static ServerCommand? staticInstance;

    private readonly UnitContext unitContext;
    private readonly ILogger logger;
    private readonly NetUnit netUnit;
    private readonly IRelayControl relayControl;
    private readonly VersionData versionData;
    private readonly NtpCorrection ntpCorrection;
    private int versionIdentifier;
    private SignaturePublicKey publicKey;
}

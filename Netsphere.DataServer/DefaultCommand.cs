// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Arc;
using Microsoft.Extensions.DependencyInjection;
using Netsphere.Crypto;
using Netsphere.Packet;
using Netsphere.Stats;
using SimpleCommandLine;

namespace RemoteDataServer;

[SimpleCommand("default", Default = true)]
public class DefaultCommand : ISimpleCommandAsync<DefaultCommandOptions>
{
    public DefaultCommand(NetUnit.Product unit, ILogger<DefaultCommandOptions> logger, NetUnit netUnit, RemoteDataControl remoteDataBroker)
    {
        this.unit = unit;
        this.logger = logger;
        this.netUnit = netUnit;
        this.remoteData = remoteDataBroker;
    }

    public async Task RunAsync(DefaultCommandOptions options, string[] args)
    {
        var netOptions = this.unit.Context.ServiceProvider.GetRequiredService<NetOptions>();
        netOptions.Port = options.Port;
        await this.unit.Run(netOptions, false); // Execute the created unit with the specified options.

        var address = await NetStatsHelper.GetOwnAddress((ushort)options.Port);

        // NtpCorrection
        var ntpCorrection = this.unit.Context.ServiceProvider.GetRequiredService<Netsphere.Misc.NtpCorrection>();
        await ntpCorrection.CorrectMicsAndUnitLogger();

        // await Console.Out.WriteLineAsync(netOptions.ToString());
        await Console.Out.WriteLineAsync(options.ToString());
        await Console.Out.WriteLineAsync();

        this.PrepareKey(options);
        var netNode = new NetNode(address, this.remoteSeedKey.GetEncryptionPublicKey());

        await this.PunchNode(options.PunchNode);
        this.remoteData.Initialize(options.DataDirectory);

        await Console.Out.WriteLineAsync($"{this.netUnit.NetBase.NetOptions.NodeName}");
        await Console.Out.WriteLineAsync($"Node: {netNode.ToString()}");
        await Console.Out.WriteLineAsync($"Remote public key: {this.remoteData.RemotePublicKey.ToString()}");
        await Console.Out.WriteLineAsync($"Data directory: {this.remoteData.DataDirectory}");
        await Console.Out.WriteLineAsync("Ctrl+C to exit");
        await Console.Out.WriteLineAsync();

        await ThreadCore.Root.Delay(Timeout.InfiniteTimeSpan); // Wait until the server shuts down.
    }

    [MemberNotNull(nameof(remoteSeedKey))]
    private void PrepareKey(DefaultCommandOptions options)
    {
        if (SeedKey.TryParse(options.NodeSecretKey, out var seedKey))
        {
            this.netUnit.NetBase.SetNodeSeedKey(seedKey);
            this.netUnit.NetTerminal.SetNodeSeedKey(seedKey);
        }
        else if (BaseHelper.TryParseFromEnvironmentVariable<SeedKey>(NetConstants.NodeSecretKeyName, out seedKey))
        {
            this.netUnit.NetBase.SetNodeSeedKey(seedKey);
            this.netUnit.NetTerminal.SetNodeSeedKey(seedKey);
        }

        this.remoteSeedKey = seedKey ?? SeedKey.NewEncryption();

        if (SignaturePublicKey.TryParse(options.RemotePublicKey, out var publicKey, out _))
        {
            this.remoteData.RemotePublicKey = publicKey;
        }
        else if (BaseHelper.TryParseFromEnvironmentVariable<SignaturePublicKey>(NetConstants.RemotePublicKeyName, out publicKey))
        {
            this.remoteData.RemotePublicKey = publicKey;
        }
    }

    private async Task PunchNode(string punchNode)
    {
        if (!NetAddress.TryParse(punchNode, out var node, out _))
        {
            if (!BaseHelper.TryParseFromEnvironmentVariable<NetAddress>("punchnode", out node))
            {
                return;
            }
        }

        var sw = Stopwatch.StartNew();

        var p = new PingPacket("PunchMe");
        var result = await this.netUnit.NetTerminal.PacketTerminal.SendAndReceive<PingPacket, PingPacketResponse>(node, p);

        sw.Stop();
        this.logger.TryGet()?.Log($"Punch: {result.ToString()} {sw.ElapsedMilliseconds} ms");
    }

    private readonly NetUnit.Product unit;
    private readonly NetUnit netUnit;
    private readonly ILogger logger;
    private readonly RemoteDataControl remoteData;
    private SeedKey? remoteSeedKey;
}

public record DefaultCommandOptions
{
    [SimpleOption("Port", Description = "Port number", ReadFromEnvironment = true)]
    public int Port { get; set; }

    [SimpleOption("DataDirectory", Description = "Data directory", ReadFromEnvironment = true)]
    public string DataDirectory { get; init; } = "Data";

    [SimpleOption("PunchNode", Description = "Punch node")]
    public string PunchNode { get; init; } = string.Empty;

    [SimpleOption("NodeSecretKey", Description = "Node secret key", ReadFromEnvironment = true)]
    public string NodeSecretKey { get; init; } = string.Empty;

    [SimpleOption("RemotePublickey", Description = "Remote public key", ReadFromEnvironment = true)]
    public string RemotePublicKey { get; set; } = string.Empty;
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc;
using Netsphere.Crypto;
using Netsphere.Packet;
using Netsphere.Stats;
using SimpleCommandLine;

namespace RemoteDataServer;

[SimpleCommand("default", Default = true)]
public class DefaultCommand : ISimpleCommandAsync<DefaultCommandOptions>
{
    public DefaultCommand(ILogger<DefaultCommandOptions> logger, NetControl netControl, RemoteDataControl remoteDataBroker)
    {
        this.logger = logger;
        this.netControl = netControl;
        this.remoteData = remoteDataBroker;
    }

    public async Task RunAsync(DefaultCommandOptions options, string[] args)
    {
        this.PrepareKey(options);
        // await this.PrepareNodeAddress();
        await this.PunchNode(options.PunchNode);
        this.remoteData.Initialize(options.Directory);

        await Console.Out.WriteLineAsync($"{this.netControl.NetBase.NetOptions.NodeName}");
        await Console.Out.WriteLineAsync($"Node: {this.netControl.NetStats.GetOwnNetNode().ToString()}");
        await Console.Out.WriteLineAsync($"Remote key: {this.remoteData.RemotePublicKey.ToString()}");
        await Console.Out.WriteLineAsync($"Directory: {this.remoteData.DataDirectory}");
        await Console.Out.WriteLineAsync("Ctrl+C to exit");
        await Console.Out.WriteLineAsync();

        await ThreadCore.Root.Delay(Timeout.InfiniteTimeSpan); // Wait until the server shuts down.
    }

    private void PrepareKey(DefaultCommandOptions options)
    {
        if (SeedKey.TryParse(options.NodeSecretKey, out var seedKey))
        {
            this.netControl.NetBase.SetNodeSeedKey(seedKey);
            this.netControl.NetTerminal.SetNodeSeedKey(seedKey);
        }
        else if (BaseHelper.TryParseFromEnvironmentVariable<SeedKey>(NetConstants.NodeSecretKeyName, out seedKey))
        {
            this.netControl.NetBase.SetNodeSeedKey(seedKey);
            this.netControl.NetTerminal.SetNodeSeedKey(seedKey);
        }

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
        var result = await this.netControl.NetTerminal.PacketTerminal.SendAndReceive<PingPacket, PingPacketResponse>(node, p);

        sw.Stop();
        this.logger.TryGet()?.Log($"Punch: {result.ToString()} {sw.ElapsedMilliseconds} ms");
    }

    private readonly NetControl netControl;
    private readonly ILogger logger;
    private readonly RemoteDataControl remoteData;
}

public record DefaultCommandOptions
{
    [SimpleOption("Directory", Description = "Directory")]
    public string Directory { get; init; } = "Data";

    [SimpleOption("PunchNode", Description = "Punch node")]
    public string PunchNode { get; init; } = string.Empty;

    [SimpleOption("NodeSecretKey", Description = "Node secret key")]
    public string NodeSecretKey { get; init; } = string.Empty;

    [SimpleOption("RemotePublickey", Description = "Remote public key")]
    public string RemotePublicKey { get; set; } = string.Empty;
}

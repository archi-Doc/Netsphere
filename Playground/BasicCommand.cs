// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Unit;
using Netsphere;
using Netsphere.Crypto;
using Netsphere.Packet;
using Netsphere.Relay;
using Netsphere.Stats;
using SimpleCommandLine;

namespace Playground;

[SimpleCommand("basic")]
public class BasicCommand : ISimpleCommandAsync<BasicCommandOptions>
{
    public BasicCommand(ILogger<BasicCommand> logger, NetUnit netUnit, IRelayControl relayControl)
    {
        this.logger = logger;
        this.netUnit = netUnit;
        this.relayControl = relayControl;
    }

    public async Task RunAsync(BasicCommandOptions options, string[] args)
    {
        /*var r = await NetStatsHelper.GetIcanhazipIPv4();
        var netAddress = new NetAddress(r.Address!, (ushort)this.netUnit.NetBase.NetOptions.Port);
        var netNode = new NetNode(netAddress, this.netUnit.NetBase.NodePublicKey);
        var st = netNode.ToString();
        options.Node = st;
        this.netUnit.NetStats.SetOwnNetNodeForTest(netAddress, this.netUnit.NetBase.NodePublicKey);*/

        if (!NetAddress.TryParse(this.logger, options.Node, out var address))
        {
            return;
        }

        var sw = Stopwatch.StartNew();
        var netTerminal = this.netUnit.NetTerminal;
        var packetTerminal = netTerminal.PacketTerminal;

        var length = AuthenticationToken.MaxStringLength;
        var p = new PingPacket("test56789");
        var result = await packetTerminal.SendAndReceive<PingPacket, PingPacketResponse>(address, p, 0, default, EndpointResolution.NetAddress);
        Console.WriteLine(result);

        Mics.UpdateFastCorrected();
        var micsId = Mics.GetMicsId();
        Console.WriteLine(micsId);
        micsId = Mics.GetMicsId();
        Console.WriteLine(micsId);

        /*var netNode = await netTerminal.UnsafeGetNetNode(Alternative.NetAddress);
        if (netNode is null)
        {
            return;
        }

        using (var clientConnection = await netTerminal.Connect(netNode, Connection.ConnectMode.ReuseIfAvailable, 0))
        {
            if (clientConnection is null)
            {
                return;
            }
        }*/
    }

    private readonly NetUnit netUnit;
    private readonly ILogger logger;
    private readonly IRelayControl relayControl;
}

public record BasicCommandOptions
{
    [SimpleOption("Node", Description = "Node address", Required = true)]
    public string Node { get; set; } = string.Empty;
}

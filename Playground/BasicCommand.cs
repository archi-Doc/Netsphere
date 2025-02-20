﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Unit;
using Netsphere;
using Netsphere.Packet;
using Netsphere.Relay;
using SimpleCommandLine;

namespace Playground;

[SimpleCommand("basic")]
public class BasicCommand : ISimpleCommandAsync<BasicCommandOptions>
{
    public BasicCommand(ILogger<BasicCommand> logger, NetControl netControl, IRelayControl relayControl)
    {
        this.logger = logger;
        this.netControl = netControl;
        this.relayControl = relayControl;
    }

    public async Task RunAsync(BasicCommandOptions options, string[] args)
    {
        if (!NetAddress.TryParse(this.logger, options.Node, out var address))
        {
            return;
        }

        var sw = Stopwatch.StartNew();
        var netTerminal = this.netControl.NetTerminal;
        var packetTerminal = netTerminal.PacketTerminal;

        var p = new PingPacket("test56789");
        var result = await packetTerminal.SendAndReceive<PingPacket, PingPacketResponse>(address, p, 0, default, EndpointResolution.NetAddress);
        Console.WriteLine(result);

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

    private readonly NetControl netControl;
    private readonly ILogger logger;
    private readonly IRelayControl relayControl;
}

public record BasicCommandOptions
{
    [SimpleOption("Node", Description = "Node address", Required = true)]
    public string Node { get; init; } = string.Empty;
}

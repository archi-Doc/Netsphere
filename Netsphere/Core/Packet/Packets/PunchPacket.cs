// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Packet;

[TinyhandObject]
public sealed partial class PunchPacket : IPacket
{
    public static PacketType PacketType => PacketType.Punch;

    public PunchPacket()
    {
    }

    public PunchPacket(NetEndpoint relayEndpoint, NetEndpoint destinationEndpoint)
    {
        this.RelayEndpoint = relayEndpoint;
        this.DestinationEndpoint = destinationEndpoint;
    }

    [Key(0)]
    public NetEndpoint RelayEndpoint { get; set; }

    [Key(1)]
    public NetEndpoint DestinationEndpoint { get; set; }

    public override string ToString()
    {
        if (this.RelayEndpoint.IsValid)
        {
            return $"{this.RelayEndpoint}->{this.DestinationEndpoint}";
        }
        else
        {
            return $"->{this.DestinationEndpoint}";
        }
    }
}

[TinyhandObject]
public sealed partial class PunchPacketResponse : IPacket
{
    public static PacketType PacketType => PacketType.PunchResponse;

    public PunchPacketResponse()
    {
    }
}

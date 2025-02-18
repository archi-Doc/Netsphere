// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Packet;

namespace Netsphere.Relay;

[TinyhandObject]
public sealed partial class PingRelayPacket : IPacket
{
    public static PacketType PacketType => PacketType.PingRelay;

    public PingRelayPacket()
    {
    }
}

[TinyhandObject]
public sealed partial class PingRelayResponse : IPacket
{
    public static PacketType PacketType => PacketType.PingRelayResponse;

    public PingRelayResponse()
    {
    }

    internal PingRelayResponse(RelayExchange exchange)
    {
        this.RelayPoint = exchange.RelayPoint;
        this.OuterEndPoint = exchange.OuterEndpoint;
        this.RelayRetensionMics = exchange.RelayRetensionMics;
    }

    [Key(0)]
    public long RelayPoint { get; private set; }

    [Key(1)]
    public NetEndpoint? OuterEndPoint { get; private set; }

    [Key(2)]
    public long RelayRetensionMics { get; private set; }

    public bool IsOutermost
        => this.OuterEndPoint is null;

    public override string ToString()
    {
        var outerRelay = this.OuterEndPoint is null ? string.Empty : $", OuterRelayAddress: {this.OuterEndPoint}";

        return $"RelayPoint: {this.RelayPoint}{outerRelay}, RetensionMics: {this.RelayRetensionMics.MicsToTimeSpanString()}";
    }
}

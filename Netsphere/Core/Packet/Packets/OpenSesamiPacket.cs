// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Packet;

/// <summary>
/// Requests the address exposed by an OpenSesami-enabled relay.
/// </summary>
[TinyhandObject]
public sealed partial class OpenSesamiPacket : IPacket
{
    public static PacketType PacketType => PacketType.OpenSesami;

    public OpenSesamiPacket()
    {
    }
}

/// <summary>
/// Returns the address exposed by an OpenSesami-enabled relay.
/// </summary>
[TinyhandObject]
public sealed partial class OpenSesamiResponse : IPacket
{
    public static PacketType PacketType => PacketType.OpenSesamiResponse;

    public OpenSesamiResponse()
    {
    }

    public OpenSesamiResponse(NetAddress secretAddress)
    {
        this.SecretAddress = secretAddress;
    }

    [Key(0)]
    public NetAddress SecretAddress { get; set; }

    public override string ToString()
        => $"{this.SecretAddress}";
}

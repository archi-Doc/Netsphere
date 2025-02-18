// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere.Packet;

[TinyhandObject]
internal partial class ConnectPacket : IPacket
{
    public static PacketType PacketType => PacketType.Connect;

    public ConnectPacket()
    {
    }

    public ConnectPacket(EncryptionPublicKey clientPublicKey, int serverPublicKeyChecksum, NetNode? sourceNode)
    {
        this.SourceNode = sourceNode;
        this.ClientPublicKey = clientPublicKey;
        this.ServerPublicKeyChecksum = serverPublicKeyChecksum;
        this.ClientSalt = RandomVault.Default.NextUInt64();
        this.ClientSalt2 = RandomVault.Default.NextUInt64();
    }

    [Key(0)]
    public uint NetsphereId { get; set; }

    [Key(1)]
    public NetNode? SourceNode { get; set; }

    [Key(2)]
    public EncryptionPublicKey ClientPublicKey { get; set; }

    [Key(3)]
    public int ServerPublicKeyChecksum { get; set; }

    [Key(4)]
    public ulong ClientSalt { get; set; }

    [Key(5)]
    public ulong ClientSalt2 { get; set; }

    [Key(6)]
    public bool Bidirectional { get; set; }
}

[TinyhandObject]
internal partial class ConnectPacketResponse : IPacket
{
    public static PacketType PacketType => PacketType.ConnectResponse;

    public ConnectPacketResponse()
    {
        this.Agreement = ConnectionAgreement.Default with { }; // Create a new instance.
        this.ServerSalt = RandomVault.Default.NextUInt64();
        this.ServerSalt2 = RandomVault.Default.NextUInt64();
    }

    public ConnectPacketResponse(ConnectionAgreement agreement, NetEndpoint sourceEndpoint)
        : this()
    {
        // this.Success = true;
        this.Agreement = agreement with { }; // Create a new instance.
        this.SourceEndpoint = sourceEndpoint;
    }

    [Key(0)]
    public NetEndpoint SourceEndpoint { get; set; }

    [Key(1)]
    public ulong ServerSalt { get; set; }

    [Key(2)]
    public ulong ServerSalt2 { get; set; }

    [Key(3)]
    public ConnectionAgreement Agreement { get; set; }
}

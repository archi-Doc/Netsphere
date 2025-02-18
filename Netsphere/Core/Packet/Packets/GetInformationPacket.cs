// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere.Packet;

[TinyhandObject]
public partial class GetInformationPacket : IPacket
{
    public static PacketType PacketType => PacketType.GetInformation;

    public GetInformationPacket()
    {
    }
}

[TinyhandObject]
public partial class GetInformationPacketResponse : IPacket
{
    public static PacketType PacketType => PacketType.GetInformationResponse;

    public GetInformationPacketResponse()
    {
    }

    public GetInformationPacketResponse(EncryptionPublicKey publicKey, NetNode? ownNetNode)
    {
        this.PublicKey = publicKey;
        this.OwnNetNode = ownNetNode;
    }

    [Key(0)]
    public EncryptionPublicKey PublicKey { get; private set; }

    [Key(1)]
    public NetNode? OwnNetNode { get; private set; }
}

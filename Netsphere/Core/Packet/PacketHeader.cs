// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Relay;

namespace Netsphere.Packet;

#pragma warning disable CS0649

internal readonly struct PacketHeader
{// 18 bytes, PacketHeaderCode, CreatePacketCode
    public const int Length = 14 + (sizeof(RelayId) * 2);
    public const int MaxPayloadLength = NetConstants.MaxPacketLength - RelayHeader.Length - Length - RelayHeader.TagSize;
    public const int MaxFrameLength = NetConstants.MaxPacketLength - RelayHeader.Length - Length - ProtectedPacket.Length - ProtectedPacket.TagSize - RelayHeader.TagSize;

    public readonly RelayId SourceRelayId; // 2 bytes
    public readonly RelayId DestinationRelayId; // 2 bytes
    public readonly uint HashSalt; // 4 bytes, Hash / Salt
    public readonly PacketType PacketType; // 2 bytes
    public readonly ulong Id; // 8 bytes, Packet id / Connection id
    // Frame
}

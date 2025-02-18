// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Packet;

#pragma warning disable CS0649

internal readonly struct ProtectedPacket
{// Protected = Salt + Nonce + Encryption, ProtectedPacketCode
    public const int Length = 8;
    public const int TagSize = Aegis256.MinTagSize;

    public readonly ulong Nonce;
    // public readonly FrameType FrameType;
    // Frame
}

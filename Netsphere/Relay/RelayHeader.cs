// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Netsphere.Relay;

#pragma warning disable CS0649

[StructLayout(LayoutKind.Explicit)]
public readonly struct RelayHeader
{// 32 bytes, RelayHeaderCode
    public const int RelayIdLength = sizeof(RelayId) * 2; // SourceRelayId/DestinationRelayId
    public const int PlainLength = 4; // Salt
    public const int CipherLength = 28; // 36; // Zero, NetAddress
    public const int Length = PlainLength + CipherLength;
    public const int TagSize = Aegis128L.MinTagSize;

    public RelayHeader(uint salt, NetAddress netAddress)
    {
        this.Salt = salt;
        this.NetAddress = netAddress;
    }

    [FieldOffset(0)]
    public readonly uint Salt; // 4 bytes

    // The byte sequence starting from zero is subject to encryption.
    [FieldOffset(4)]
    public readonly uint Zero; // 4 bytes.
    [FieldOffset(8)]
    public readonly NetAddress NetAddress; // 24 bytes
}

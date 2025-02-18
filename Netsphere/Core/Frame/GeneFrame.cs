// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Core;

namespace Netsphere.Packet;

#pragma warning disable CS0649

// NetTerminalGene by Nihei.
internal readonly struct FirstGeneFrame
{// FirstGeneFrameCode
    public const int Length = 30;
    public const int LengthExcludingFrameType = Length - 2;
    public const int MaxGeneLength = PacketHeader.MaxFrameLength - Length;

    public readonly FrameType FrameType; // 2 bytes
    public readonly ushort TransmissionMode; // 2 bytes 0:Block, 1:Stream
    public readonly uint TransmissionId; // 4 bytes
    public readonly DataControl DataControl; // 2 bytes
    public readonly int RttHint; // 4 bytes
    public readonly int TotalGene; // 4 bytes (StreamMaxLength for Stream)
    public readonly uint DataKind; // 4 bytes (StreamMaxLength for Stream)
    public readonly ulong DataId; // 8 bytes
}

internal readonly struct FollowingGeneFrame
{// FollowingGeneFrameCode
    public const int Length = 12;
    public const int LengthExcludingFrameType = Length - 2;
    public const int MaxGeneLength = PacketHeader.MaxFrameLength - Length;

    public readonly FrameType FrameType; // 2 bytes
    public readonly uint TransmissionId; // 4 bytes
    public readonly DataControl DataControl; // 2 bytes
    public readonly int DataPosition; // 4 bytes
}

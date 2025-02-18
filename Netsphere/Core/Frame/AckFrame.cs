// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Packet;

#pragma warning disable CS0649

internal readonly struct AckFrame
{// AckFrameCode
    public const int Margin = 32;

    public readonly FrameType FrameType; // 2 bytes

    // Burst (Complete)
    // public readonly int ReceiveCapacity; // -1
    // public readonly uint TransmissionId;

    // Block/Stream
    // public readonly int MaxReceivePosition;
    // public readonly uint TransmissionId;
    // public readonly int SuccessiveReceivedPosition;
    // public readonly ushort NumberOfPairs; // 2 bytes
    //   public readonly int StartGene; // 4 bytes
    //   public readonly int EndGene; // 4 bytes
}

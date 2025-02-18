// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Packet;

#pragma warning disable CS0649

internal readonly struct KnockFrame
{// KnockFrameCode
    public const int Length = 6;

    public readonly FrameType FrameType; // 2 bytes
    public readonly uint TransmissionId; // 4 bytes
}

internal readonly struct KnockResponseFrame
{// KnockResponseFrameCode
    public const int Length = 10;

    public readonly FrameType FrameType; // 2 bytes
    public readonly uint TransmissionId; // 4 bytes
    public readonly int MaxReceivePosition; // 4 bytes
}

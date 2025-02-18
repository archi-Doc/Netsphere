// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Packet;

#pragma warning disable CS0649

internal readonly struct CloseFrame
{// CloseFrameCode
    public const int Length = 2;

    public readonly FrameType FrameType; // 2 bytes
}

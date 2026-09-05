// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Packet;

/// <summary>
/// Identifies a frame carried inside an encrypted connection packet.
/// </summary>
public enum FrameType : ushort
{
    Close,
    Ack,
    FirstGene,
    FollowingGene,
    Knock,
    KnockResponse,
    // Stream,
}

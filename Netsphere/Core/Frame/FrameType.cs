// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Packet;

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

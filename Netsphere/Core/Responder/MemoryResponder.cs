// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Core;

namespace Netsphere.Responder;

public class MemoryResponder : SyncResponder<Memory<byte>, Memory<byte>>
{
    public static readonly INetResponder Instance = new MemoryResponder();

    public override NetResultValue<Memory<byte>> RespondSync(Memory<byte> value)
        => new(NetResult.Success, value);
}

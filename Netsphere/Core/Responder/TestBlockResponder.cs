// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Core;
using Netsphere.Misc;

namespace Netsphere.Responder;

public class TestBlockResponder : SyncResponder<TestBlock, TestBlock>
{
    public static readonly INetResponder Instance = new TestBlockResponder();

    public override NetResultValue<TestBlock> RespondSync(TestBlock value)
        => new(NetResult.Success, value);
}

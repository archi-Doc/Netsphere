// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Tinyhand;

namespace Lp.NetServices;

[TinyhandObject(ImplicitMemberNameAsKey = true)]
public partial record RemoteBenchRecord
{
    public int SuccessCount { get; init; }

    public int FailureCount { get; init; }

    public int Concurrent { get; init; }

    public long ElapsedMilliseconds { get; init; }

    public int CountPerSecond { get; init; }

    public int AverageLatency { get; init; }
}

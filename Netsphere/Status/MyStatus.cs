// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using Arc.Threading;

namespace LP.Netsphere;

public class MyStatus
{
    public enum ConnectionType
    {
        Unknown,
        Global,
        NAT,
        Symmetric,
    }

    public MyStatus()
    {
    }

    public ConnectionType Type { get; private set; }

    public double EstimatedMBPS { get; private set; }
}

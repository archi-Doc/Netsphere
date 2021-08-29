// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using Arc.Threading;

namespace LP.Netsphere;

public class MyStatus
{
    public enum NetType
    {
        Unknown,
        Direct,
        NAT,
        Symmetric,
    }

    public MyStatus()
    {
    }

    public NetType Type { get; set; }
}

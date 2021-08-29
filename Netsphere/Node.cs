// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Net;
using Arc.Threading;

namespace LP.Netsphere;

public enum NodeType : byte
{
    Development,
    Release,
}

public class NodeAddress
{
    public NodeType Type { get; set; }

    public byte Engagement { get; set; }

    public ushort Port { get; set; }

    public IPAddress Address { get; set; } = IPAddress.None;
}

public class NodeInformation : NodeAddress
{
}

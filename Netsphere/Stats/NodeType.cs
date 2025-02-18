// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Stats;

public enum NodeType
{
    /// <summary>
    /// The node type is unknown.
    /// </summary>
    Unknown,

    /// <summary>
    /// The configured port number and the number visible to the other party are the same.
    /// </summary>
    Direct,

    /// <summary>
    /// The port numbers is translated through a NAT. The same port number is visible to multiple peers.
    /// </summary>
    Cone,

    /// <summary>
    /// The port numbers is translated through a NAT. Different port numbers are visible to multiple peer.
    /// </summary>
    Symmetric,
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Stats;

/// <summary>
/// Classifies a node's network reachability.
/// </summary>
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
    /// The NAT exposes the same translated port to multiple peers.
    /// </summary>
    Cone,

    /// <summary>
    /// The NAT exposes different translated ports to different peers.
    /// </summary>
    Symmetric,
}

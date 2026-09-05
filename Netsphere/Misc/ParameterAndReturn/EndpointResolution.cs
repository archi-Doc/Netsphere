// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Represents the resolution preference for network endpoints.
/// </summary>
public enum EndpointResolution : byte
{
    /// <summary>
    /// Prefer IPv6 endpoints.
    /// </summary>
    PreferIpv6,

    /// <summary>
    /// Use endpoints specified by NetAddress.
    /// </summary>
    NetAddress,

    /// <summary>
    /// Uses IPv4 endpoints only.
    /// </summary>
    Ipv4,

    /// <summary>
    /// Uses IPv6 endpoints only.
    /// </summary>
    Ipv6,
}

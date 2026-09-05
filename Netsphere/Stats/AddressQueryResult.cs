// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Stats;

/// <summary>
/// Contains the source and optional address returned by a public-address query.
/// </summary>
/// <param name="Uri">The address-query service URI.</param>
/// <param name="Address">The returned IP address, or null if the query failed.</param>
public record struct AddressQueryResult(string? Uri, IPAddress? Address)
{
    public bool IsValidIpv4 => this.Address?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

    public bool IsValidIpv6 => this.Address?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
}

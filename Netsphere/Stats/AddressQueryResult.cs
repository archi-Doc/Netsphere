// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Stats;

public record struct AddressQueryResult(string? Uri, IPAddress? Address)
{
    public bool IsValidIpv4 => this.Address?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

    public bool IsValidIpv6 => this.Address?.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
}

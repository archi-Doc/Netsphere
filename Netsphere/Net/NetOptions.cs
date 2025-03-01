// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Netsphere;

[TinyhandObject(ImplicitKeyAsName = true)]
public partial record NetOptions
{
    [SimpleOption("NodeName", Description = "Node name", GetEnvironmentVariable = true)]
    public string NodeName { get; set; } = string.Empty;

    // [SimpleOption("address", Description = "Global IP address")]
    // public string Address { get; set; } = string.Empty;

    [SimpleOption("Port", Description = "Port number associated with the address", GetEnvironmentVariable = true)]
    public int Port { get; set; }

    [SimpleOption("NodeSecretkey", Description = "Node secret key", GetEnvironmentVariable = true)]
    public string NodeSecretKey { get; set; } = string.Empty;

    [SimpleOption("NodeList", Description = "Node addresses to connect", GetEnvironmentVariable = true)]
    public string NodeList { get; set; } = string.Empty;

    [SimpleOption("NetsphereId", Description = "Netsphere Id", GetEnvironmentVariable = true)]
    public int NetsphereId { get; set; }

    [SimpleOption("Ping", Description = "Enable ping function")]
    public bool EnablePing { get; set; } = true;

    [SimpleOption("Server", Description = "Enable server function")]
    public bool EnableServer { get; set; } = false;

    [SimpleOption("Alternative", Description = "Enable alternative (debug) terminal")]
    public bool EnableAlternative { get; set; } = false;

    [SimpleOption("TemporaryIpv6", Description = "Enable temporary Ipv6 address")]
    public bool EnableTemporaryIpv6Address { get; set; } = false;
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere.Version;

public partial record UpdateOptions
{
    [SimpleOption("Address", Description = "Target address")]
    public string Address { get; init; } = string.Empty;

    [SimpleOption(NetConstants.RemoteSecretKeyName, Description = "Private key for remote operation", GetEnvironmentVariable = true)]
    public string RemoteSecretKey { get; set; } = string.Empty;

    [SimpleOption("VersionIdentifier", Description = "Version identifier", GetEnvironmentVariable = true)]
    public int VersionIdentifier { get; set; }

    [SimpleOption("Kind", Description = "Version kind (development, release)")]
    public VersionInfo.Kind VersionKind { get; init; } = VersionInfo.Kind.Development;

    public void Prepare()
    {
        if (SeedKey.TryParse(this.RemoteSecretKey, out var seedKey))
        {
            this.remoteSecretKey = seedKey;
        }

        this.RemoteSecretKey = string.Empty;
    }

    public SeedKey? remoteSecretKey { get; private set; }
}

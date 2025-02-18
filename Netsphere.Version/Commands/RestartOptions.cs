// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Lp.Subcommands;

public record RestartOptions
{
    [SimpleOption("RunnerNode", Description = "Runner nodes", Required = true)]
    public string RunnerNode { get; init; } = string.Empty;

    [SimpleOption(NetConstants.RemoteSecretKeyName, Description = "Secret signature key for remote operation", GetEnvironmentVariable = true)]
    public string RemoteSecretKey { get; set; } = string.Empty;

    [SimpleOption("ContainerPort", Description = "Port number associated with the container")]
    public ushort ContainerPort { get; init; } = 0;

    public bool IsValidContainerPort => this.ContainerPort > 0;

    public void Prepare()
    {
        if (SeedKey.TryParse(this.RemoteSecretKey, out var seedKey))
        {
            this.RemoteSeedKey = seedKey;
        }

        this.RemoteSecretKey = string.Empty;
    }

    public SeedKey? RemoteSeedKey { get; private set; }
}

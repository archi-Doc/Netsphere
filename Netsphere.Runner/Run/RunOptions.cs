// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using Arc.Unit;
using SimpleCommandLine;

namespace Netsphere.Runner;

public partial record RunOptions : RunnerOptions
{
    [SimpleOption("DockerParam", Description = "Parameters to be passed to the docker run command.")]
    public string DockerParameters { get; init; } = string.Empty;

    [SimpleOption("ContainerPort", Description = "Port number associated with the container")]
    public ushort ContainerPort { get; set; } = 0; // 0: Disabled

    [SimpleOption("ContainerParam", Description = "Parameters to be passed to the container.")]
    public string ContainerParameters { get; init; } = string.Empty;

    public override bool Check(ILogger logger)
    {
        var result = base.Check(logger);
        if (string.IsNullOrEmpty(this.Image))
        {
            logger.TryGet(LogLevel.Fatal)?.Log($"Specify the container image (-image).");
            result = false;
        }

        return result;
    }

    public bool TryGetContainerAddress(out NetAddress netAddress)
    {
        if (this.ContainerPort == 0)
        {
            netAddress = default;
            return false;
        }

        netAddress = new NetAddress(IPAddress.Loopback, this.ContainerPort);
        return true;
    }
}

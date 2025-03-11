// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Netsphere.Runner;

public partial record RestartOptions : RunnerOptions
{
    [SimpleOption(nameof(Project), Description = "Project name that the Runner operates", ReadFromEnvironment = true)]
    public string Project { get; init; } = string.Empty;

    [SimpleOption(nameof(Service), Description = "Service name that the Runner operates", Required = true, ReadFromEnvironment = true)]
    public string Service { get; init; } = string.Empty;
}

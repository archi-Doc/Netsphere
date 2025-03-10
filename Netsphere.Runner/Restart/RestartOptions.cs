// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SimpleCommandLine;

namespace Netsphere.Runner;

public partial record RestartOptions : RunnerOptions
{
    [SimpleOption(nameof(Service), Description = "Service name that the Runner operates")]
    public string Service { get; init; } = string.Empty;
}

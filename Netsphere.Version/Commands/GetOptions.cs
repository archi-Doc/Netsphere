// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Version;

public partial record GetOptions
{
    [SimpleOption("Address", Description = "Target address")]
    public string Address { get; init; } = string.Empty;

    [SimpleOption("Kind", Description = "Version kind (development, release)")]
    public VersionInfo.Kind VersionKind { get; init; } = VersionInfo.Kind.Development;
}

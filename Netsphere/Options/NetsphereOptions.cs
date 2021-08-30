// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using SimpleCommandLine;

namespace LP.Netsphere;

public class NetsphereOptions
{
    [SimpleOption("address", null, "global IP address")]
    public string Address { get; set; } = string.Empty;
}

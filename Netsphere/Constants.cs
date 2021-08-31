// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using Arc.Threading;

namespace LP.Netsphere;

public static class Constants
{
    public const int MaxPayload = 1432; // 1432 bytes
    public const int MinPort = 49152; // Ephemeral port 49152 - 60999
    public const int MaxPort = 60999;
}

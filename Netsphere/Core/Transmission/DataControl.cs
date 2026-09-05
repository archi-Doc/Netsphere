// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Core;

/// <summary>
/// Identifies valid stream data, completion, cancellation, or an initial state.
/// </summary>
public enum DataControl : ushort
{
    Initial,
    Valid,
    Complete,
    Cancel,
}

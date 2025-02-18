// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Validate that object members are appropriate.
/// </summary>
public interface IValidatable
{
    /// <summary>
    /// Validate that object members are appropriate.
    /// </summary>
    /// <returns><see langword="true" />: Success.</returns>
    bool Validate();
}

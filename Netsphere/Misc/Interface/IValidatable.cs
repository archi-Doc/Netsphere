// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Defines validation of an object's contents.
/// </summary>
public interface IValidatable
{
    /// <summary>
    /// Checks whether the object's contents are valid.
    /// </summary>
    /// <returns>True on success; otherwise, false.</returns>
    bool Validate();
}

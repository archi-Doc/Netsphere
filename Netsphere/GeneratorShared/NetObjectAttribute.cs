// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Marks a server-side implementation of one or more network services.
/// </summary>
/// <remarks>Implement <see cref="INetObject"/> to receive connection lifecycle callbacks.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class NetObjectAttribute : Attribute
{
    /*/// <summary>
    /// Gets or sets a value indicating whether this object should be enabled by default (default is <see langword="true"/>).
    /// </summary>
    public bool EnableByDefault { get; set; } = true;*/

    public NetObjectAttribute()
    {
    }
}

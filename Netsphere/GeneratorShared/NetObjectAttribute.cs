// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// An attribute applied to classes that provide a NetService on the server side.<br/>
/// If necessary, the class can also implement <see cref="INetObject"/> to add connection-related callback methods.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class NetObjectAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether this class should be registered automatically to the dependency injection container (default is <see langword="true"/>).
    /// </summary>
    public bool EnableAutoRegistration { get; set; } = true;

    public NetObjectAttribute()
    {
    }
}

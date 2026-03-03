// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// An attribute applied to classes that provide a NetService on the server side.<br/>
/// If necessary, the class can also implement <see cref="INetServiceObject"/> to add connection-related callback methods.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class NetServiceObjectAttribute : Attribute
{
    public NetServiceObjectAttribute()
    {
    }
}

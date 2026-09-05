// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Marks an interface for generated network service proxies and dispatchers.
/// </summary>
/// <remarks>The interface must inherit <see cref="INetService"/> and use its supported method signatures.</remarks>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class NetServiceAttribute : Attribute
{
    /// <summary>
    /// Gets or sets an identifier of the net service [0: auto-generated from the interface full name].
    /// </summary>
    public uint ServiceId { get; set; } = 0;

    public NetServiceAttribute(uint serviceId = 0)
    {
        this.ServiceId = serviceId;
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// An attribute that is applied to the interface when defining a NetService.<br/>
/// The requirements are to add the <see cref="NetServiceInterfaceAttribute" /> and to derive from the <see cref="INetService" />.<br/>
/// The return type of the interface function must be either <see cref="NetTask"/> or <see cref="NetTask{TResponse}"/>(TResponse is Tinyhand serializable).
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class NetServiceInterfaceAttribute : Attribute
{
    /// <summary>
    /// Gets or sets an identifier of the net service [0: auto-generated from the interface full name].
    /// </summary>
    public uint ServiceId { get; set; } = 0;

    public NetServiceInterfaceAttribute(uint serviceId = 0)
    {
        this.ServiceId = serviceId;
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.CodeAnalysis;

namespace Netsphere.Generator;

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class NetServiceInterfaceAttributeMock : Attribute
{
    public static readonly string SimpleName = "NetServiceInterface";
    public static readonly string StandardName = SimpleName + "Attribute";
    public static readonly string FullName = "Netsphere." + StandardName;

    public NetServiceInterfaceAttributeMock(uint serviceId = 0)
    {
        this.ServiceId = serviceId;
    }

    public uint ServiceId { get; set; } = 0;

    public Location Location { get; set; } = Location.None;

    public static NetServiceInterfaceAttributeMock FromArray(object?[] constructorArguments, KeyValuePair<string, object?>[] namedArguments)
    {
        var attribute = new NetServiceInterfaceAttributeMock();
        object? val;

        val = AttributeHelper.GetValue(0, nameof(ServiceId), constructorArguments, namedArguments);
        if (val != null)
        {
            attribute.ServiceId = (uint)val;
        }

        return attribute;
    }
}

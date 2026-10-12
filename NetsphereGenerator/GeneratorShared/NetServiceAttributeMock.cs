// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.CodeAnalysis;

namespace Netsphere.Generator;

[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class NetServiceAttributeMock : Attribute
{
    public static readonly string SimpleName = "NetService";
    public static readonly string StandardName = SimpleName + "Attribute";
    public static readonly string FullName = "Netsphere." + StandardName;

    public NetServiceAttributeMock(uint serviceId = 0)
    {
        this.ServiceId = serviceId;
    }

    public uint ServiceId { get; set; } = 0;

    public Location Location { get; set; } = Location.None;

    public static NetServiceAttributeMock FromArray(object?[] constructorArguments, KeyValuePair<string, object?>[] namedArguments)
    {
        var attribute = new NetServiceAttributeMock();
        object? val;

        // Named properties are assigned after the constructor, including its optional default.
        val = AttributeHelper.GetValue(-1, nameof(ServiceId), constructorArguments, namedArguments)
            ?? AttributeHelper.GetValue(0, null, constructorArguments, namedArguments);
        if (val != null)
        {
            attribute.ServiceId = (uint)val;
        }

        return attribute;
    }
}

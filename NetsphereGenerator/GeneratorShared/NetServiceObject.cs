// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.CodeAnalysis;

namespace Netsphere.Generator;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class NetServiceObjectAttributeMock : Attribute
{
    public static readonly string SimpleName = "NetServiceObject";
    public static readonly string StandardName = SimpleName + "Attribute";
    public static readonly string FullName = "Netsphere." + StandardName;

    public bool EnableAutoRegistration { get; set; } = true;

    public NetServiceObjectAttributeMock()
    {
    }

    public Location Location { get; set; } = Location.None;

    public static NetServiceObjectAttributeMock FromArray(object?[] constructorArguments, KeyValuePair<string, object?>[] namedArguments)
    {
        var attribute = new NetServiceObjectAttributeMock();

        object? val;
        val = AttributeHelper.GetValue(-1, nameof(EnableAutoRegistration), constructorArguments, namedArguments);
        if (val != null)
        {
            attribute.EnableAutoRegistration = (bool)val;
        }

        return attribute;
    }
}

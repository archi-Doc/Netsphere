// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.CodeAnalysis;

namespace Netsphere.Generator;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class NetObjectAttributeMock : Attribute
{
    public static readonly string SimpleName = "NetObject";
    public static readonly string StandardName = SimpleName + "Attribute";
    public static readonly string FullName = "Netsphere." + StandardName;

    public bool EnableByDefault { get; set; } = true;

    public NetObjectAttributeMock()
    {
    }

    public Location Location { get; set; } = Location.None;

    public static NetObjectAttributeMock FromArray(object?[] constructorArguments, KeyValuePair<string, object?>[] namedArguments)
    {
        var attribute = new NetObjectAttributeMock();

        object? val;
        val = AttributeHelper.GetValue(-1, nameof(EnableByDefault), constructorArguments, namedArguments);
        if (val != null)
        {
            attribute.EnableByDefault = (bool)val;
        }

        return attribute;
    }
}

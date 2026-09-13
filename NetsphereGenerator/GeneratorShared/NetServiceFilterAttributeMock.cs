// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.CodeAnalysis;

namespace Netsphere.Generator;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
public class NetServiceFilterAttributeMock : Attribute
{
    public static readonly string SimpleName = "NetServiceFilter";
    public static readonly string StandardName = SimpleName + "Attribute";
    public static readonly string FullName = "Netsphere." + StandardName;
    public static readonly string StartName = FullName + "<";

    public NetServiceFilterAttributeMock(Location location)
    {
        this.Location = location;
    }

    public int Order { get; set; } = int.MaxValue;

    public string Arguments { get; set; } = string.Empty;

    public Location Location { get; set; } = Location.None;

    public ISymbol? FilterType { get; set; }

    public static NetServiceFilterAttributeMock FromArray(object?[] constructorArguments, KeyValuePair<string, object?>[] namedArguments, Location location)
    {
        var attribute = new NetServiceFilterAttributeMock(location);
        object? val;

        val = AttributeHelper.GetValue(-1, nameof(Order), constructorArguments, namedArguments);
        if (val != null)
        {
            attribute.Order = (int)val;
        }

        val = AttributeHelper.GetValue(-1, nameof(Arguments), constructorArguments, namedArguments);
        if (val != null)
        {
            attribute.Arguments = (string)val;
        }

        return attribute;
    }
}

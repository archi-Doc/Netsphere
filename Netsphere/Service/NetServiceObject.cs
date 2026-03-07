// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace Netsphere;

public class NetServiceObject
{
    public NetServiceObject(Type objectType, Func<object>? objectFactory)
    {
        this.Type = objectType;
        this.Factory = objectFactory;
    }

    public void AddMethod(ServiceMethod serviceMethod)
        => this.serviceMethods.TryAdd(serviceMethod.Id, serviceMethod);

    public bool TryGetMethod(ulong id, [MaybeNullWhen(false)] out ServiceMethod serviceMethod)
        => this.serviceMethods.TryGetValue(id, out serviceMethod);

    public Type Type { get; }

    public Func<object>? Factory { get; }

    private UInt64Hashtable<ServiceMethod> serviceMethods = new();
}

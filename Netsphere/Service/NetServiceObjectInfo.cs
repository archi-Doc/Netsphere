// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace Netsphere;

public class NetServiceObjectInfo
{
    public NetServiceObjectInfo(Type objectType, Func<object>? objectFactory)
    {
        this.ObjectType = objectType;
        this.ObjectFactory = objectFactory;
    }

    public void AddMethod(ServiceMethod serviceMethod)
        => this.serviceMethods.TryAdd(serviceMethod.Id, serviceMethod);

    public bool TryGetMethod(ulong id, [MaybeNullWhen(false)] out ServiceMethod serviceMethod)
        => this.serviceMethods.TryGetValue(id, out serviceMethod);

    public Type ObjectType { get; }

    public Func<object>? ObjectFactory { get; }

    private UInt64Hashtable<ServiceMethod> serviceMethods = new();
}

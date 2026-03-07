// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace Netsphere;

internal class NetsphereUnitContext : INetsphereUnitContext, IUnitCustomContext
{
    internal readonly record struct NetObjectAndLifetime(Type ObjectType, ServiceLifetime ServiceLifetime);

    internal Dictionary<Type, NetObjectAndLifetime> NetServices { get; } = new();

    void IUnitCustomContext.ProcessContext(IUnitConfigurationContext context)
    {
        context.SetOptions(this);

        foreach (var x in StaticNetService.ServiceToObject.ToArray())
        {// x: (INetService, NetServiceObject)
            if (!this.NetServices.ContainsKey(x.Key))
            {// INetService, (NetServiceObjectType, Lifetime)
                this.NetServices.TryAdd(x.Key, new(x.Value.Type, ServiceLifetime.Scoped));
            }
        }

        foreach (var x in this.NetServices)
        {// INetService, NetServiceObjectType, Lifetime
            var serviceDescriptor = ServiceDescriptor.Describe(x.Key, x.Value.ObjectType, x.Value.ServiceLifetime);
            context.Services.Add(serviceDescriptor); // INetService -> NetServiceObject
        }
    }

    void INetsphereUnitContext.AddNetService<TNetService, TNetObject>(ServiceLifetime lifetime)
    {
        this.NetServices.Add(typeof(TNetService), new(typeof(TNetObject), lifetime));
    }
}

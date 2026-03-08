// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace Netsphere;

internal class NetsphereUnitContext : INetsphereUnitContext, IUnitCustomContext
{
    // internal readonly record struct NetObjectAndLifetime(Type ObjectType, ServiceLifetime ServiceLifetime);

    internal Dictionary<Type, ServiceDescriptor> NetServices { get; } = new();

    void IUnitCustomContext.ProcessContext(IUnitConfigurationContext context)
    {
        context.SetOptions(this);

        foreach (var x in StaticNetService.ServiceInfoTable.ToArray())
        {// x: (INetService, NetServiceInfo)
            if (!this.NetServices.ContainsKey(x.Key))
            {
                this.AddNetService(ServiceDescriptor.Transient(x.Key, x.Value.NetObjectInfo.ObjectType));
            }
        }

        foreach (var x in this.NetServices)
        {// INetService, NetObjectType, Lifetime
            context.Services.Add(x.Value); // INetService -> NetObject
        }
    }

    void INetsphereUnitContext.AddNetService<TNetService, TNetObject>()
    {
        this.AddNetService(ServiceDescriptor.Transient<TNetService, TNetObject>());
    }

    void INetsphereUnitContext.AddNetService<TNetService, TNetObject>(Func<IServiceProvider, TNetObject> factory)
    {
        this.AddNetService(ServiceDescriptor.Transient<TNetService, TNetObject>(factory));
    }

    private void AddNetService(ServiceDescriptor serviceDescriptor)
    {
        if (serviceDescriptor.ImplementationType is null ||
            !StaticNetService.TryGetNetObjectInfo(serviceDescriptor.ImplementationType, out var netObjectInfo))
        {
            throw new InvalidOperationException();
        }

        this.NetServices[serviceDescriptor.ServiceType] = serviceDescriptor;
    }
}

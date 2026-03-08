// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Netsphere.Service;

namespace Netsphere;

internal class NetsphereUnitContext : INetsphereUnitContext, IUnitCustomContext
{
    // internal readonly record struct NetObjectAndLifetime(Type ObjectType, ServiceLifetime ServiceLifetime);

    internal Dictionary<Type, ObjectTypeAndServiceDescriptor> NetServices { get; } = new();

    void IUnitCustomContext.ProcessContext(IUnitConfigurationContext context)
    {
        context.SetOptions(this);

        foreach (var x in StaticNetService.ServiceInfoTable.ToArray())
        {// x: (INetService, NetServiceInfo)
            if (!this.NetServices.ContainsKey(x.Key))
            {
                var objectType = x.Value.NetObjectInfo.ObjectType;
                // this.AddNetService(new(objectType, ServiceDescriptor.Transient(x.Key, objectType), x.Value.EnableByDefault));
                this.AddNetService(new(objectType, ServiceDescriptor.Transient(x.Key, objectType), x.Value.EnableByDefault));
            }
        }

        foreach (var x in this.NetServices)
        {// INetService, NetObjectType, Lifetime
            context.Services.Add(x.Value.ServiceDescriptor); // INetService -> NetObject
        }
    }

    void INetsphereUnitContext.AddNetService<TNetService, TNetObject>(bool enableByDefault)
    {
        this.AddNetService(new(typeof(TNetObject), ServiceDescriptor.Transient<TNetService, TNetObject>(), enableByDefault));
    }

    void INetsphereUnitContext.AddNetService<TNetService, TNetObject>(Func<IServiceProvider, TNetObject> factory, bool enableByDefault)
    {
        this.AddNetService(new(typeof(TNetObject), ServiceDescriptor.Transient<TNetService, TNetObject>(factory), enableByDefault));
    }

    private void AddNetService(ObjectTypeAndServiceDescriptor netService)
    {
        if (!StaticNetService.TryGetNetObjectInfo(netService.ObjectType, out var netObjectInfo))
        {
            throw new InvalidOperationException("The specified NetService type is not registered.");
        }

        this.NetServices[netService.ServiceDescriptor.ServiceType] = netService;
    }
}

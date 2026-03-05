// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Netsphere;

internal class NetsphereUnitContext : INetsphereUnitContext, IUnitCustomContext
{
    internal readonly record struct Item(Type ServiceType, Type ObjectType, ServiceLifetime ServiceLifetime);

    private readonly List<Item> items = new();

    void IUnitCustomContext.ProcessContext(IUnitConfigurationContext context)
    {
        context.SetOptions(this);

        foreach (var x in StaticNetService.ServiceToObject)
        {
            context.Services.TryAddScoped(x.Key, x.Value.Type);
        }

        foreach (var x in this.items)
        {
            var serviceDescriptor = ServiceDescriptor.Describe(x.ServiceType, x.ObjectType, x.ServiceLifetime);
            context.Services.Add(serviceDescriptor);
        }
    }

    void INetsphereUnitContext.AddNetService<TNetService, TNetObject>(ServiceLifetime lifetime)
    {
        this.items.Add(new(typeof(TNetService), typeof(TNetObject), lifetime));
    }
}

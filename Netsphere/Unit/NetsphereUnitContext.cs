// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Netsphere;

internal class NetsphereUnitContext : INetsphereUnitContext, IUnitCustomContext
{
    void IUnitCustomContext.Configure(IUnitConfigurationContext context)
    {
        context.SetOptions(this);

        foreach (var x in this.ServiceToAgent.Values)
        {
            context.Services.TryAddTransient(x);
        }
    }

    void INetsphereUnitContext.AddNetService<TService, TAgent>()
    {
        this.ServiceToAgent.TryAdd(typeof(TService), typeof(TAgent));
    }

    internal Dictionary<Type, Type> ServiceToAgent { get; } = new();
}

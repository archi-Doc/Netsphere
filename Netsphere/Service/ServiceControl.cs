// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using static Netsphere.NetsphereUnitContext;

namespace Netsphere.Service;

public sealed class ServiceControl
{
    internal ServiceControl(NetsphereUnitContext context)
    {
        this.netServices = new();
        foreach (var x in context.NetServices)
        {
            if (StaticNetService.TryGetNetServiceObject(x.Value.ObjectType, out var netServiceObject))
            {
                this.netServices.TryAdd(x.Key, netServiceObject);
            }
        }
    }

    #region FieldAndProperty

    private readonly Dictionary<Type, NetServiceObject> netServices;
    private readonly Dictionary<Type, NetServiceObject> enabledServices = new();
    private NetServiceItem[]? serviceArray;

    #endregion

    public void EnableNetService<TNetService>()
        where TNetService : INetService
    {
        if (!this.netServices.TryGetValue(typeof(TNetService), out var netServiceObject))
        {
            throw new InvalidOperationException();
        }

        this.enabledServices.TryAdd(typeof(TNetService), netServiceObject);
    }

    internal NetServiceItem[] GetServiceArray()
    {
        var array = this.serviceArray;
        if (array is null)
        {
            var enabledArray = this.enabledServices.ToArray();
            array = new NetServiceItem[enabledArray.Length];
            for (var i = 0; i < array.Length; i++)
            {
                array[i] = new(StaticNetService.GetServiceId(enabledArray[i].Key), enabledArray[i].Value);
            }

            this.serviceArray = array;
        }

        var newArray = new NetServiceItem[array.Length];
        Array.Copy(array, newArray, array.Length);
        return newArray;
    }

    private void ResetServiceArray()
        => this.serviceArray = default;
}

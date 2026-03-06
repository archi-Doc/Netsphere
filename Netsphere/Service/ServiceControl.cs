// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Netsphere.Service;

public sealed class ServiceControl
{
    internal ServiceControl(NetsphereUnitContext context)
    {
    }

    #region FieldAndProperty

    private readonly Dictionary<Type, NetServiceObject> serviceToObject = new();
    private readonly Dictionary<Type, NetServiceObject> enabledServices = new();
    private NetServiceItem[]? serviceArray;

    #endregion

    public void EnableNetService<TNetService>()
        where TNetService : INetService
    {
        if (!this.serviceToObject.TryGetValue(typeof(TNetService), out var netServiceObject))
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

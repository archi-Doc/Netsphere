// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Service;

public sealed class ServiceControl
{
    internal ServiceControl(NetsphereUnitContext context)
    {
        this.netServices = new();
        foreach (var x in context.NetServices)
        {// x.Key = NetService, x.Value.ImplementationType = NetObject

            if (x.Value.ImplementationType is { } objectType &&
                StaticNetService.TryGetNetServiceObjectInfo(objectType, out var netServiceObject))
            {
                this.netServices.TryAdd(x.Key, new(x.Key, netServiceObject));
            }
        }
    }

    #region FieldAndProperty

    private readonly Lock lockObject = new();
    private readonly Dictionary<Type, NetServiceInfo> netServices; // NetService -> NetServiceInfo
    private readonly Dictionary<Type, NetServiceInfo> enabledServices = new(); // NetService -> NetServiceInfo
    private NetServiceItem[]? cachedArray;

    #endregion

    public void EnableNetService<TNetService>()
        where TNetService : class, INetService
    {
        using (this.lockObject.EnterScope())
        {
            if (!this.netServices.TryGetValue(typeof(TNetService), out var netServiceObject))
            {
                throw new InvalidOperationException();
            }

            this.enabledServices.TryAdd(typeof(TNetService), netServiceObject);
            this.ResetServiceArray();
        }
    }

    public bool DisableService<TNetService>()
        where TNetService : class, INetService
    {
        using (this.lockObject.EnterScope())
        {
            var result = this.enabledServices.Remove(typeof(TNetService));
            this.ResetServiceArray();
        }

        return result;
    }

    internal NetServiceItem[] GetServiceArray()
    {
        var array = this.cachedArray;
        if (array is null)
        {
            using (this.lockObject.EnterScope())
            {
                array = new NetServiceItem[this.enabledServices.Count];
                var i = 0;
                foreach (var x in this.enabledServices.Values)
                {
                    array[i++] = new(x);
                }

                this.cachedArray = array;
            }
        }

        var newArray = new NetServiceItem[array.Length];
        Array.Copy(array, newArray, array.Length);
        return newArray;
    }

    private void ResetServiceArray()
        => this.cachedArray = default;
}

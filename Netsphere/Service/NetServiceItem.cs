// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Service;

internal record struct NetServiceItem
{
    public NetServiceItem(uint serviceId, NetServiceObject netServiceObject)
    {
        this.ServiceId = serviceId;
        this.NetServiceObject = netServiceObject;
    }

    public readonly uint ServiceId;

    public readonly NetServiceObject NetServiceObject;

    public object? Instance;
}

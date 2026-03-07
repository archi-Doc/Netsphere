// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Service;

internal record struct NetServiceItem
{
    public NetServiceItem(uint serviceId, Type serviceType)
    {
        this.ServiceId = serviceId;
        this.ServiceType = serviceType;
    }

    public readonly uint ServiceId;

    public readonly Type ServiceType;

    public object? Instance;
}

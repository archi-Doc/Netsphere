// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Service;

internal record struct NetServiceItem
{
    public NetServiceItem(NetServiceInfo netServiceInfo)
    {
        this.NetServiceInfo = netServiceInfo;
    }

    public readonly NetServiceInfo NetServiceInfo;

    public object? Instance;
}

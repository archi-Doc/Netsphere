// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

public record class NetServiceInfo
{
    public NetServiceInfo(Type serviceType, NetServiceObjectInfo netServiceObjectInfo)
    {
        this.ServiceId = StaticNetService.GetServiceId(serviceType);
        this.ServiceType = serviceType;
        this.NetServiceObjectInfo = netServiceObjectInfo;
    }

    public uint ServiceId { get; }

    public Type ServiceType { get; }

    public NetServiceObjectInfo NetServiceObjectInfo { get; }
}

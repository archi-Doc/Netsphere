// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

public record class NetServiceInfo
{
    public NetServiceInfo(Type serviceType, NetObjectInfo netObjectInfo)
    {
        this.ServiceId = StaticNetService.GetServiceId(serviceType);
        this.ServiceType = serviceType;
        this.NetObjectInfo = netObjectInfo;
    }

    public uint ServiceId { get; }

    public Type ServiceType { get; }

    public NetObjectInfo NetObjectInfo { get; }
}

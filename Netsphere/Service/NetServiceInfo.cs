// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

public record class NetServiceInfo
{
    public NetServiceInfo(Type serviceType, NetObjectInfo netObjectInfo, bool enableByDefault)
    {
        this.ServiceId = StaticNetService.GetServiceId(serviceType);
        this.ServiceType = serviceType;
        this.NetObjectInfo = netObjectInfo;
        this.EnableByDefault = enableByDefault;
    }

    public uint ServiceId { get; }

    public Type ServiceType { get; }

    public NetObjectInfo NetObjectInfo { get; }

    public bool EnableByDefault { get; }
}

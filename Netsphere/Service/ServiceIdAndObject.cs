// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Service;

internal readonly record struct ServiceIdAndObject
{
    public ServiceIdAndObject(uint serviceId, NetServiceObject agentInformation)
    {
        this.ServiceId = serviceId;
        this.AgentInformation = agentInformation;
    }

    public readonly uint ServiceId;

    public readonly NetServiceObject AgentInformation;
}

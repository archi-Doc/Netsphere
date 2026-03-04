// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Service;

internal readonly record struct ServiceIdAndAgent
{
    public ServiceIdAndAgent(uint serviceId, AgentInformation agentInformation)
    {
        this.ServiceId = serviceId;
        this.AgentInformation = agentInformation;
    }

    public readonly uint ServiceId;

    public readonly AgentInformation AgentInformation;
}

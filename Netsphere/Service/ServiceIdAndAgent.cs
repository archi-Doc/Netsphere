// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Netsphere.Service;

internal readonly record struct ServiceIdAndAgent
{
    public ServiceIdAndAgent(int serviceId, AgentInformation agentInformation)
    {
        this.ServiceId = serviceId;
        this.AgentInformation = agentInformation;
    }

    public readonly int ServiceId;

    public readonly AgentInformation AgentInformation;
}

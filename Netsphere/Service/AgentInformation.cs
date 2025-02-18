// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace Netsphere;

public class AgentInformation
{
    public AgentInformation(Type agentType, Func<object>? createAgent)
    {
        // this.ServiceId = serviceId;
        this.AgentType = agentType;
        this.CreateAgent = createAgent;
    }

    public void AddMethod(ServiceMethod serviceMethod) => this.serviceMethods.TryAdd(serviceMethod.Id, serviceMethod);

    public bool TryGetMethod(ulong id, [MaybeNullWhen(false)] out ServiceMethod serviceMethod) => this.serviceMethods.TryGetValue(id, out serviceMethod);

    // public uint ServiceId { get; }

    public Type AgentType { get; }

    public Func<object>? CreateAgent { get; }

    private Dictionary<ulong, ServiceMethod> serviceMethods = new();
}

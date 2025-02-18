// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

public record class ServiceMethod
{
    public delegate Task ServiceDelegate(object instance, TransmissionContext transmissionContext);

    public ServiceMethod(ulong id, ServiceDelegate invoke)
    {// Id = ServiceId + MethodId
        this.Id = id;
        this.Invoke = invoke;
    }

    public ulong Id { get; }

    public ServiceDelegate Invoke { get; }
}

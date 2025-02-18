// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

public interface INetsphereUnitContext
{
    /// <summary>
    /// Register the type of net service and the type of agent that implements it.<br/>
    /// The net service is enabled throughout NetControl.<br/>
    /// It can also be changed dynamically with <see cref="ServiceControl.Register{TService, TAgent}()"/>.<br/>
    /// It is recommended that the Agent type be registered in the ServiceProvider, but if it is not registered, it will be registered as Transient.
    /// </summary>
    /// <typeparam name="TService">The type of the net service to add.</typeparam>
    /// <typeparam name="TAgent">The type of the agent associated with the net service.</typeparam>
    void AddNetService<TService, TAgent>()
        where TService : INetService
        where TAgent : class, TService;
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace Netsphere;

public interface INetsphereUnitContext
{
    /*/// <summary>
    /// Register the type of net service and the type of net object that implements it.<br/>
    /// </summary>
    /// <typeparam name="TNetService">The type of the net service to add.</typeparam>
    /// <typeparam name="TNetObject">The type of the agent associated with the net service.</typeparam>
    /// <param name="lifetime">The lifetime of the service.</param>
    void AddNetService<TNetService, TNetObject>(ServiceLifetime lifetime = ServiceLifetime.Transient)
        where TNetService : class, INetService
        where TNetObject : class, TNetService;*/

    /// <summary>
    /// Register the type of net service and the type of net object that implements it.<br/>
    /// </summary>
    /// <typeparam name="TNetService">The type of the net service to add.</typeparam>
    /// <typeparam name="TNetObject">The type of the agent associated with the net service.</typeparam>
    void AddNetService<TNetService, TNetObject>()
        where TNetService : class, INetService
        where TNetObject : class, TNetService;

    void AddNetService<TNetService, TNetObject>(Func<IServiceProvider, TNetObject> factory)
        where TNetService : class, INetService
        where TNetObject : class, TNetService;
}

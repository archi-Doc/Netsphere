// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace Netsphere;

public interface INetsphereUnitContext
{
    /// <summary>
    /// Register the type of net service and the type of net object that implements it.<br/>
    /// </summary>
    /// <typeparam name="TNetService">The type of the net service to add.</typeparam>
    /// <typeparam name="TNetObject">The type of the agent associated with the net service.</typeparam>
    /// <param name="enableByDefault">A value indicating whether the net service is enabled by default.</param>
    void AddNetService<TNetService, TNetObject>(bool enableByDefault = true)
        where TNetService : class, INetService
        where TNetObject : class, TNetService;

    /// <summary>
    /// Registers the specified net service type and its implementation using a factory method.
    /// </summary>
    /// <typeparam name="TNetService">The type of the net service to add.</typeparam>
    /// <typeparam name="TNetObject">The type of the agent associated with the net service.</typeparam>
    /// <param name="factory">
    /// A factory function that takes an <see crdef="IServiceProvider"/> and returns an instance of <typeparamref name="TNetObject"/>.
    /// </param>
    /// <param name="enableByDefault">A value indicating whether the net service is enabled by default.</param>
    void AddNetService<TNetService, TNetObject>(Func<IServiceProvider, TNetObject> factory, bool enableByDefault = true)
        where TNetService : class, INetService
        where TNetObject : class, TNetService;
}

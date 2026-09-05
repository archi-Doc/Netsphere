// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace Netsphere;

/// <summary>
/// Registers network services and their implementations during unit configuration.
/// </summary>
public interface INetsphereUnitContext
{
    /// <summary>
    /// Registers a network service interface and its implementation type.
    /// </summary>
    /// <typeparam name="TNetService">The type of the net service to add.</typeparam>
    /// <typeparam name="TNetObject">The implementation type for the service.</typeparam>
    void AddNetService<TNetService, TNetObject>()
        where TNetService : class, INetService
        where TNetObject : class, TNetService;

    /// <summary>
    /// Registers the specified net service type and its implementation using a factory method.
    /// </summary>
    /// <typeparam name="TNetService">The type of the net service to add.</typeparam>
    /// <typeparam name="TNetObject">The implementation type for the service.</typeparam>
    /// <param name="factory">
    /// A factory function that takes an <see cref="IServiceProvider"/> and returns an instance of <typeparamref name="TNetObject"/>.
    /// </param>
    void AddNetService<TNetService, TNetObject>(Func<IServiceProvider, TNetObject> factory)
        where TNetService : class, INetService
        where TNetObject : class, TNetService;
}

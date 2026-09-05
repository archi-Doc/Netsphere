// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace Netsphere;

/// <summary>
/// Combines a connection result, connection, and service proxy with connection disposal.
/// </summary>
/// <typeparam name="TService">The network service interface.</typeparam>
/// <param name="Result">The connection result.</param>
/// <param name="Connection">The connection, if available.</param>
/// <param name="Service">The service proxy, if available.</param>
public readonly record struct ConnectionAndService<TService>(NetResult Result, Connection? Connection, TService? Service) : IDisposable
    where TService : INetService
{
    public ConnectionAndService(NetResult result)
        : this(result, default, default)
    {
    }

    public ConnectionAndService(Connection connection, TService service)
        : this(NetResult.Success, connection, service)
    {
    }

    /// <summary>
    /// Gets a value indicating whether the result is successful and both the connection and service are available.
    /// </summary>
    /// <value>True if Result is Success and both objects are non-null; otherwise, false.</value>
    [MemberNotNullWhen(true, nameof(Connection))]
    [MemberNotNullWhen(true, nameof(Service))]
    public bool IsSuccess => this.Result == NetResult.Success && this.Connection is not null && this.Service is not null;

    /// <summary>
    /// Gets a value indicating whether the result failed or the connection or service is unavailable.
    /// </summary>
    /// <value>True if Result is not Success or either object is null; otherwise, false.</value>
    [MemberNotNullWhen(false, nameof(Connection))]
    [MemberNotNullWhen(false, nameof(Service))]
    public bool IsFailure => this.Result != NetResult.Success || this.Connection is null || this.Service is null;

    /// <summary>
    /// Disposes the connection if it is available.
    /// </summary>
    public void Dispose()
    {
        this.Connection?.Dispose();
    }
}

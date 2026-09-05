// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere;

/// <summary>
/// Marks an interface as a network service contract.
/// </summary>
/// <remarks>Apply <see cref="NetServiceAttribute"/>. Methods return <see cref="Task"/> or <see cref="Task{TResult}"/>, or use a final ref <see cref="ResponseChannel{TResponse}"/> parameter with a void return type.</remarks>
public interface INetService
{
}

/// <summary>
/// Adds a signed connection-agreement update operation to a service.
/// </summary>
public interface INetServiceWithUpdateAgreement : INetService
{
    /// <summary>
    /// Updates the connection agreement using the provided certificate token.
    /// </summary>
    /// <param name="token">A certificate token containing the connection agreement to be updated.</param>
    /// <returns>
    /// A <see cref="Task{NetResult}"/> representing the asynchronous operation result of the agreement update.
    /// </returns>
    Task<NetResult> UpdateAgreement(CertificateToken<ConnectionAgreement> token);
}

/// <summary>
/// Adds an operation that enables bidirectional communication on a connection.
/// </summary>
public interface INetServiceWithConnectBidirectionally : INetService
{
    /// <summary>
    /// Establishes a bidirectional connection using the provided agreement token.<br/>
    /// Returning <see cref="NetResult.Success"/> will enable bidirectional communication between client and server.
    /// </summary>
    /// <param name="token">A certificate token containing the connection agreement, or <c>null</c> if not required.</param>
    /// <returns>A <see cref="Task{NetResult}"/> representing the result of the connection attempt.</returns>
    Task<NetResult> ConnectBidirectionally(CertificateToken<ConnectionAgreement>? token);
}

/// <summary>
/// Adds token authentication to a network service.
/// </summary>
public interface INetServiceWithAuthenticate : INetService
{
    /// <summary>
    /// Authenticates the user with the provided token.
    /// </summary>
    /// <param name="token">The authentication token.</param>
    /// <returns>The result of the authentication.</returns>
    Task<NetResult> Authenticate(AuthenticationToken token);
}

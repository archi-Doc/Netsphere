// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere;

/// <summary>
/// A base interface for net service.<br/>
/// The requirements are to add the <see cref="NetServiceInterfaceAttribute" /> and to derive from the <see cref="INetService" />.<br/>
/// The return type of the interface function must be either <see cref="Task"/> or <see cref="Task{TResponse}"/>(TResponse is Tinyhand serializable) or <see cref="Task"/> or <see cref="Task{TResult}"/>.
/// </summary>
public interface INetService
{
}

/// <summary>
/// An interface for adding functions to <see cref="INetService"/> to update the agreement.
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
/// An interface for adding functions to <see cref="INetService"/> to enable bidirectional connection.
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
/// An interface for adding functions to <see cref="INetService"/> for authentication.
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

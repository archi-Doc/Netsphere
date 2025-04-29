// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere;

/// <summary>
/// Represents a robust connection that manages client connections with optional authentication.
/// </summary>
public class RobustConnection
{
    /// <summary>
    /// Delegate for authenticating a client connection.
    /// </summary>
    /// <param name="connection">The client connection to authenticate.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is true if authentication succeeds; otherwise, false.</returns>
    public delegate Task<bool> AuthenticationDelegate(ClientConnection connection);

    /// <summary>
    /// Factory class for creating instances of <see cref="RobustConnection"/>.
    /// </summary>
    public class Factory
    {
        private readonly NetTerminal netTerminal;

        /// <summary>
        /// Initializes a new instance of the <see cref="Factory"/> class.
        /// </summary>
        /// <param name="netTerminal">The <see cref="NetTerminal"/> used for creating connections.</param>
        public Factory(NetTerminal netTerminal)
        {
            this.netTerminal = netTerminal;
        }

        /// <summary>
        /// Creates a new instance of <see cref="RobustConnection"/>.
        /// </summary>
        /// <param name="netNode">The destination node for the connection.</param>
        /// <param name="authenticationDelegate">An optional delegate for authenticating the connection.</param>
        /// <returns>A new instance of <see cref="RobustConnection"/>.</returns>
        public RobustConnection Create(NetNode netNode, AuthenticationDelegate? authenticationDelegate)
        {// authenticate = x => RobustConnection.SetAuthenticationToken(x, privateKey)
            return new(this.netTerminal, netNode, authenticationDelegate);
        }
    }

    #region FieldAndProperty

    /// <summary>
    /// Gets the <see cref="NetTerminal"/> associated with this connection.
    /// </summary>
    public NetTerminal NetTerminal { get; }

    /// <summary>
    /// Gets the destination node for this connection.
    /// </summary>
    public NetNode DestinationNode { get; }

    private readonly AuthenticationDelegate? authenticationDelegate;
    private readonly SemaphoreLock semaphore = new();
    private ClientConnection? connection;

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="RobustConnection"/> class.
    /// </summary>
    /// <param name="netTerminal">The <see cref="NetTerminal"/> used for creating connections.</param>
    /// <param name="netNode">The destination node for the connection.</param>
    /// <param name="authenticationDelegate">An optional delegate for authenticating the connection.</param>
    private RobustConnection(NetTerminal netTerminal, NetNode netNode, AuthenticationDelegate? authenticationDelegate)
    {
        this.NetTerminal = netTerminal;
        this.DestinationNode = netNode;
        this.authenticationDelegate = authenticationDelegate;
    }

    /// <summary>
    /// Sets the authentication token for a client connection.
    /// </summary>
    /// <param name="connection">The client connection to set the token for.</param>
    /// <param name="seedKey">The seed key used to create and sign the authentication token.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is true if the token was successfully set; otherwise, false.</returns>
    public static async Task<bool> SetAuthenticationToken(ClientConnection connection, SeedKey seedKey)
    {
        var context = connection.GetContext();
        var token = AuthenticationToken.CreateAndSign(seedKey, connection);
        if (context.AuthenticationTokenEquals(token.PublicKey))
        {
            return true;
        }

        var result = await connection.SetAuthenticationToken(token).ConfigureAwait(false);
        return result == NetResult.Success;
    }

    /// <summary>
    /// Gets an active client connection, creating a new one if necessary.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result is the active <see cref="ClientConnection"/>, or null if the connection could not be established.</returns>
    public async ValueTask<ClientConnection?> Get()
    {
        var currentConnection = this.connection; // Since it is outside the lock statement, the reference to connection is not safe.
        if (currentConnection?.IsActive == true)
        {
            return currentConnection;
        }

        ClientConnection? newConnection = default;
        await this.semaphore.EnterAsync().ConfigureAwait(false);
        try
        {
            if (this.connection?.IsActive == true)
            {// Safe
                return this.connection;
            }

            if (this.connection is not null)
            {
                this.connection.Dispose();
                this.connection = null;
            }

            newConnection = await this.NetTerminal.Connect(this.DestinationNode, Connection.ConnectMode.NoReuse).ConfigureAwait(false);
            if (newConnection is null)
            {// Failed to connect
                return default;
            }

            if (this.authenticationDelegate is not null)
            {// Authenticate delegate
                if (!await this.authenticationDelegate(newConnection).ConfigureAwait(false))
                {
                    return default;
                }
            }

            this.connection = newConnection;
        }
        finally
        {
            this.semaphore.Exit();
        }

        return newConnection;
    }
}

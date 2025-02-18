// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere;

public class RobustConnection
{
    public delegate Task<bool> AuthenticateDelegate(ClientConnection connection);

    // public record class Options(Func<ClientConnection, Task<bool>>? Authenticate);

    public class Factory
    {
        private readonly NetTerminal netTerminal;

        public Factory(NetTerminal netTerminal)
        {
            this.netTerminal = netTerminal;
        }

        public RobustConnection Create(NetNode netNode, AuthenticateDelegate? authenticate)
        {// authenticate = x => RobustConnection.SetAuthenticationToken(x, privateKey)
            return new(this.netTerminal, netNode, authenticate);
        }
    }

    #region FieldAndProperty

    public NetTerminal NetTerminal { get; }

    public NetNode DestinationNode { get; }

    private readonly AuthenticateDelegate? authenticate;
    private readonly SemaphoreLock semaphore = new();
    private ClientConnection? connection;

    #endregion

    private RobustConnection(NetTerminal netTerminal, NetNode netNode, AuthenticateDelegate? authenticate)
    {
        this.NetTerminal = netTerminal;
        this.DestinationNode = netNode;
        this.authenticate = authenticate;
    }

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

            if (this.authenticate is not null)
            {// Authenticate delegate
                if (!await this.authenticate(newConnection).ConfigureAwait(false))
                {
                    return default;
                }
            }

            /*else if (this.options?.PrivateKey is { } privateKey)
            {// Private key
                var context = newConnection.GetContext();
                var token = new AuthenticationToken(newConnection.Salt);
                token.Sign(privateKey);
                if (!context.AuthenticationTokenEquals(token.PublicKey))
                {
                    var result = await newConnection.SetAuthenticationToken(token).ConfigureAwait(false);
                    if (result != NetResult.Success)
                    {
                        this.options = default;
                        return default;
                    }
                }
            }*/

            this.connection = newConnection;
        }
        finally
        {
            this.semaphore.Exit();
        }

        return newConnection;
    }
}

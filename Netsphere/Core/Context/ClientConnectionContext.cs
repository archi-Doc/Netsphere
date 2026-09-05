// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere;

/// <summary>
/// Stores a client connection and its authentication token.
/// </summary>
public class ClientConnectionContext
{
    public ClientConnectionContext(ClientConnection clientConnection)
    {
        this.Connection = clientConnection;
    }

    public ClientConnection Connection { get; }

    public AuthenticationToken? AuthenticationToken { get; internal set; }

    public bool IsAuthenticationTokenSet
        => this.AuthenticationToken is not null;

    public bool AuthenticationTokenEquals(SignaturePublicKey publicKey)
    {
        if (this.AuthenticationToken is { } token &&
            token.PublicKey.Equals(publicKey))
        {
            return true;
        }

        return false;
    }
}

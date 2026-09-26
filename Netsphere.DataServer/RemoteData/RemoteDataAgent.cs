// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;
using Netsphere.Interfaces;

namespace RemoteDataServer;

[NetObject]
public class RemoteDataAgent : IRemoteData
{
    public RemoteDataAgent(RemoteDataControl control)
    {
        this.control = control;
    }

    private readonly RemoteDataControl control;

    // The agent is transient and cached per connection, so this flag authorizes only the connection that presented a valid token.
    // Without it, the default agreement still allows zero-length streams, and an unauthenticated Put would truncate files.
    private volatile bool authorized;

    async Task<NetResult> INetServiceWithUpdateAgreement.UpdateAgreement(CertificateToken<ConnectionAgreement> token)
    {
        var result = await this.control.UpdateAgreement(token).ConfigureAwait(false);
        if (result == NetResult.Success)
        {
            this.authorized = true;
        }

        return result;
    }

    Task<ReceiveStream?> IRemoteData.Get(string identifier)
    {
        if (!this.authorized)
        {
            TransmissionContext.Current.Result = NetResult.NotAuthenticated;
            return Task.FromResult<ReceiveStream?>(default);
        }

        return this.control.Get(identifier);
    }

    Task<SendStreamAndReceive<NetResult>?> IRemoteData.Put(string identifier, long maxLength)
    {
        if (!this.authorized)
        {
            TransmissionContext.Current.Result = NetResult.NotAuthenticated;
            return Task.FromResult<SendStreamAndReceive<NetResult>?>(default);
        }

        return this.control.Put(identifier, maxLength);
    }
}

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

    Task<NetResult> INetServiceWithUpdateAgreement.UpdateAgreement(CertificateToken<ConnectionAgreement> token)
        => this.control.UpdateAgreement(token);

    Task<ReceiveStream?> IRemoteData.Get(string identifier)
        => this.control.Get(identifier);

    Task<SendStreamAndReceive<NetResult>?> IRemoteData.Put(string identifier, long maxLength)
        => this.control.Put(identifier, maxLength);
}

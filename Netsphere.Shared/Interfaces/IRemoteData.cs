// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Interfaces;

[NetService]
public interface IRemoteData : INetService, INetServiceWithUpdateAgreement
{
    Task<ReceiveStream?> Get(string identifier);

    Task<SendStreamAndReceive<NetResult>?> Put(string identifier, long maxLength);
}

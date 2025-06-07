// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Lp.NetServices;

[NetServiceInterface]
public interface IRemoteBenchHost : IRemoteBenchService, INetServiceWithConnectBidirectionally, INetServiceWithUpdateAgreement
{
    NetTask<byte[]?> Pingpong(byte[] data);

    NetTask<SendStreamAndReceive<ulong>?> GetHash(long maxLength);

    NetTask Report(RemoteBenchRecord record);
}

[NetServiceInterface]
public interface IRemoteBenchService : INetService
{
    NetTask<ulong> GetHash(byte[] data);
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Lp.NetServices;

[NetService]
public interface IRemoteBenchHost : IRemoteBenchService, INetServiceWithConnectBidirectionally, INetServiceWithUpdateAgreement
{
    Task<byte[]?> Pingpong(byte[] data);

    Task<SendStreamAndReceive<ulong>?> GetHash(long maxLength);

    Task Report(RemoteBenchRecord record);
}

[NetService]
public interface IRemoteBenchService : INetService
{
    Task<ulong> GetHash(byte[] data);
}

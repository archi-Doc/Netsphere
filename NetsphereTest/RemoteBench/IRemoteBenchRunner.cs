// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Lp.NetServices;

[NetServiceInterface]
public partial interface IRemoteBenchRunner : INetService
{
    NetTask<NetResult> Start(int total, int concurrent, string? remoteNode, string? remotePrivateKey);
}

public interface INetServiceHandler
{
    void OnConnected();

    void OnDisconnected();
}

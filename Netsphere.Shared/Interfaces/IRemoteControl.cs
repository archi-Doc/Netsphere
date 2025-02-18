// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Interfaces;

[NetServiceInterface]
public interface IRemoteControl : INetService
{
    public NetTask<NetResult> Restart();
}

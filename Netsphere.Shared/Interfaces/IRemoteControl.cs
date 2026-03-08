// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Interfaces;

[NetService]
public interface IRemoteControl : INetService
{
    public Task<NetResult> Restart();
}

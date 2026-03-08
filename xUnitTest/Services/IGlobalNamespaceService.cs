// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

[NetService]
public interface IGlobalNamespaceService : INetService
{
    public Task<int> Sum(int x, int y);
}

[NetObject]
public class GlobalNamespaceServiceAgent : IGlobalNamespaceService
{
    async Task<int> IGlobalNamespaceService.Sum(int x, int y)
    {
        return x + y;
    }
}

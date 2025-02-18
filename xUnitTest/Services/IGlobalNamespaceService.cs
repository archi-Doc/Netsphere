// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

[NetServiceInterface]
public interface IGlobalNamespaceService : INetService
{
    public NetTask<int> Sum(int x, int y);
}

[NetServiceObject]
public class GlobalNamespaceServiceAgent : IGlobalNamespaceService
{
    async NetTask<int> IGlobalNamespaceService.Sum(int x, int y)
    {
        return x + y;
    }
}

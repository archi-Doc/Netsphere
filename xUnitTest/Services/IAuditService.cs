// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace xUnitTest.NetsphereTest;

[NetService]
public interface IAuditService : INetService
{
    Task<NetResultAndValue<byte[]>> Allocate(int length);
}

[NetObject]
public class AuditService : IAuditService
{
    public Task<NetResultAndValue<byte[]>> Allocate(int length) => Task.FromResult(new NetResultAndValue<byte[]>(new byte[length]));
}

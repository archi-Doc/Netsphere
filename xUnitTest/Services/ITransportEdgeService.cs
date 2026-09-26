// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Netsphere;
using ReviewContract = Netsphere.NetServiceAttribute;
using ReviewImplementation = Netsphere.NetObjectAttribute;

namespace xUnitTest.NetsphereTest;

[ReviewContract]
public interface ITransportEdgeService : INetService
{
    Task<int> @event(int value);

    Task<byte[]?> Echo(byte[] value);

    Task<BytePool.RentedMemory> EchoRent(BytePool.RentedMemory value, CancellationToken cancellationToken);

    Task<BytePool.RentedReadOnlyMemory> EchoReadOnlyRent(BytePool.RentedReadOnlyMemory value, CancellationToken cancellationToken);
}

[ReviewImplementation]
public class TransportEdgeService : ITransportEdgeService
{
    public Task<int> @event(int value) => Task.FromResult(value);

    public Task<byte[]?> Echo(byte[] value) => Task.FromResult<byte[]?>(value);

    public Task<BytePool.RentedMemory> EchoRent(BytePool.RentedMemory value, CancellationToken cancellationToken) => Task.FromResult(value);

    public Task<BytePool.RentedReadOnlyMemory> EchoReadOnlyRent(BytePool.RentedReadOnlyMemory value, CancellationToken cancellationToken) => Task.FromResult(value);
}

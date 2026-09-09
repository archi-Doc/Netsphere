// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using Arc.Collections;
using Netsphere;
using Netsphere.Crypto;
using Netsphere.Relay;
using Tinyhand;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class AllocationReviewTest
{
    private readonly NetFixture fixture;

    public AllocationReviewTest(NetFixture fixture) => this.fixture = fixture;

    [Theory]
    [InlineData(NetResult.Success)]
    [InlineData(NetResult.Timeout)]
    [InlineData(NetResult.InvalidOperation)]
    public void ResultSerializationReturnsExactlyOneByte(NetResult result)
    {
        NetHelper.SerializeNetResult(result, out var memory);
        try
        {
            Assert.Equal(1, memory.Length);
            Assert.True(NetHelper.TryDeserializeNetResult(memory.Span, out var restored));
            Assert.Equal(result, restored);
            NetHelper.DeserializeNetResult(0, memory.Span, out restored);
            Assert.Equal(result, restored);
        }
        finally
        {
            memory.Return();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ResultDeserializationRejectsInvalidLength(int length)
    {
        var bytes = new byte[length];
        Assert.False(NetHelper.TryDeserializeNetResult(bytes, out _));
        NetHelper.DeserializeNetResult((ulong)NetResult.Timeout, bytes, out var result);
        Assert.Equal(NetResult.Timeout, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1024)]
    [InlineData(100_000)]
    public void LengthPrefixedSerializationOwnsItsCompletePayload(int size)
    {
        var value = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
        Assert.True(NetHelper.TrySerializeWithLength(value, out var memory));
        try
        {
            Assert.Equal(memory.Length - sizeof(int), BitConverter.ToInt32(memory.Span));
            Assert.Equal(value, TinyhandSerializer.Deserialize<byte[]>(memory.Span.Slice(sizeof(int))));
        }
        finally
        {
            memory.Return();
        }
    }

    [Fact]
    public void FailedSerializationReturnsNoLease()
    {
        Assert.False(NetHelper.TrySerializeWithLength(new SerializationFailure(), out var memory));
        Assert.True(memory.IsEmpty);
        Assert.False(NetHelper.TrySerialize(new SerializationFailure(), out memory));
        Assert.True(memory.IsEmpty);
    }

    [Fact]
    public void FailedSerializationReturnsGrownBuffersToPool()
    {
        var value = new SerializationFailure();
        for (var i = 0; i < 8; i++)
        {
            NetHelper.TrySerializeWithLength(value, out _);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 32; i++)
        {
            NetHelper.TrySerializeWithLength(value, out _);
        }

        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        // Allow exception allocations, but reject a fresh 100 KB payload buffer per failure.
        Assert.True(allocated < 32 * 16_384, $"Failed serialization allocated {allocated} bytes.");
    }

    [Fact]
    public void SignatureVerificationRejectsWrongSaltAndTampering()
    {
        var key = SeedKey.NewSignature();
        var token = AuthenticationToken.UnsafeConstructor();
        key.SignWithSalt(token, 123);
        Assert.True(token.ValidateAndVerify(123));
        Assert.False(token.ValidateAndVerify(124));
        token.Signature[0] ^= 1;
        Assert.False(token.ValidateAndVerify(123));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void RelayEncryptionPreservesPacketSlice(int offset)
    {
        var memory = BytePool.Default.Rent(128).AsMemory(offset, 40);
        var original = Enumerable.Range(0, 40).Select(i => (byte)i).ToArray();
        original.CopyTo(memory.Span);
        var key = new byte[32];
        try
        {
            RelayHelper.Encrypt(key, ref memory);
            Assert.Equal(56, memory.Length);
            Assert.True(RelayHelper.TryDecrypt(key, ref memory, out var payload));
            Assert.Equal(original, memory.Span.ToArray());
            Assert.Equal(original.AsSpan(4).ToArray(), payload.ToArray());
        }
        finally
        {
            memory.Return();
        }
    }

    [Fact]
    public void RelayEncryptionWithoutTagCapacityLeavesBufferUnchanged()
    {
        var pool = BytePool.CreateFlat(40, 1);
        var memory = pool.Rent(40).AsMemory();
        var capacity = memory.Length;
        memory.Span.Fill(42);
        try
        {
            RelayHelper.Encrypt(new byte[32], ref memory);
            Assert.Equal(capacity, memory.Length);
            Assert.All(memory.Span.ToArray(), b => Assert.Equal(42, b));
        }
        finally
        {
            memory.Return();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(100)]
    public void ResponseBufferReusesOnlyWhenItFits(int responseLength)
    {
        var request = BytePool.Default.Rent(8).AsMemory(0, 8);
        var context = new TransmissionContext(null!, 42, 1, 0, request);
        var response = Enumerable.Range(0, responseLength).Select(i => (byte)i).ToArray();
        try
        {
            context.SetResponseMemory(response);
            Assert.Equal(response, context.RentMemory.Span.ToArray());
            if (responseLength is > 0 and <= 8)
            {
                Assert.Same(request.RentArray, context.RentMemory.RentArray);
            }
            else
            {
                Assert.Equal(0, request.RentArray!.Count);
            }
        }
        finally
        {
            context.Return();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverlappingResponsePreservesOtherOwners(bool shared)
    {
        var request = BytePool.Default.Rent(8).AsMemory(0, 8);
        new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }.CopyTo(request.Span);
        var other = shared ? request.IncrementAndShare() : default;
        var context = new TransmissionContext(null!, 42, 1, 0, request);
        try
        {
            context.SetResponseMemory(request.Memory.Slice(2, 4));
            Assert.Equal(new byte[] { 2, 3, 4, 5 }, context.RentMemory.Span.ToArray());
            if (shared)
            {
                Assert.NotSame(other.RentArray, context.RentMemory.RentArray);
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }, other.Span.ToArray());
            }
            else
            {
                Assert.Same(request.RentArray, context.RentMemory.RentArray);
            }
        }
        finally
        {
            context.Return();
            other.Return();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RelayCleanPublishesOnlyWhenMembershipChanges(bool incoming)
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var circuit = new RelayCircuit(terminal, incoming);
        var empty = circuit.RelayKey;
        circuit.Clean();
        Assert.Same(empty, circuit.RelayKey);
        Assert.False(circuit.TryGetOutermostAddress(out _));
        using var connection = await terminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var block = circuit.NewAssignRelayBlock();
        var response = new AssignRelayResponse(RelayResult.Success, 1, 2, 100, 1_000_000, null);
        Assert.Equal(RelayResult.Success, await circuit.AddRelay(block, response, connection));
        var populated = circuit.RelayKey;
        circuit.Clean();
        Assert.Same(populated, circuit.RelayKey);
        Assert.True(circuit.TryGetOutermostAddress(out var address));
        Assert.Equal((ushort)2, address.RelayId);
        Assert.Equal(RelayResult.InvalidEndpoint, circuit.CanAddRelay(3, new NetEndpoint(1, new IPEndPoint(IPAddress.Loopback, 12345))));
        connection.CloseInternal();
        circuit.Clean();
        Assert.NotSame(populated, circuit.RelayKey);
        Assert.Equal(0, circuit.NumberOfRelays);
        var cleaned = circuit.RelayKey;
        circuit.Clean();
        Assert.Same(cleaned, circuit.RelayKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(4096)]
    public async Task GeneratedByteResponsesRemainValidAfterSubsequentRequests(int length)
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        terminal.Services.EnableNetService<IReviewService>();
        using var connection = await terminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var service = connection.GetService<IReviewService>();
        var data = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
        var readOnly = await service.EchoReadOnlyMemory(data);
        var array = await service.EchoArray(data);
        var slice = await service.SliceMemory(data);
        await service.Memory();
        Assert.Equal(data, readOnly.ToArray());
        Assert.Equal(length == 0 ? null : data, array);
        Assert.Equal(data.AsSpan(length / 2).ToArray(), slice.ToArray());
    }
}

[TinyhandObject]
public partial class SerializationFailure
{
    [Key(0)]
    public byte[] Payload { get; set; } = new byte[100_000];

    [Key(1)]
    public int Failure
    {
        get => throw new InvalidOperationException("Expected serialization failure after growing the writer.");
        set { }
    }
}

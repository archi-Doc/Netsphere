// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using System.Reflection;
using Arc.Collections;
using Arc.Unit;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;
using Netsphere.Core;
using Netsphere.Packet;
using Tinyhand;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class CoreReliabilityTest
{
    private readonly NetFixture fixture;

    public CoreReliabilityTest(NetFixture fixture) => this.fixture = fixture;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void ExactGeneMultiplesRetainLastPayload(int following)
    {
        var size = FirstGeneFrame.MaxGeneLength + (following * FollowingGeneFrame.MaxGeneLength);
        var result = NetHelper.CalculateGene(size);
        Assert.Equal(following + 1, result.NumberOfGenes);
        Assert.Equal((uint)FollowingGeneFrame.MaxGeneLength, result.LastGeneSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SenderReturnsMemoryWhenItCannotSend(int mode)
    {
        var sender = this.CreateSender();
        var memory = BytePool.Default.Rent(32).AsMemory(0, 32);
        var array = memory.RentArray!;
        sender.Send_NotThreadSafe(mode == 0 ? null : new IPEndPoint(mode == 1 ? IPAddress.Loopback : IPAddress.IPv6Loopback, 1234), memory);
        sender.Stop();
        Assert.Equal(0, array.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(151)]
    [InlineData(152)]
    [InlineData(153)]
    [InlineData(600)]
    public async Task FragmentedAckRetainsEveryRange(int count)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, null);
        transmission.SetState_Receiving(2000);
        var sender = this.CreateSender();
        var ack = new AckBuffer(connection.ConnectionTerminal);
        var serials = Enumerable.Range(0, count).Select(x => x * 2).ToArray();
        var queue = new Queue<AckBuffer.ReceiveTransmissionAndAckGene>();
        queue.Enqueue(new(transmission, new Queue<int>(serials)));
        typeof(AckBuffer).GetMethod("ProcessAck", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(ack, [sender, connection, queue]);
        var packets = (Queue<NetSender.Item>)typeof(NetSender).GetField("itemsIpv4", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(sender)!;
        var received = new List<int>();
        Assert.True(packets.Count >= (count > 152 ? 2 : 1));
        while (packets.TryDequeue(out var packet))
        {
            try
            {
                var span = packet.MemoryOwner.Span;
                var salt = BitConverter.ToUInt32(span.Slice(4));
                var nonce = BitConverter.ToUInt64(span.Slice(PacketHeader.Length));
                span = span.Slice(PacketHeader.Length + ProtectedPacket.Length);
                Assert.True(connection.TryDecrypt(salt, nonce, span, span.Length, out var written));
                span = span.Slice(2, written - 2);
                while (!span.IsEmpty)
                {
                    var pairCount = BitConverter.ToUInt16(span.Slice(12));
                    span = span.Slice(14);
                    for (var i = 0; i < pairCount; i++)
                    {
                        var start = BitConverter.ToInt32(span);
                        var end = BitConverter.ToInt32(span.Slice(4));
                        received.AddRange(Enumerable.Range(start, end - start));
                        span = span.Slice(8);
                    }
                }
            }
            finally
            {
                packet.MemoryOwner.Return();
            }
        }

        Assert.Equal(serials, received);
    }

    [Fact]
    public async Task CumulativeAckWithoutRangesCompletesBlock()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new SendTransmission(connection, uint.MaxValue);
        var memory = BytePool.Default.Rent(10_000).AsMemory(0, 10_000);
        try
        {
            Assert.Equal(NetResult.Success, transmission.SendBlock(0, 0, memory, null));
            transmission.ProcessReceive_AckBlock(transmission.GeneSerialMax, transmission.GeneSerialMax, Span<byte>.Empty, 0);
            Assert.True(transmission.IsDisposed);
        }
        finally
        {
            memory.Return();
        }
    }

    [Fact]
    public async Task MaximumStreamLengthUsesBoundedWindow()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var previousLimit = connection.Agreement.MaxStreamLength;
        connection.Agreement.MaxStreamLength = -1;
        var (result, stream) = connection.SendStream(long.MaxValue);
        Assert.Equal(NetResult.Success, result);
        Assert.NotNull(stream);
        try
        {
            Assert.InRange(stream.SendTransmission.MaxReceivePosition, 1, connection.Agreement.StreamBufferGenes);
        }
        finally
        {
            stream.Dispose(true);
            connection.Agreement.MaxStreamLength = previousLimit;
        }
    }

    [Fact]
    public async Task DisposingGeneTwiceDoesNotReturnAnotherOwnersReference()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new SendTransmission(connection, uint.MaxValue);
        var gene = new SendGene(transmission);
        var memory = BytePool.Default.Rent(32).AsMemory(0, 32);
        gene.SetSend(memory.IncrementAndShare());
        try
        {
            gene.Dispose(false);
            gene.DisposeMemory();
            Assert.Equal(1, memory.RentArray!.Count);
        }
        finally
        {
            memory.Return();
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task InvalidFirstGeneCountDoesNotStartReception(int genes)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        using var transmission = connection.TryCreateReceiveTransmission(uint.MaxValue, new TaskCompletionSource<NetResponse>());
        Assert.NotNull(transmission);
        var memory = BytePool.Default.Rent(28).AsMemory(0, 28);
        try
        {
            memory.Span.Clear();
            BitConverter.TryWriteBytes(memory.Span.Slice(2), uint.MaxValue);
            BitConverter.TryWriteBytes(memory.Span.Slice(6), (ushort)DataControl.Valid);
            BitConverter.TryWriteBytes(memory.Span.Slice(12), genes);
            connection.ProcessReceive_FirstGene(default, memory);
            Assert.Equal(NetTransmissionMode.Initial, transmission.Mode);
        }
        finally
        {
            memory.Return();
        }
    }

    [Theory]
    [InlineData(1, -1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(2, -1)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(3, -1)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(8, -1)]
    [InlineData(8, 0)]
    [InlineData(8, 1)]
    public async Task BlocksRoundTripAcrossGeneBoundaries(int following, int offset)
    {
        this.fixture.NetUnit.Responders.Register(Netsphere.Responder.MemoryResponder.Instance);
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        var length = FirstGeneFrame.MaxGeneLength + (following * FollowingGeneFrame.MaxGeneLength) + offset;
        var overhead = TinyhandSerializer.Serialize(new byte[length].AsMemory()).Length - length;
        var bytes = new byte[length - overhead];
        new Random(123).NextBytes(bytes);
        Assert.Equal(length, TinyhandSerializer.Serialize(bytes.AsMemory()).Length);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await connection.SendAndReceive<Memory<byte>, Memory<byte>>(bytes.AsMemory(), 0, cancellation.Token);
        Assert.Equal(NetResult.Success, response.Result);
        Assert.True(bytes.AsSpan().SequenceEqual(response.Value.Span));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task InvalidCumulativeAckDoesNotAcknowledgeData(int successive)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new SendTransmission(connection, uint.MaxValue);
        var memory = BytePool.Default.Rent(10_000).AsMemory(0, 10_000);
        try
        {
            Assert.Equal(NetResult.Success, transmission.SendBlock(0, 0, memory, null));
            var capacity = transmission.MaxReceivePosition;
            transmission.ProcessReceive_AckBlock(int.MaxValue, successive, Span<byte>.Empty, 0);
            Assert.False(transmission.IsDisposed);
            Assert.Equal(capacity, transmission.MaxReceivePosition);
        }
        finally
        {
            memory.Return();
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 17)]
    [InlineData(2, 2000)]
    [InlineData(0, 64)]
    [InlineData(1, 64)]
    [InlineData(2, 64)]
    public void PacketRejectionReturnsMemory(int method, int length)
    {
        var packet = BytePool.Default.Rent(Math.Max(1, length)).AsMemory(0, length);
        var array = packet.RentArray!;
        var terminal = this.fixture.NetUnit.NetTerminal.PacketTerminal;
        var result = method switch
        {
            0 => terminal.SendPacketWithoutRelay(default, packet, null),
            1 => terminal.SendPacketWithRelay(default, packet, false, 0),
            _ => terminal.SendPacket(default, packet, null, 0, EndpointResolution.Ipv4, false),
        };
        Assert.NotEqual(NetResult.Success, result);
        Assert.Equal(0, array.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void MissingRelayReturnsMemory(int relay)
    {
        var packet = BytePool.Default.Rent(64).AsMemory(0, 64);
        var array = packet.RentArray!;
        var result = this.fixture.NetUnit.NetTerminal.PacketTerminal.SendPacket(Alternative.NetAddress, packet, null, relay, EndpointResolution.Ipv4, false);
        Assert.Equal(NetResult.InvalidRelay, result);
        Assert.Equal(0, array.Count);
    }

    [Fact]
    public async Task PacketWithoutRelayMatchesItsResponseId()
    {
        var terminal = this.fixture.NetUnit.NetTerminal.PacketTerminal;
        PacketTerminal.CreatePacket(0x1020304050607080, new PingPacket("packet-id"), out var packet);
        var completion = new TaskCompletionSource<NetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.Equal(NetResult.Success, terminal.SendPacketWithoutRelay(new NetEndpoint(0, new IPEndPoint(IPAddress.Loopback, Alternative.Port)), packet, completion));
        var response = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal(NetResult.Success, response.Result);
        }
        finally
        {
            response.Return();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(16)]
    public async Task InvalidCiphertextReturnsFalse(int length)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        Assert.False(connection.TryDecrypt(1, 2, new byte[length], length, out var written));
        Assert.Equal(0, written);
    }

    [Theory]
    [InlineData(0, 11)]
    [InlineData(0, 13)]
    [InlineData(1, 1)]
    [InlineData(3, 0)]
    [InlineData(-1, 12)]
    [InlineData(4, 12)]
    public async Task MalformedBlockGeneIsNotRetained(int position, int length)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, null);
        transmission.SetState_Receiving(4);
        var packet = BytePool.Default.Rent(Math.Max(1, length)).AsMemory(0, length);
        try
        {
            transmission.ProcessReceive_Gene(DataControl.Valid, position, packet);
            Assert.Equal(1, packet.RentArray!.Count);
            Assert.Equal(0, transmission.SuccessiveReceivedPosition);
        }
        finally
        {
            packet.Return();
        }
    }

    [Theory]
    [InlineData("1.2.3.4")]
    [InlineData("192.168.123.234")]
    [InlineData("255.255.255.255")]
    public void Ipv4FormattingHonorsEveryBufferBoundary(string ip)
    {
        var address = new NetAddress(IPAddress.Parse(ip), 54321);
        var expected = $"{ip}:54321";
        for (var length = 0; length <= expected.Length; length++)
        {
            var destination = new char[length];
            var result = address.TryFormat(destination, out var written);
            Assert.Equal(length == expected.Length, result);
            if (result)
            {
                Assert.Equal(expected, new string(destination, 0, written));
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public async Task BlockLimitChecksBytesEvenWithinTheSameGene(int limit)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var previousLimit = connection.Agreement.MaxBlockSize;
        connection.Agreement.MaxBlockSize = limit;
        using var transmission = new SendTransmission(connection, uint.MaxValue);
        var packet = BytePool.Default.Rent(limit + 1).AsMemory(0, limit + 1);
        try
        {
            Assert.Equal(NetResult.BlockSizeLimit, transmission.SendBlock(0, 0, packet, null));
            Assert.Equal(NetTransmissionMode.Initial, transmission.Mode);
        }
        finally
        {
            connection.Agreement.MaxBlockSize = previousLimit;
            packet.Return();
        }
    }

    [Fact]
    public async Task ZeroReceiveWindowStillCancelsStream()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new SendTransmission(connection, uint.MaxValue);
        Assert.Equal(NetResult.Success, transmission.SendStream(100));
        transmission.ProcessReceive_AckBlock(0, 0, Span<byte>.Empty, 0);
        Assert.Equal(0, transmission.MaxReceivePosition);
    }

    [Fact]
    public async Task CanceledPacketWaitRemovesPendingBuffer()
    {
        var terminal = new PacketTerminal(
            this.fixture.NetUnit.NetBase,
            this.fixture.NetUnit.NetTerminal,
            this.fixture.NetUnit.ServiceProvider.GetRequiredService<LogUnit>().RootLogService.GetLogger<PacketTerminal>());
        using var cancellation = new CancellationTokenSource();
        var task = terminal.SendAndReceive<PingPacket, PingPacketResponse>(Alternative.NetAddress, new PingPacket("cancel"), 0, cancellation.Token);
        var items = (System.Collections.IEnumerable)typeof(PacketTerminal).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(terminal)!;
        var item = Assert.Single(items.Cast<object>());
        var memory = (BytePool.RentMemory)item.GetType().GetProperty("MemoryOwner")!.GetValue(item)!;
        cancellation.Cancel();
        var response = await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(NetResult.Timeout, response.Result);
        Assert.Empty(items.Cast<object>());
        Assert.Equal(0, memory.RentArray!.Count);
    }

    [Theory]
    [InlineData(2, 5, 8)]
    [InlineData(-1, 2, 8)]
    [InlineData(2, 1, 8)]
    [InlineData(0, int.MaxValue, 8)]
    [InlineData(0, 2, 7)]
    public async Task AckRangesOnlyReleaseValidAcknowledgedGenes(int start, int end, int length)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new SendTransmission(connection, uint.MaxValue);
        var packet = BytePool.Default.Rent(10_000).AsMemory(0, 10_000);
        try
        {
            Assert.Equal(NetResult.Success, transmission.SendBlock(0, 0, packet, null));
            var genes = (SendGene.GoshujinClass)typeof(SendTransmission).GetField("genes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transmission)!;
            var ranges = new byte[8];
            BitConverter.TryWriteBytes(ranges, start);
            BitConverter.TryWriteBytes(ranges.AsSpan(4), end);
            transmission.ProcessReceive_AckBlock(transmission.GeneSerialMax, 0, ranges.AsSpan(0, length), 1);
            var valid = start == 2 && end == 5 && length == 8;
            Assert.Equal(transmission.GeneSerialMax - (valid ? 3 : 0), genes.GeneSerialListChain.Count);
            Assert.NotNull(genes.GeneSerialListChain.Get(0));
            Assert.Equal(valid, genes.GeneSerialListChain.Get(2) is null);
            transmission.ProcessReceive_AckBlock(transmission.GeneSerialMax, transmission.GeneSerialMax, Span<byte>.Empty, 0);
            Assert.True(transmission.IsDisposed);
        }
        finally
        {
            packet.Return();
        }
    }

    [Theory]
    [InlineData(0, 1, 11)]
    [InlineData(1, 1, 11)]
    [InlineData(0, 2, 13)]
    [InlineData(0, 3, 13)]
    public async Task InvalidGeneControlOrStreamHeaderDoesNotRetainMemory(int streamMode, int control, int length)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, null);
        if (streamMode == 1)
        {
            transmission.SetState_ReceivingStream(100);
        }
        else
        {
            transmission.SetState_Receiving(1);
        }

        var packet = BytePool.Default.Rent(length).AsMemory(0, length);
        try
        {
            transmission.ProcessReceive_Gene((DataControl)control, 0, packet);
            Assert.Equal(1, packet.RentArray!.Count);
            Assert.Equal(0, transmission.SuccessiveReceivedPosition);
        }
        finally
        {
            packet.Return();
        }
    }

    [Fact]
    public async Task LastGeneCannotExceedExactBlockLimit()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var previousLimit = connection.Agreement.MaxBlockSize;
        connection.Agreement.MaxBlockSize = FirstGeneFrame.MaxGeneLength + (2 * FollowingGeneFrame.MaxGeneLength) + 3;
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, null);
        transmission.SetState_Receiving(4);
        var packet = BytePool.Default.Rent(4).AsMemory(0, 4);
        try
        {
            transmission.ProcessReceive_Gene(DataControl.Valid, 3, packet);
            Assert.Equal(1, packet.RentArray!.Count);
        }
        finally
        {
            connection.Agreement.MaxBlockSize = previousLimit;
            packet.Return();
        }
    }

    private NetSender CreateSender()
        => new(this.fixture.NetUnit.NetTerminal, this.fixture.NetUnit.NetBase, this.fixture.NetUnit.ServiceProvider.GetRequiredService<LogUnit>().RootLogService.GetLogger<NetSender>());
}

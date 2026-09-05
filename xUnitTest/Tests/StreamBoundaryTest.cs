// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Collections;
using Netsphere;
using Netsphere.Core;
using Tinyhand;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class StreamBoundaryTest
{
    private readonly NetFixture fixture;

    public StreamBoundaryTest(NetFixture fixture)
        => this.fixture = fixture;

    [Theory]
    [InlineData(-1, NetResult.DeserializationFailed)]
    [InlineData(int.MinValue, NetResult.DeserializationFailed)]
    [InlineData(NetFixture.MaxBlockSize + 1, NetResult.BlockSizeLimit)]
    public async Task InvalidBlockLengthReturnsError(int length, NetResult expected)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, null);
        transmission.SetState_ReceivingStream(100);
        var packet = BytePool.Default.Rent(16).AsMemory(0, 16);
        try
        {
            packet.Span.Clear();
            BitConverter.TryWriteBytes(packet.Span.Slice(12), length);
            transmission.ProcessReceive_Gene(DataControl.Valid, 0, packet);
        }
        finally
        {
            packet.Return();
        }

        IReceiveStreamInternal stream = new ReceiveStream(transmission, 0, 100);
        var result = await stream.ReceiveBlock<int>(TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Result);
        Assert.True(transmission.IsDisposed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task SendBlockHonorsPayloadLimitWithoutCountingLengthPrefix(int excess)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        var data = new byte[NetFixture.MaxBlockSize - 5 + excess];
        Assert.Equal(NetFixture.MaxBlockSize + excess, TinyhandSerializer.Serialize(data).Length);
        var (_, stream) = connection.SendStream(NetFixture.MaxBlockSize + 100);
        Assert.NotNull(stream);
        try
        {
            var result = await stream.SendBlock(data, TestContext.Current.CancellationToken);
            Assert.Equal(excess == 0 ? NetResult.Success : NetResult.BlockSizeLimit, result);
        }
        finally
        {
            stream.Dispose(true);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public async Task BlockAndStreamRejectInvalidTotalLength(long length)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        var result = await connection.SendBlockAndStream(1, length);
        Assert.Equal(NetResult.StreamLengthLimit, result.Result);
        Assert.Null(result.Stream);
        var response = await connection.SendBlockAndStreamAndReceive<int, int>(1, length);
        Assert.Equal(NetResult.StreamLengthLimit, response.Result);
        Assert.Null(response.Stream);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2000)]
    public async Task SendRejectsDataBeyondRemainingLength(int length)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        var (_, stream) = connection.SendStream(length);
        Assert.NotNull(stream);
        try
        {
            Assert.Equal(NetResult.StreamLengthLimit, await stream.Send(new byte[length + 1], TestContext.Current.CancellationToken));
            Assert.Equal(0, stream.SentLength);
        }
        finally
        {
            stream.Dispose(true);
        }
    }

    [Fact]
    public async Task CallbackReceptionReleasesPacketReference()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        var actual = 0;
        IResponseChannelInternal channel = new ResponseChannel<int>((_, value) => actual = value);
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, channel);
        transmission.SetState_Receiving(1);
        var packet = BytePool.Default.Rent(13).AsMemory(0, 13);
        try
        {
            packet.Span.Clear();
            packet.Span[12] = 42;
            transmission.ProcessReceive_Gene(DataControl.Valid, 0, packet);
            Assert.Equal(42, actual);
            Assert.Equal(1, packet.RentArray!.Count);
        }
        finally
        {
            packet.Return();
        }
    }

    [Fact]
    public async Task ReceiveDoesNotCopyBeyondDeclaredLength()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, null);
        transmission.SetState_ReceivingStream(1);
        var packet = BytePool.Default.Rent(14).AsMemory(0, 14);
        try
        {
            packet.Span.Clear();
            packet.Span[12] = 42;
            packet.Span[13] = 99;
            transmission.ProcessReceive_Gene(DataControl.Valid, 0, packet);
        }
        finally
        {
            packet.Return();
        }

        var stream = new ReceiveStream(transmission, 0, 1);
        var buffer = new byte[10];
        var result = await stream.Receive(buffer, TestContext.Current.CancellationToken);
        Assert.Equal(NetResult.Completed, result.Result);
        Assert.Equal(1, result.Written);
        Assert.Equal(1, stream.ReceivedLength);
        Assert.Equal(42, buffer[0]);
        Assert.Equal(0, buffer[1]);
    }

    [Fact]
    public async Task StreamHelperPropagatesCompletionFailure()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var (_, stream) = connection.SendStream(100);
        Assert.NotNull(stream);
        connection.Dispose();
        using var source = new MemoryStream();
        Assert.Equal(NetResult.Closed, await NetHelper.StreamToSendStream(source, stream, TestContext.Current.CancellationToken));
    }
}

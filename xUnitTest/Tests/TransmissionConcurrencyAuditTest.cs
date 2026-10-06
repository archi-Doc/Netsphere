// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using Arc.Collections;
using Netsphere;
using Netsphere.Core;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class TransmissionConcurrencyAuditTest
{
    private readonly NetFixture fixture;

    public TransmissionConcurrencyAuditTest(NetFixture fixture) => this.fixture = fixture;

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task CanceledStreamStaysCanceledIncludingWhenItsLengthIsZero(int maxLength)
    {
        using var connection = this.CreateConnection();
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, null);
        transmission.SetState_ReceivingStream(maxLength);
        var packet = BytePool.Default.Rent(12).AsMemory(0, 12);
        try
        {
            packet.Span.Clear();
            transmission.ProcessReceive_Gene(DataControl.Cancel, 0, packet);
            var stream = new ReceiveStream(transmission, 0, maxLength);

            Assert.Equal((NetResult.Canceled, 0), await stream.Receive(new byte[1], TestContext.Current.CancellationToken));
            Assert.Equal((NetResult.Canceled, 0), await stream.Receive(new byte[1], TestContext.Current.CancellationToken));
            Assert.Equal(1, packet.Owner!.ReferenceCount);
        }
        finally
        {
            packet.Return();
        }
    }

    [Fact]
    public async Task LocalCancellationKeepsTheSameTerminalResultOnLaterReads()
    {
        using var connection = this.CreateConnection();
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, null);
        transmission.SetState_ReceivingStream(100);
        var stream = new ReceiveStream(transmission, 0, 100);

        Assert.Equal((NetResult.Canceled, 0), await stream.Receive(new byte[1], new CancellationToken(true)));
        Assert.Equal((NetResult.Canceled, 0), await stream.Receive(new byte[1], TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task TruncatedBlockPrefixIsADeserializationFailure(int prefixLength)
    {
        using var connection = this.CreateConnection();
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, null);
        transmission.SetState_ReceivingStream(prefixLength);
        var packet = BytePool.Default.Rent(12 + prefixLength).AsMemory(0, 12 + prefixLength);
        try
        {
            packet.Span.Clear();
            BitConverter.GetBytes(1).AsSpan(0, prefixLength).CopyTo(packet.Span.Slice(12));
            transmission.ProcessReceive_Gene(DataControl.Valid, 0, packet);
            IReceiveStreamInternal stream = new ReceiveStream(transmission, 0, prefixLength);

            var response = await stream.ReceiveBlock<int>(TestContext.Current.CancellationToken);
            Assert.Equal(NetResult.DeserializationFailed, response.Result);
            Assert.True(response.IsFailure);
            Assert.Equal(1, packet.Owner!.ReferenceCount);
        }
        finally
        {
            packet.Return();
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public async Task BlockCannotClaimMoreBytesThanTheStreamHasRemaining(int maxLength)
    {
        using var connection = this.CreateConnection();
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, null);
        transmission.SetState_ReceivingStream(maxLength);
        var packet = BytePool.Default.Rent(16).AsMemory(0, 16);
        try
        {
            packet.Span.Clear();
            BitConverter.TryWriteBytes(packet.Span.Slice(12), 100_000);
            transmission.ProcessReceive_Gene(DataControl.Valid, 0, packet);
            IReceiveStreamInternal stream = new ReceiveStream(transmission, 0, maxLength);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));

            var response = await stream.ReceiveBlock<byte[]>(timeout.Token);
            Assert.Equal(NetResult.DeserializationFailed, response.Result);
            Assert.True(transmission.IsDisposed);
            Assert.Equal(1, packet.Owner!.ReferenceCount);
        }
        finally
        {
            packet.Return();
        }
    }

    private ClientConnection CreateConnection()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var connection = new ClientConnection(terminal.PacketTerminal, terminal.ConnectionTerminal, 42, Alternative.NetNode, new NetEndpoint(0, new IPEndPoint(IPAddress.Loopback, 12345)));
        connection.Initialize(new ConnectionAgreement { MaxBlockSize = 100_000, MaxStreamLength = 1000 }, new byte[Connection.EmbryoSize]);
        return connection;
    }
}

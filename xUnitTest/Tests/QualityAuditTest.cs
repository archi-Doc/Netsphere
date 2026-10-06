// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Net;
using Arc.Crypto;
using Netsphere;
using Netsphere.Relay;
using Tinyhand;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class QualityAuditTest
{
    private readonly NetFixture fixture;

    public QualityAuditTest(NetFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task ClosingConnectionCompletesPendingReceive()
    {
        var netTerminal = this.fixture.NetUnit.NetTerminal;
        var connection = await netTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);

        var pending = connection.SendAndReceive<int, int>(1, 0x1234_5678_9abc_def0ul, TestContext.Current.CancellationToken); // No responder is registered, so no response arrives.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);

        var stopwatch = Stopwatch.StartNew();
        connection.Dispose();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(NetResult.Closed, result.Result);
        Assert.True(stopwatch.ElapsedMilliseconds < 2_000, $"Elapsed {stopwatch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task StreamCompletedBeforeBlockArgumentIsRejected()
    {
        this.fixture.NetUnit.Services.EnableNetService<IQualityAuditService>();
        var netTerminal = this.fixture.NetUnit.NetTerminal;
        using var connection = await netTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);

        var methodId = ((ulong)StaticNetService.GetServiceId<IQualityAuditService>() << 32) | (uint)FarmHash.Hash64($"{typeof(IQualityAuditService).FullName}.PutText(string, long)");
        var (result, stream) = connection.SendStreamAndReceive<NetResult>(0, methodId); // The stream ends before the block argument.
        Assert.Equal(NetResult.Success, result);
        Assert.NotNull(stream);

        var response = await stream.CompleteSendAndReceive(TestContext.Current.CancellationToken);
        Assert.Equal(NetResult.Success, response.Result);
        Assert.Equal(NetResult.DeserializationFailed, response.Value); // The handler must not be invoked with a missing (null) argument.
    }

    [Fact]
    public async Task NilArgumentForChannelMethodIsRejected()
    {
        this.fixture.NetUnit.Services.EnableNetService<IQualityAuditService>();
        var netTerminal = this.fixture.NetUnit.NetTerminal;
        using var connection = await netTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);

        var completion = new TaskCompletionSource<NetResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new ResponseChannel<int>((result, value) => completion.TrySetResult(result));
        connection.GetService<IQualityAuditService>().Channel(null!, ref channel);
        Assert.Equal(NetResult.DeserializationFailed, await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SourceStreamFailureIsNotReportedAsCancellation()
    {
        var netTerminal = this.fixture.NetUnit.NetTerminal;
        using var connection = await netTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);

        var (result, stream) = connection.SendStream(100);
        Assert.Equal(NetResult.Success, result);
        Assert.NotNull(stream);
        await Assert.ThrowsAsync<IOException>(() => NetHelper.StreamToSendStream(new ThrowingStream(), stream, TestContext.Current.CancellationToken));
        Assert.Equal(0, stream.SentLength);
    }

    [Fact]
    public void RelayEndpointCacheMatchesSingleFamilyReplies()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var agent = new RelayAgent(terminal.RelayControl, terminal);
        try
        {
            const ushort port = 23457;
            var destination = new NetAddress(IPAddress.Loopback, IPAddress.IPv6Loopback, port); // Senders name dual-stack destinations.
            var reply = terminal.NetStats.IsIpv6Supported ? new NetAddress(IPAddress.IPv6Loopback, port) : new NetAddress(IPAddress.Loopback, port); // A reply arrives from one family.
            var unknown = new NetAddress(IPAddress.Loopback, 23458);

            Assert.False(agent.GetEndPoint_NotThreadSafe(reply, RelayAgent.EndpointOperation.Lookup).Unrestricted);
            agent.GetEndPoint_NotThreadSafe(destination, RelayAgent.EndpointOperation.SetUnrestricted);
            Assert.True(agent.GetEndPoint_NotThreadSafe(reply, RelayAgent.EndpointOperation.Lookup).Unrestricted);
            Assert.True(agent.GetEndPoint_NotThreadSafe(new NetAddress(5, reply), RelayAgent.EndpointOperation.Lookup).Unrestricted); // The relay id of a reply differs from the one of the destination.

            // A lookup must not add unknown endpoints to the bounded cache.
            Assert.Null(agent.GetEndPoint_NotThreadSafe(unknown, RelayAgent.EndpointOperation.Lookup).EndPoint);
            Assert.NotNull(agent.GetEndPoint_NotThreadSafe(unknown, RelayAgent.EndpointOperation.None).EndPoint);
        }
        finally
        {
            agent.Stop();
        }
    }

    [Fact]
    public void SetupRelayRequiresTheExchangeOwner()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        using var client = new ClientConnection(terminal.PacketTerminal, terminal.ConnectionTerminal, 42, Alternative.NetNode, new NetEndpoint(0, new IPEndPoint(IPAddress.Loopback, 12345)));
        client.Initialize(new ConnectionAgreement(), new byte[Connection.EmbryoSize]);
        using var owner = new ServerConnection(client);
        using var stale = new ServerConnection(client);
        var agent = new RelayAgent(terminal.RelayControl, terminal);
        try
        {
            Assert.Equal(RelayResult.Success, agent.AddExchange(owner, new AssignRelayBlock(false, false), out var inner, out _, 10));
            Assert.True(agent.ProcessPingRelay(inner)!.IsOutermost);
            stale.InnerRelayId = inner; // A connection whose exchange was released keeps its id until it is cleared.

            var block = new SetupRelayBlock(new NetEndpoint(7, new IPEndPoint(IPAddress.Loopback, 23459)), new byte[AssignRelayBlock.KeyAndNonceSize]);
            Assert.True(NetHelper.TrySerialize(block, out var request));
            agent.ProcessSetupRelay(new TransmissionContext(stale, 1, 0, SetupRelayBlock.DataId, request));
            Assert.True(agent.ProcessPingRelay(inner)!.IsOutermost); // Rejected

            Assert.True(NetHelper.TrySerialize(block, out request));
            agent.ProcessSetupRelay(new TransmissionContext(owner, 2, 0, SetupRelayBlock.DataId, request));
            Assert.False(agent.ProcessPingRelay(inner)!.IsOutermost); // Accepted
        }
        finally
        {
            agent.Stop();
        }
    }

    [Fact]
    public void GossipedNodeFromTheFutureIsIgnored()
    {
        var nodeControl = new Netsphere.Stats.NodeControl(this.fixture.NetUnit.NetBase);
        var address = new NetAddress(IPAddress.Parse("8.8.8.8"), 1234);

        var future = new Netsphere.Stats.LifelineNode(address, Alternative.PublicKey) { LastConnectedMics = Mics.FastCorrected + Mics.FromHours(1) };
        nodeControl.ProcessGetActiveNodes(TinyhandSerializer.SerializeObject(new Netsphere.Stats.ActiveNode(future)));
        Assert.Equal(0, nodeControl.CountActive);

        var recent = new Netsphere.Stats.LifelineNode(address, Alternative.PublicKey) { LastConnectedMics = Mics.FastCorrected + Mics.FromSeconds(10) }; // Within the clock-skew tolerance.
        nodeControl.ProcessGetActiveNodes(TinyhandSerializer.SerializeObject(new Netsphere.Stats.ActiveNode(recent)));
        Assert.Equal(1, nodeControl.CountActive);
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Source failure");

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public class TrustSourceAuditTest
{
    [Fact]
    public void TrustSourceKeepsItsWindowAtCapacity()
    {
        var source = new TrustSource<int>(4, 2);
        for (var i = 0; i < 100; i++)
        {
            source.Add(i);
            Assert.Equal(Math.Min(i + 1, 4), source.Count);
        }

        // The window slides: the oldest values are forgotten and a repeated value becomes trusted.
        for (var i = 0; i < 4; i++)
        {
            source.Add(7);
        }

        Assert.True(source.TryGet(out var value, out var isFixed));
        Assert.Equal(7, value);
        Assert.True(isFixed);
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using Arc.Collections;
using Arc.Unit;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;
using Netsphere.Core;
using Netsphere.Packet;
using Netsphere.Relay;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class TransportEdgeReviewTest
{
    private readonly NetFixture fixture;

    public TransportEdgeReviewTest(NetFixture fixture) => this.fixture = fixture;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullSendBufferWaitsForAcknowledgementOrCancellation(bool cancel)
    {
        using var connection = this.CreateConnection();
        using var transmission = new SendTransmission(connection, 42);
        Assert.Equal(NetResult.Success, transmission.SendStream(100));
        var stream = new SendStream(transmission, 100, 0);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            Assert.Equal(2, transmission.MaxReceivePosition);
            Assert.Equal(NetResult.Success, await stream.Send(new byte[] { 1 }, timeout.Token));
            Assert.Equal(NetResult.Success, await stream.Send(new byte[] { 2 }, timeout.Token));
            transmission.ProcessReceive_KnockResponse(3);

            // The peer consumed data, but its ACK has not released the local sliding buffer.
            var pending = stream.Send(new byte[] { 3 }, timeout.Token);
            Assert.False(pending.IsCompleted);
            Assert.False(timeout.IsCancellationRequested);
            if (cancel)
            {
                timeout.Cancel();
            }
            else
            {
                transmission.ProcessReceive_AckBlock(3, 1, Span<byte>.Empty, 0);
            }

            Assert.Equal(cancel ? NetResult.Canceled : NetResult.Success, await pending.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
            Assert.Equal(cancel ? 2 : 3, stream.SentLength);
        }
        finally
        {
            stream.Dispose(true);
        }
    }

    [Fact]
    public async Task ZeroSendTimeoutCompletesWithoutPollingDelay()
    {
        using var connection = this.CreateConnection();
        using var transmission = new SendTransmission(connection, 42);
        var pending = transmission.Wait(new TaskCompletionSource<NetResult>().Task, 0, TestContext.Current.CancellationToken);
        Assert.True(pending.IsCompleted);
        Assert.Equal(NetResult.Timeout, await pending);
    }

    [Fact]
    public void EarlyLossNotificationIsRetainedWithoutCountingAResend()
    {
        using var connection = this.CreateConnection();
        connection.AddRtt(1_000_000);
        ICongestionControl control = new CubicCongestionControl(connection);
        connection.CongestionControl = control;
        using var transmission = new SendTransmission(connection, 42);
        var sender = this.CreateSender();
        var gene = new SendGene(transmission);
        gene.SetSend(PacketPool.Rent().AsMemory(0, PacketHeader.Length));
        try
        {
            PrepareSender(sender);
            Assert.True(gene.Send_NotThreadSafe(sender, 0));
            control.LossDetected(gene);
            control.Process(sender, 0, 0);
            Assert.Equal(0, connection.ResendCount);
            Assert.Equal(SendGene.State.LossDetected, gene.CurrentState);
            Assert.Single(GetField<Queue<SendGene>>(control, "genesLossDetected"));
            Assert.Equal(0u, GetField<uint>(control, "deliveryFailure"));
        }
        finally
        {
            gene.Dispose(false);
            sender.Stop();
        }
    }

    [Fact]
    public void CubicRetransmissionRespectsSenderCapacity()
    {
        using var connection = this.CreateConnection();
        ICongestionControl control = new CubicCongestionControl(connection);
        connection.CongestionControl = control;
        using var transmission = new SendTransmission(connection, 42);
        var sender = this.CreateSender();
        var gene = new SendGene(transmission);
        gene.SetSend(PacketPool.Rent().AsMemory(0, PacketHeader.Length));
        try
        {
            // No Prepare: the sender has no capacity in this round.
            Assert.True(gene.Send_NotThreadSafe(sender, 0));
            typeof(SendGene).GetField("<SentMics>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(gene, Mics.FastSystem - 10_000_000);
            control.LossDetected(gene);
            control.Process(sender, 0, 0);
            Assert.Equal(0, connection.ResendCount);
            Assert.Single(GetField<Queue<SendGene>>(control, "genesLossDetected"));
        }
        finally
        {
            gene.Dispose(false);
            sender.Stop();
        }
    }

    [Fact]
    public void EarlyRetransmissionDeadlineIsDeferredWithoutBackoff()
    {
        using var connection = this.CreateConnection();
        connection.AddRtt(1_000_000);
        ICongestionControl control = new NoCongestionControl();
        connection.CongestionControl = control;
        using var transmission = new SendTransmission(connection, 42);
        var sender = this.CreateSender();
        var gene = new SendGene(transmission);
        gene.SetSend(PacketPool.Rent().AsMemory(0, PacketHeader.Length));
        try
        {
            PrepareSender(sender);
            Assert.True(gene.Send_NotThreadSafe(sender, 0));
            var inFlight = GetField<OrderedMultiMap<long, SendGene>>(control, "genesInFlight");
            inFlight.SetNodeKey((OrderedMultiMap<long, SendGene>.Node)gene.Node!, Mics.FastSystem - 1);
            control.Process(sender, 0, 0);
            Assert.Equal(1, connection.Taichi);
            Assert.Equal(0, connection.ResendCount);
            Assert.True(inFlight.First!.Key > gene.SentMics + connection.MinimumRtt);
        }
        finally
        {
            gene.Dispose(false);
            sender.Stop();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminationReleasesIncompleteBlocksEvenWhenCanceled(bool canceled)
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var connections = new ConnectionTerminal(this.fixture.NetUnit.ServiceProvider, terminal);
        using var client = this.CreateConnection(connections);
        using var server = new ServerConnection(client);
        GetField<ClientConnection.GoshujinClass>(connections, "clientConnections").Add(client);
        GetField<ServerConnection.GoshujinClass>(connections, "serverConnections").Add(server);
        var sent = new TaskCompletionSource<NetResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sender = client.TryCreateSendTransmission();
        Assert.NotNull(sender);
        Assert.Equal(NetResult.Success, sender.SendBlock(0, 0, BytePool.RentedMemory.CreateFrom(new byte[16]), sent));
        var sentOwner = GetField<SendGene>(sender, "gene0").Packet.Owner!;
        var received = new TaskCompletionSource<NetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var receiver = server.TryCreateReceiveTransmission(42, received);
        Assert.NotNull(receiver);
        receiver.SetState_Receiving(2);
        var first = BytePool.Default.Rent(FirstGeneFrame.MaxGeneLength + 12).AsMemory(0, FirstGeneFrame.MaxGeneLength + 12);
        first.Span.Clear();
        var receivedOwner = first.Owner!;
        receiver.ProcessReceive_Gene(DataControl.Valid, 0, first);
        first.Return();

        await connections.Terminate(new CancellationToken(canceled));
        Assert.True(client.IsDisposed);
        Assert.True(server.IsDisposed);
        Assert.True(sender.IsDisposed);
        Assert.True(receiver.IsDisposed);
        Assert.Equal(NetResult.Closed, await sent.Task);
        Assert.Equal(NetResult.Closed, (await received.Task).Result);
        Assert.Equal(0, sentOwner.ReferenceCount);
        Assert.Equal(0, receivedOwner.ReferenceCount);
        Assert.Equal(0, client.SendTransmissionsCount);
        Assert.Equal(0, server.ReceiveTransmissionsCount);

        var request = new ConnectPacket(terminal.NodePublicKey, terminal.NodePublicKey.GetHashCode(), null);
        var response = new ConnectPacketResponse(terminal.NetBase.DefaultAgreement, client.DestinationEndpoint);
        Assert.False(connections.PrepareServerSide(client.DestinationEndpoint, request, response, 0));
        Assert.Empty(GetField<ServerConnection.GoshujinClass>(connections, "serverConnections"));
        Assert.Throws<ObjectDisposedException>(() => connections.PrepareBidirectionalConnection(client));
        await connections.Terminate(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ParallelBackoffUpdatesAreNotLost()
    {
        using var connection = this.CreateConnection();
        for (var round = 0; round < 32; round++)
        {
            connection.ResetTaichi();
            await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(connection.DoubleTaichi, TestContext.Current.CancellationToken)));
            Assert.Equal(1 << 20, connection.Taichi);
        }
    }

    [Fact]
    public async Task ParallelRelayAssignmentsKeepOneExchangePerConnection()
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        var terminal = this.fixture.NetUnit.NetTerminal;
        var agent = new RelayAgent(terminal.RelayControl, terminal);
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(() => agent.AddExchange(server, new(false, false), out _, out _), TestContext.Current.CancellationToken)));
            Assert.Single(results, x => x == RelayResult.Success);
            Assert.Equal(15, results.Count(x => x == RelayResult.DuplicateEndpoint));
            Assert.Equal(1, agent.NumberOfExchanges);
        }
        finally
        {
            agent.Stop();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    public void RelayRoundTripPreservesPacketSlice(int offset)
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        var terminal = this.fixture.NetUnit.NetTerminal;
        var agent = new RelayAgent(terminal.RelayControl, terminal);
        var block = new AssignRelayBlock(false, false);
        Assert.Equal(RelayResult.Success, agent.AddExchange(server, block, out var inner, out var outer));
        agent.AddRelayPoint(inner, 10);
        var nodes = new RelayNode.GoshujinClass();
        nodes.Add(new RelayNode(block, new(RelayResult.Success, inner, outer, 10, 1000, null), client));
        var key = new RelayKey(nodes);
        var remote = new NetEndpoint(0, new IPEndPoint(IPAddress.Loopback, 23456));
        agent.GetEndPoint_NotThreadSafe(new(remote), RelayAgent.EndpointOperation.SetUnrestricted);
        PacketTerminal.CreatePacket(42, new PingPacket("slice"), out var content);
        var owner = PacketPool.Rent();
        owner.AsSpan().Fill(0xa5);
        var source = owner.AsMemory(offset, content.Length);
        content.Span.CopyTo(source.Span);
        var expected = content.Span.ToArray();
        content.Return();
        try
        {
            Assert.False(agent.ProcessRelay(remote, outer, source, out _));
            var queue = GetField<ConcurrentQueue<NetSender.Item>>(agent, "sendItems");
            Assert.True(queue.TryDequeue(out var item));
            var response = item.MemoryOwner;
            try
            {
                Assert.True(key.TryDecrypt(key.FirstEndpoint, ref response, out var original, out var hops));
                Assert.Equal(new NetAddress(remote), original);
                Assert.Equal(1, hops);
                Assert.Equal(expected, response.Span.ToArray());
                Assert.All(owner.AsSpan(0, offset).ToArray(), b => Assert.Equal((byte)0xa5, b));
            }
            finally
            {
                item.MemoryOwner.Return();
            }
        }
        finally
        {
            source.Return();
            agent.Stop();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RelayRejectsPacketsWithoutSpaceForAuthenticationTag(bool insufficientCapacity)
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        var terminal = this.fixture.NetUnit.NetTerminal;
        var agent = new RelayAgent(terminal.RelayControl, terminal);
        agent.AddExchange(server, new(false, false), out var inner, out var outer);
        agent.AddRelayPoint(inner, 10);
        var remote = new NetEndpoint(1, new IPEndPoint(IPAddress.Loopback, 23456));
        agent.GetEndPoint_NotThreadSafe(new(remote), RelayAgent.EndpointOperation.SetUnrestricted);
        var owner = PacketPool.Rent();
        var packet = insufficientCapacity ? owner.AsMemory(owner.Array.Length - 64, 64) : owner.AsMemory(0, NetConstants.MaxPacketLength);
        packet.Span.Clear();
        BitConverter.TryWriteBytes(packet.Span, (ushort)1);
        try
        {
            Assert.False(agent.ProcessRelay(remote, outer, packet, out _));
            Assert.Empty(GetField<ConcurrentQueue<NetSender.Item>>(agent, "sendItems"));
            Assert.Equal(10, agent.ProcessPingRelay(inner)!.RelayPoint);
        }
        finally
        {
            packet.Return();
            agent.Stop();
        }
    }

    [Fact]
    public async Task GeneratedAliasedServiceHandlesKeywordsEmptyArraysAndCancelablePooledMemory()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        terminal.Services.EnableNetService<ITransportEdgeService>();
        using var connection = await terminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var proxy = connection.GetService<ITransportEdgeService>();
        Assert.Equal(42, await proxy.@event(42));
        var empty = await proxy.Echo([]);
        Assert.NotNull(empty);
        Assert.Empty(empty);

        var request = BytePool.Default.Rent(16).AsMemory(0, 16);
        request.Span.Fill(0x3d);
        try
        {
            var response = await proxy.EchoRent(request, TestContext.Current.CancellationToken);
            try
            {
                Assert.Equal(request.Span.ToArray(), response.Span.ToArray());
            }
            finally
            {
                response.Return();
            }

            var readOnlyResponse = await proxy.EchoReadOnlyRent(request.ReadOnly, TestContext.Current.CancellationToken);
            try
            {
                Assert.Equal(request.Span.ToArray(), readOnlyResponse.Span.ToArray());
            }
            finally
            {
                readOnlyResponse.Return();
            }

            Assert.Equal(1, request.Owner!.ReferenceCount);
        }
        finally
        {
            request.Return();
        }
    }

    private static T GetField<T>(object instance, string name)
        => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static void PrepareSender(NetSender sender)
        => typeof(NetSender).GetMethod("Prepare", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(sender, null);

    private ClientConnection CreateConnection(ConnectionTerminal? connections = null)
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var connection = new ClientConnection(terminal.PacketTerminal, connections ?? terminal.ConnectionTerminal, 12345, Alternative.NetNode, new(0, new IPEndPoint(IPAddress.Loopback, 12345)));
        connection.Initialize(new ConnectionAgreement { MaxStreamLength = 1000, TransmissionTimeout = TimeSpan.FromSeconds(30) }, new byte[Connection.EmbryoSize]);
        return connection;
    }

    private NetSender CreateSender()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var logger = this.fixture.NetUnit.ServiceProvider.GetRequiredService<LogUnit>().RootLogService.GetLogger<NetSender>();
        return new(terminal, terminal.NetBase, logger);
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Arc.Collections;
using Arc.Crypto;
using Arc.Unit;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;
using Netsphere.Core;
using Netsphere.Crypto;
using Netsphere.Packet;
using Netsphere.Relay;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class TransportDeepReviewTest
{
    private readonly NetFixture fixture;

    public TransportDeepReviewTest(NetFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task SocketCanReceiveAfterRepeatedStopAndRestart()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var socket = new NetSocket(terminal);
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            for (var i = 0; i < 3; i++)
            {
                Assert.True(socket.Start(terminal.ExecutionGroup, 0, false, out var port));
                var bytes = new byte[] { 1, 2, (byte)i };
                await client.SendAsync(bytes, new IPEndPoint(IPAddress.Loopback, port), timeout.Token);
                var response = await client.ReceiveAsync(timeout.Token);
                Assert.Equal(bytes, response.Buffer);
                socket.Stop();
                socket.Stop();
            }
        }
        finally
        {
            socket.Stop();
        }
    }

    [Fact]
    public async Task ConcurrentSocketStartsPublishOnlyOneReceiver()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var socket = new NetSocket(terminal);
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => socket.Start(terminal.ExecutionGroup, 0, false, out _), TestContext.Current.CancellationToken)));
            Assert.Single(results, x => x);
        }
        finally
        {
            socket.Stop();
        }
    }

    [Fact]
    public async Task HeaderSizedPingIsHandledAsProtocolPacket()
    {
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var packet = new byte[PacketHeader.Length];
        BitConverter.TryWriteBytes(packet.AsSpan(8), (ushort)PacketType.Ping);
        BitConverter.TryWriteBytes(packet.AsSpan(10), 42ul);
        BitConverter.TryWriteBytes(packet.AsSpan(4), (uint)XxHash3.Hash64(packet.AsSpan(8)));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await client.SendAsync(packet, new IPEndPoint(IPAddress.Loopback, this.fixture.NetUnit.NetTerminal.Port), timeout.Token);
        var response = await client.ReceiveAsync(timeout.Token);
        Assert.Equal((ushort)PacketType.PingResponse, BitConverter.ToUInt16(response.Buffer, 8));
        Assert.Equal(42ul, BitConverter.ToUInt64(response.Buffer, 10));
    }

    [Fact]
    public async Task FailedReceiveTransfersItsLeaseExactlyOnce()
    {
        using var connection = this.CreateConnection(12345);
        using var receiver = new ReceiveTransmission(connection, 42, null, null);
        var memory = BytePool.Default.Rent(64).AsMemory(0, 64);
        var owner = memory.RentArray!;
        try
        {
            var response = await receiver.Wait(Task.FromResult(new NetResponse(NetResult.InvalidData, 0, 0, memory)), 1000, TestContext.Current.CancellationToken);
            Assert.Equal(NetResult.InvalidData, response.Result);
            Assert.Equal(1, owner.Count);
        }
        finally
        {
            if (owner.Count > 0)
            {
                memory.Return();
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZeroTimeoutDoesNotWaitForPollingInterval(bool packet)
    {
        using var connection = this.CreateConnection(12345);
        using var receiver = new ReceiveTransmission(connection, 42, null, null);
        var completion = new TaskCompletionSource<NetResponse>();
        var task = packet ? this.fixture.NetUnit.NetTerminal.Wait(completion.Task, TimeSpan.Zero, TestContext.Current.CancellationToken) : receiver.Wait(completion.Task, 0, TestContext.Current.CancellationToken);
        Assert.True(task.IsCompleted);
        Assert.Equal(NetResult.Timeout, (await task).Result);
        completion.SetResult(new(NetResult.Closed));
    }

    [Theory]
    [InlineData(RelayResult.ConnectionFailure, 1, 2)]
    [InlineData(RelayResult.Success, 0, 2)]
    [InlineData(RelayResult.Success, 1, 0)]
    [InlineData(RelayResult.Success, 1, 1)]
    public async Task InvalidRelayAssignmentDoesNotChangeCircuit(RelayResult result, ushort inner, ushort outer)
    {
        using var connection = this.CreateConnection(12345);
        var circuit = new RelayCircuit(this.fixture.NetUnit.NetTerminal, false);
        var key = circuit.RelayKey;
        var response = new AssignRelayResponse(result, inner, outer, 100, 1_000_000, null);
        Assert.NotEqual(RelayResult.Success, await circuit.AddRelay(circuit.NewAssignRelayBlock(), response, connection));
        Assert.Same(key, circuit.RelayKey);
        Assert.Equal(0, connection.MinimumNumberOfRelays);
    }

    [Fact]
    public async Task FailedRelaySetupDoesNotPublishNewHop()
    {
        using var first = this.CreateConnection(12345);
        using var second = this.CreateConnection(12346);
        var circuit = new RelayCircuit(this.fixture.NetUnit.NetTerminal, false);
        Assert.Equal(RelayResult.Success, await circuit.AddRelay(circuit.NewAssignRelayBlock(), new(RelayResult.Success, 1, 2, 100, 1_000_000, null), first));
        var key = circuit.RelayKey;
        Assert.Equal(RelayResult.ConnectionFailure, await circuit.AddRelay(circuit.NewAssignRelayBlock(), new(RelayResult.Success, 3, 4, 100, 1_000_000, null), second));
        Assert.Same(key, circuit.RelayKey);
        Assert.Equal(1, circuit.NumberOfRelays);
        Assert.Equal(0, second.MinimumNumberOfRelays);
    }

    [Fact]
    public async Task RelayKeysAreIndependentOfAssignmentBuffer()
    {
        using var connection = this.CreateConnection(12345);
        var circuit = new RelayCircuit(this.fixture.NetUnit.NetTerminal, false);
        var block = circuit.NewAssignRelayBlock();
        var expected = block.InnerKeyAndNonce.ToArray();
        Assert.Equal(RelayResult.Success, await circuit.AddRelay(block, new(RelayResult.Success, 1, 2, 100, 1_000_000, null), connection));
        block.InnerKeyAndNonce.AsSpan().Clear();
        Assert.Equal(expected, circuit.RelayKey.FirstKeyAndNonce);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RelayCleanupRemovesHopsBeyondBrokenLink(int broken)
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        using var first = await terminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        using var second = await terminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        using var third = await terminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);
        var connections = new[] { first, second, third };
        var circuit = new RelayCircuit(this.fixture.NetUnit.NetTerminal, false);
        var nodes = (RelayNode.GoshujinClass)typeof(RelayCircuit).GetField("relayNodes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(circuit)!;
        for (var i = 0; i < connections.Length; i++)
        {
            nodes.Add(new(circuit.NewAssignRelayBlock(), new(RelayResult.Success, (ushort)((i * 2) + 1), (ushort)((i * 2) + 2), 100, 1_000_000, null), connections[i]));
        }

        connections[broken].CloseInternal();
        circuit.Clean();
        Assert.Equal(broken, circuit.NumberOfRelays);
        Assert.False(third.IsOpen);
        Assert.Equal(broken > 0, first.IsOpen);
    }

    [Fact]
    public async Task RejectedAuthenticationIsNotCachedAsSuccessful()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var token = AuthenticationToken.UnsafeConstructor();
        connection.SignWithSalt(token, SeedKey.NewSignature());
        token.Signature[0] ^= 1;
        Assert.Equal(NetResult.InvalidData, await connection.SetAuthenticationToken(token));
        Assert.False(connection.GetContext().IsAuthenticationTokenSet);
        Assert.Equal(NetResult.InvalidData, await connection.SetAuthenticationToken(token));
    }

    [Fact]
    public async Task ConcurrentAuthenticationCannotReplaceAnotherIdentity()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var first = AuthenticationToken.UnsafeConstructor();
        var second = AuthenticationToken.UnsafeConstructor();
        connection.SignWithSalt(first, SeedKey.NewSignature());
        connection.SignWithSalt(second, SeedKey.NewSignature());
        var results = await Task.WhenAll(connection.SetAuthenticationToken(first), connection.SetAuthenticationToken(second));
        Assert.Single(results, x => x == NetResult.Success);
        Assert.Single(results, x => x == NetResult.InvalidOperation);
        var winner = results[0] == NetResult.Success ? first : second;
        var loser = results[0] == NetResult.Success ? second : first;
        Assert.True(connection.GetContext().AuthenticationTokenEquals(winner.PublicKey));
        Assert.Equal(NetResult.InvalidOperation, await connection.SetAuthenticationToken(loser));
        Assert.True(connection.GetContext().AuthenticationTokenEquals(winner.PublicKey));
    }

    [Fact]
    public async Task GeneratedFiltersResolveDependenciesAndHonorClassOrder()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        terminal.Services.EnableNetService<ITransportReviewService>();
        terminal.Services.EnableNetService<IClassOrderedReviewService>();
        using var connection = await terminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var service = connection.GetService<ITransportReviewService>();
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => service.Echo(i)));
        Assert.Equal(Enumerable.Range(0, 16), results);
        var completed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new ResponseChannel<int>((result, value) => completed.TrySetResult(result == NetResult.Success ? value : -1));
        service.Channel(ref channel);
        Assert.Equal(42, await completed.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.Equal(11, await connection.GetService<IClassOrderedReviewService>().Echo(5));
        Assert.Equal(83, await connection.GetService<IClassOrderedReviewService>().WithReplacement(5));
    }

    [Fact]
    public async Task PacketStopReturnsPendingBuffersAndRejectsLaterEnqueues()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var logger = this.fixture.NetUnit.ServiceProvider.GetRequiredService<LogUnit>().RootLogService.GetLogger<PacketTerminal>();
        var packets = new PacketTerminal(this.fixture.NetUnit.NetBase, terminal, logger);
        var endpoint = new NetEndpoint(0, new IPEndPoint(IPAddress.Loopback, 12345));
        var completion = new TaskCompletionSource<NetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        PacketTerminal.CreatePacket(42, new PingPacket("pending"), out var packet);
        var owner = packet.RentArray!;
        Assert.Equal(NetResult.Success, packets.SendPacketWithoutRelay(endpoint, packet, completion));
        packets.Stop();
        Assert.Equal(0, owner.Count);
        Assert.Equal(NetResult.Closed, (await completion.Task).Result);
        var owners = new System.Collections.Concurrent.ConcurrentBag<BytePool.RentArray>();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(
            () =>
            {
                for (var i = 0; i < 32; i++)
                {
                    PacketTerminal.CreatePacket((ulong)i, new PingPacket("late"), out var late);
                    owners.Add(late.RentArray!);
                    Assert.Equal(NetResult.Closed, packets.SendPacketWithoutRelay(endpoint, late, null));
                    packets.Stop();
                }
            },
            TestContext.Current.CancellationToken)));
        Assert.All(owners, x => Assert.Equal(0, x.Count));
    }

    [Fact]
    public void RelayStopDrainsForwardedPacketsAndRejectsNewExchanges()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var agent = new RelayAgent(terminal.RelayControl, terminal);
        using var client = this.CreateConnection(12345);
        using var server = new ServerConnection(client);
        Assert.Equal(RelayResult.Success, agent.AddExchange(server, new(false, false), out var inner, out _));
        var queue = (System.Collections.Concurrent.ConcurrentQueue<NetSender.Item>)typeof(RelayAgent).GetField("sendItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(agent)!;
        var packet = PacketPool.Rent().AsMemory(0, 40);
        var owner = packet.RentArray!;
        queue.Enqueue(new(new IPEndPoint(IPAddress.Loopback, 12345), packet));
        agent.Stop();
        Assert.Equal(0, owner.Count);
        Assert.Equal(0, agent.NumberOfExchanges);
        Assert.Empty(queue);
        Assert.Equal(RelayResult.ConnectionFailure, agent.AddExchange(server, new(false, false), out _, out _));
        Assert.False(agent.ProcessRelay(server.DestinationEndpoint, inner, default, out _));
        agent.Stop();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DispatchFailureReleasesRequestOrStream(bool stream)
    {
        using var client = this.CreateConnection(12345);
        using var server = new ServerConnection(client);
        var serverContext = server.GetContext();
        serverContext.EnableNetService<IFactoryFailureReviewService>();
        var serviceId = (ulong)StaticNetService.GetServiceId<IFactoryFailureReviewService>() << 32;
        if (stream)
        {
            server.Agreement.MaxStreamLength = 100;
            using var receiver = new ReceiveTransmission(server, 42, null, null);
            receiver.SetState_ReceivingStream(100);
            serverContext.InvokeStream(receiver, serviceId, 100);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            while (!receiver.IsDisposed)
            {
                await Task.Delay(1, timeout.Token);
            }
        }
        else
        {
            NetHelper.TrySerialize(1, out var request);
            var context = new TransmissionContext(server, 42, 1, serviceId, request);
            await serverContext.InvokeRPC(context);
            Assert.True(context.RentMemory.IsEmpty);
            Assert.True(context.IsSent);
        }
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(7u)]
    public void InvalidOrThrowingResponderReleasesRequest(uint kind)
    {
        using var client = this.CreateConnection(12345);
        using var server = new ServerConnection(client);
        var responder = new ThrowingReviewResponder();
        this.fixture.NetUnit.NetTerminal.Responders.Register(responder);
        NetHelper.TrySerialize(1, out var memory);
        var context = new TransmissionContext(server, 42, kind, responder.DataId, memory);
        server.GetContext().InvokeSync(context);
        Assert.True(context.RentMemory.IsEmpty);
    }

    [Fact]
    public void GeneratedMethodsPreserveTheirOwnReturnNullability()
    {
        using var client = this.CreateConnection(12345);
        var proxy = client.GetService<ITransportReviewService>();
        var info = new NullabilityInfoContext();
        var optional = proxy.GetType().GetMethod(nameof(ITransportReviewService.OptionalText))!;
        var required = proxy.GetType().GetMethod(nameof(ITransportReviewService.RequiredText))!;
        Assert.Equal(NullabilityState.Nullable, info.Create(optional.ReturnParameter).GenericTypeArguments[0].ReadState);
        Assert.Equal(NullabilityState.NotNull, info.Create(required.ReturnParameter).GenericTypeArguments[0].ReadState);
    }

    private ClientConnection CreateConnection(ushort port)
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var connection = new ClientConnection(terminal.PacketTerminal, terminal.ConnectionTerminal, port, Alternative.NetNode, new NetEndpoint(0, new IPEndPoint(IPAddress.Loopback, port)));
        connection.Initialize(new ConnectionAgreement { TransmissionTimeout = TimeSpan.FromMilliseconds(20) }, new byte[Connection.EmbryoSize]);
        return connection;
    }
}

public class ThrowingReviewResponder : INetResponder
{
    public ulong DataId => 0xe5d7000000000001;

    public void Respond(TransmissionContext transmissionContext) => throw new InvalidOperationException("Expected responder failure.");
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections;
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
public class ProtocolReviewTest
{
    private readonly NetFixture fixture;

    public ProtocolReviewTest(NetFixture fixture) => this.fixture = fixture;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateFirstGeneDoesNotAcknowledgeIncompleteBurst(bool server)
    {
        using var client = this.CreateConnection();
        using var serverConnection = new ServerConnection(client);
        Connection connection = server ? serverConnection : client;
        using var receiver = server ? null : connection.TryCreateReceiveTransmission(42, null);
        var id = receiver?.TransmissionId ?? 42;
        var packet = BytePool.Default.Rent(FirstGeneFrame.LengthExcludingFrameType + FirstGeneFrame.MaxGeneLength).AsMemory(0, FirstGeneFrame.LengthExcludingFrameType + FirstGeneFrame.MaxGeneLength);
        try
        {
            packet.Span.Clear();
            BitConverter.TryWriteBytes(packet.Span.Slice(2), id);
            BitConverter.TryWriteBytes(packet.Span.Slice(6), (ushort)DataControl.Valid);
            BitConverter.TryWriteBytes(packet.Span.Slice(12), 2);
            connection.ProcessReceive_FirstGene(default, packet);
            connection.ProcessReceive_FirstGene(default, packet);
            Assert.Null(connection.AckQueue);
            Assert.Equal(NetTransmissionMode.Burst, this.GetReceivers(connection).TransmissionIdChain.FindFirst(id)!.Mode);
        }
        finally
        {
            connection.CloseAllTransmission();
            packet.Return();
        }
    }

    [Fact]
    public void DuplicateAckDoesNotAccumulateInBurstSentinel()
    {
        using var connection = this.CreateConnection();
        using var receiver = new ReceiveTransmission(connection, 42, null, null);
        var buffer = new AckBuffer(connection.ConnectionTerminal);
        buffer.AckBurst(connection, receiver);
        for (var i = 0; i < 100; i++)
        {
            buffer.AckBlock(connection, receiver, 0);
        }

        Assert.Empty(receiver.AckGene!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitializationCannotResurrectDisposedReceiver(bool stream)
    {
        using var connection = this.CreateConnection();
        for (var i = 0; i < 100; i++)
        {
            using var receiver = new ReceiveTransmission(connection, 42, null, null);
            await Task.WhenAll(
                Task.Run(() => receiver.Dispose(), TestContext.Current.CancellationToken),
                Task.Run(() => Initialize(receiver), TestContext.Current.CancellationToken));
            Initialize(receiver);
            Assert.True(receiver.IsDisposed);
            Assert.Equal(0, receiver.MaxReceivePosition);
        }

        void Initialize(ReceiveTransmission receiver)
        {
            if (stream)
            {
                receiver.SetState_ReceivingStream(100);
            }
            else
            {
                receiver.SetState_Receiving(4);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PacketResponseMustMatchTypeAndEndpoint(bool wrongEndpoint)
    {
        var terminal = this.CreatePacketTerminal();
        var endpoint = new NetEndpoint(0, new IPEndPoint(IPAddress.Loopback, 12345));
        var completion = new TaskCompletionSource<NetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        PacketTerminal.CreatePacket(42, new PingPacket("test"), out var request);
        terminal.SendPacketWithoutRelay(endpoint, request, completion);
        PacketTerminal.CreatePacket(42, new PingPacketResponse(endpoint, "test", 0), out var response);
        try
        {
            var source = wrongEndpoint ? new NetEndpoint(0, new IPEndPoint(IPAddress.Loopback, 12346)) : endpoint;
            terminal.ProcessReceive(source, 0, false, 0, (ushort)(wrongEndpoint ? PacketType.PingResponse : PacketType.ConnectResponse), response, Mics.FastSystem);
            Assert.False(completion.Task.IsCompleted);
            terminal.ProcessReceive(endpoint, 0, false, 0, (ushort)PacketType.PingResponse, response, Mics.FastSystem);
            (await completion.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken)).Return();
            Assert.Equal(1, response.RentArray!.Count);
        }
        finally
        {
            response.Return();
            this.ClearPackets(terminal);
        }
    }

    [Fact]
    public async Task PacketTimeoutRemovesPendingMemory()
    {
        var terminal = this.CreatePacketTerminal();
        try
        {
            var pending = terminal.SendAndReceive<PingPacket, PingPacketResponse>(Alternative.NetAddress, new PingPacket("timeout"), cancellationToken: TestContext.Current.CancellationToken);
            var item = Assert.Single(this.GetPackets(terminal));
            var memory = (BytePool.RentMemory)item.GetType().GetProperty("MemoryOwner")!.GetValue(item)!;
            Assert.Equal(NetResult.Timeout, (await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Result);
            Assert.Empty(this.GetPackets(terminal));
            Assert.Equal(0, memory.RentArray!.Count);
        }
        finally
        {
            this.ClearPackets(terminal);
        }
    }

    [Fact]
    public void PacketRetryDoesNotMutateAlreadyQueuedBytes()
    {
        var terminal = this.CreatePacketTerminal();
        terminal.RetransmissionTimeoutMics = long.MaxValue;
        var sender = this.CreateSender();
        typeof(NetSender).GetMethod("Prepare", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(sender, null);
        PacketTerminal.CreatePacket(42, new PingPacket("retry"), out var request);
        var original = request.Memory.ToArray();
        terminal.SendPacketWithoutRelay(new NetEndpoint(0, new IPEndPoint(IPAddress.Loopback, 12345)), request, new(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            terminal.ProcessSend(sender);
            terminal.RetransmissionTimeoutMics = -1;
            terminal.MaxResendCount = 1;
            terminal.ProcessSend(sender);
            Assert.Equal(original, request.Memory.ToArray());
        }
        finally
        {
            sender.Stop();
            this.ClearPackets(terminal);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RelayRejectsWrongSourceExhaustedPointsAndTampering(int rejection)
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        var terminal = this.fixture.NetUnit.NetTerminal;
        var agent = new RelayAgent(terminal.RelayControl, terminal);
        var block = new AssignRelayBlock(false, false);
        Assert.Equal(RelayResult.Success, agent.AddExchange(server, block, out var inner, out var outer));
        if (rejection != 1)
        {
            agent.AddRelayPoint(inner, 10);
        }

        var nodes = new RelayNode.GoshujinClass();
        nodes.Add(new RelayNode(block, new AssignRelayResponse(RelayResult.Success, inner, outer, 10, 1000, null), client));
        var key = new RelayKey(nodes);
        PacketTerminal.CreatePacket(42, new PingPacket("relay"), out var content);
        Assert.True(key.TryEncrypt(-1, NetAddress.Relay, content.Span, out var encrypted, out _));
        content.Return();
        try
        {
            var endpoint = rejection == 0 ? new NetEndpoint(0, new IPEndPoint(IPAddress.Loopback, 12346)) : server.DestinationEndpoint;
            if (rejection == 2)
            {
                encrypted.Span[^1] ^= 1;
            }

            Assert.False(agent.ProcessRelay(endpoint, inner, encrypted, out var decrypted));
            Assert.False(decrypted.IsRent);
        }
        finally
        {
            encrypted.Return();
        }
    }

    [Fact]
    public void RelayAcceptsValidAuthenticatedPacket()
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        var terminal = this.fixture.NetUnit.NetTerminal;
        var agent = new RelayAgent(terminal.RelayControl, terminal);
        var block = new AssignRelayBlock(false, false);
        agent.AddExchange(server, block, out var inner, out var outer);
        agent.AddRelayPoint(inner, 10);
        var nodes = new RelayNode.GoshujinClass();
        nodes.Add(new RelayNode(block, new AssignRelayResponse(RelayResult.Success, inner, outer, 10, 1000, null), client));
        var key = new RelayKey(nodes);
        PacketTerminal.CreatePacket(42, new PingPacket("relay"), out var content);
        Assert.True(key.TryEncrypt(-1, NetAddress.Relay, content.Span, out var encrypted, out _));
        try
        {
            Assert.True(agent.ProcessRelay(server.DestinationEndpoint, inner, encrypted, out var decrypted));
            Assert.Equal(content.Span.Slice(4).ToArray(), decrypted.Span.Slice(4).ToArray());
        }
        finally
        {
            content.Return();
            encrypted.Return();
        }
    }

    [Fact]
    public void RelayRejectsInvalidOuterAuthenticationWithoutForwarding()
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        var terminal = this.fixture.NetUnit.NetTerminal;
        var agent = new RelayAgent(terminal.RelayControl, terminal);
        var block = new AssignRelayBlock(false, false);
        agent.AddExchange(server, block, out var inner, out var outer);
        agent.AddRelayPoint(inner, 10);
        var exchanges = (RelayExchange.GoshujinClass)typeof(RelayAgent).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(agent)!;
        var exchange = exchanges.RelayIdChain.FindFirst(inner)!;
        exchange.OuterEndpoint = client.DestinationEndpoint;
        exchange.OuterKeyAndNonce = new byte[32];
        var packet = PacketPool.Rent().AsMemory(0, 64);
        try
        {
            packet.Span.Clear();
            BitConverter.TryWriteBytes(packet.Span, (ushort)1);
            Assert.False(agent.ProcessRelay(exchange.OuterEndpoint, outer, packet, out _));
            var queued = (IEnumerable)typeof(RelayAgent).GetField("sendItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(agent)!;
            Assert.Empty(queued.Cast<object>());
        }
        finally
        {
            packet.Return();
        }
    }

    [Fact]
    public void RelayPointsSaturateWithoutOverflow()
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        var terminal = this.fixture.NetUnit.NetTerminal;
        var agent = new RelayAgent(terminal.RelayControl, terminal);
        agent.AddExchange(server, new AssignRelayBlock(false, false), out var inner, out _);
        Assert.Equal(terminal.RelayControl.DefaultMaxRelayPoint, agent.AddRelayPoint(inner, long.MaxValue));
        Assert.Equal(0, agent.AddRelayPoint(inner, long.MaxValue));
        Assert.Equal(-terminal.RelayControl.DefaultMaxRelayPoint, agent.AddRelayPoint(inner, long.MinValue));
        Assert.Equal(0, agent.AddRelayPoint(inner, long.MinValue));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(17, 1)]
    [InlineData(1350, 1)]
    [InlineData(18, int.MinValue)]
    public void InvalidRelayEncryptionFailsWithoutThrowing(int length, int relay)
    {
        Assert.False(new RelayKey().TryEncrypt(relay, default, new byte[length], out var memory, out _));
        Assert.False(memory.IsRent);
    }

    [Fact]
    public async Task GeneratedMemoryResponseReleasesPoolAndOwnsItsBytes()
    {
        using var connection = this.CreateConnection();
        var task = connection.GetService<IReviewService>().Memory();
        var packet = this.DeliverResponse(connection, [1, 2, 3]);
        try
        {
            var result = await task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            Assert.Equal(1, packet.RentArray!.Count);
            packet.Span.Clear();
            Assert.Equal(new byte[] { 1, 2, 3 }, result.ToArray());
        }
        finally
        {
            packet.Return();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedMalformedResponseReturnsPoolMemory(bool resultAndValue)
    {
        using var connection = this.CreateConnection();
        var service = connection.GetService<IReviewService>();
        Task task = resultAndValue ? service.Result() : service.Echo("text", TestContext.Current.CancellationToken);
        var packet = this.DeliverResponse(connection, [0xc1]);
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
            if (task is Task<NetResultAndValue<int>> result)
            {
                Assert.Equal(NetResult.DeserializationFailed, (await result).Result);
            }

            Assert.Equal(1, packet.RentArray!.Count);
        }
        finally
        {
            packet.Return();
        }
    }

    [Fact]
    public async Task GeneratedSingleArgumentAndChannelRoundTrip()
    {
        this.fixture.NetUnit.NetTerminal.Services.EnableNetService<IReviewService>();
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var service = connection.GetService<IReviewService>();
        Assert.Equal("text", await service.Echo("text", TestContext.Current.CancellationToken));
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = new ResponseChannel<int>((_, value) => completion.TrySetResult(value));
        service.Channel(ref channel);
        Assert.Equal(42, await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GeneratedClassFiltersAreIsolatedAcrossConcurrentCalls()
    {
        this.fixture.NetUnit.NetTerminal.Services.EnableNetService<IReviewService>();
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var service = connection.GetService<IReviewService>();
        await Task.WhenAll(Enumerable.Range(0, 32).Select(async i =>
        {
            var text = i.ToString();
            Assert.Equal(text, await service.Echo(text, TestContext.Current.CancellationToken));
        }));
    }

    [Fact]
    public async Task GeneratedBorrowedMemoryCanBeReturnedByService()
    {
        this.fixture.NetUnit.NetTerminal.Services.EnableNetService<IReviewService>();
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var service = connection.GetService<IReviewService>();
        var data = Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray();
        var result = await service.EchoMemory(data);
        Assert.Equal(data, result.ToArray());
        Assert.Equal(new byte[] { 1, 2, 3 }, (await service.Memory()).ToArray());
        Assert.Equal(data, result.ToArray());
    }

    [Fact]
    public async Task GeneratedStreamRequestHonorsCancellation()
    {
        using var connection = this.CreateConnection();
        using var cancellation = new CancellationTokenSource();
        var task = connection.GetService<IReviewService>().Open(cancellation.Token);
        Assert.False(task.IsCompleted);
        cancellation.Cancel();
        Assert.Null(await task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        Assert.All(this.GetReceivers(connection), receiver => Assert.True(receiver.IsDisposed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbandonedTimedReceiveReturnsLateMemory(bool canceled)
    {
        using var connection = this.CreateConnection();
        var completion = new TaskCompletionSource<NetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var receiver = new ReceiveTransmission(connection, 42, completion, null);
        receiver.SetState_Receiving(1);
        if (canceled)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiver.Wait(completion.Task, TimeSpan.FromSeconds(10), new CancellationToken(true)));
        }
        else
        {
            await Assert.ThrowsAsync<TimeoutException>(() => receiver.Wait(completion.Task, TimeSpan.Zero, TestContext.Current.CancellationToken));
        }

        var packet = BytePool.Default.Rent(13).AsMemory(0, 13);
        packet.Span.Clear();
        try
        {
            receiver.ProcessReceive_Gene(DataControl.Valid, 0, packet);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            while (packet.RentArray!.Count != 1)
            {
                await Task.Delay(1, timeout.Token);
            }
        }
        finally
        {
            packet.Return();
        }
    }

    [Fact]
    public async Task DisposedResponseCallbackCanAcquireReceiverCollection()
    {
        using var connection = this.CreateConnection();
        var completion = new TaskCompletionSource<NetResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivers = this.GetReceivers(connection);
        IResponseChannelInternal channel = new ResponseChannel<int>((result, _) =>
        {
            using (receivers.LockObject.EnterScope())
            {
                completion.TrySetResult(result);
            }
        });
        using var receiver = connection.TryCreateReceiveTransmission(42, null, channel);
        Assert.NotNull(receiver);
        var closing = Task.Run(() => connection.CloseAllTransmission(), TestContext.Current.CancellationToken);
        await closing.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        Assert.Equal(NetResult.Closed, await completion.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentRelayEndpointCacheUpdatesRemainConsistent()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var agent = new RelayAgent(terminal.RelayControl, terminal);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(
            () =>
            {
                for (var i = 0; i < 200; i++)
                {
                    var address = new NetAddress(IPAddress.Loopback, (ushort)(10000 + ((i + worker) % 150)));
                    var result = agent.GetEndPoint_NotThreadSafe(address, RelayAgent.EndpointOperation.SetUnrestricted);
                    Assert.NotNull(result.EndPoint);
                    Assert.True(result.Unrestricted);
                }
            },
            TestContext.Current.CancellationToken)));
        var cache = (IEnumerable)typeof(RelayAgent).GetField("endPointCache", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(agent)!;
        Assert.Equal(100, cache.Cast<object>().Count());
    }

    private BytePool.RentMemory DeliverResponse(Connection connection, byte[] bytes)
    {
        var receiver = Assert.Single(this.GetReceivers(connection));
        receiver.SetState_Receiving(1);
        var packet = BytePool.Default.Rent(12 + bytes.Length).AsMemory(0, 12 + bytes.Length);
        packet.Span.Clear();
        bytes.CopyTo(packet.Span.Slice(12));
        receiver.ProcessReceive_Gene(DataControl.Valid, 0, packet);
        return packet;
    }

    private ClientConnection CreateConnection()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var connection = new ClientConnection(this.CreatePacketTerminal(), terminal.ConnectionTerminal, 42, Alternative.NetNode, new NetEndpoint(0, new IPEndPoint(IPAddress.Loopback, 12345)));
        connection.Initialize(new ConnectionAgreement { MaxBlockSize = 100_000, MaxStreamLength = 1000, StreamBufferSize = 10_000 }, new byte[Connection.EmbryoSize]);
        return connection;
    }

    private ReceiveTransmission.GoshujinClass GetReceivers(Connection connection)
        => (ReceiveTransmission.GoshujinClass)typeof(Connection).GetField("receiveTransmissions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(connection)!;

    private PacketTerminal CreatePacketTerminal()
        => new(this.fixture.NetUnit.NetBase, this.fixture.NetUnit.NetTerminal, this.fixture.NetUnit.ServiceProvider.GetRequiredService<LogUnit>().RootLogService.GetLogger<PacketTerminal>());

    private NetSender CreateSender()
        => new(this.fixture.NetUnit.NetTerminal, this.fixture.NetUnit.NetBase, this.fixture.NetUnit.ServiceProvider.GetRequiredService<LogUnit>().RootLogService.GetLogger<NetSender>());

    private object[] GetPackets(PacketTerminal terminal)
        => ((IEnumerable)typeof(PacketTerminal).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(terminal)!).Cast<object>().ToArray();

    private void ClearPackets(PacketTerminal terminal)
    {
        foreach (var item in this.GetPackets(terminal))
        {
            item.GetType().GetMethod("Remove")!.Invoke(item, null);
        }
    }
}

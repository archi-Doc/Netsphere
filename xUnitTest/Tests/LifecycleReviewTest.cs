// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Net;
using System.Reflection;
using Arc.Collections;
using Arc.Crypto;
using Arc.Unit;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;
using Netsphere.Core;
using Netsphere.Crypto;
using Netsphere.Packet;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class LifecycleReviewTest
{
    private readonly NetFixture fixture;

    public LifecycleReviewTest(NetFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task CleanDoesNotCloseConnectionReusedConcurrently()
    {
        var netTerminal = this.fixture.NetUnit.NetTerminal;
        var terminal = new ConnectionTerminal(this.fixture.NetUnit.ServiceProvider, netTerminal);
        var address = new NetAddress(IPAddress.Parse("10.1.2.3"), 12345);
        var node = new NetNode(address, Alternative.PublicKey);
        Assert.True(netTerminal.NetStats.TryCreateEndpoint(ref address, EndpointResolution.PreferIpv6, out var endpoint));
        var seedKey = SeedKey.NewEncryption();
        var request = new ConnectPacket(seedKey.GetEncryptionPublicKey(), node.PublicKey.GetHashCode(), default);
        var response = new ConnectPacketResponse(new ConnectionAgreement { MinimumConnectionRetentionMics = Mics.FromSeconds(1) }, endpoint);
        var connection = terminal.PrepareClientSide(node, endpoint, seedKey, node.PublicKey, request, response);
        var connections = (ClientConnection.GoshujinClass)typeof(ConnectionTerminal).GetField("clientConnections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(terminal)!;
        using (connections.LockObject.EnterScope())
        {
            connection.IncrementOpenCount();
            connection.Goshujin = connections;
        }

        try
        {
            var expired = Mics.GetSystem() - connection.Agreement.MinimumConnectionRetentionMics - Mics.FromSeconds(1);
            for (var i = 0; i < 200; i++)
            {
                connection.LastEventMics = expired;
                var reuse = Task.Run(() => terminal.Connect(node, Connection.ConnectMode.ReuseOnly), TestContext.Current.CancellationToken);
                var clean = Task.Run(() => terminal.Clean(), TestContext.Current.CancellationToken);
                await Task.WhenAll(reuse, clean);

                // Connect() always refreshes and reopens the connection; Clean() must not close it afterwards.
                Assert.Same(connection, await reuse);
                Assert.True(connection.IsOpen);
                Assert.Equal(connections, connection.Goshujin);
            }
        }
        finally
        {
            using (connections.LockObject.EnterScope())
            {
                connection.ChangeStateInternal(Connection.State.Disposed);
                connection.Goshujin = null;
            }
        }
    }

    [Fact]
    public async Task ClosedServerConnectionIsReopenedOnlyByAuthenticatedPackets()
    {
        var netTerminal = this.fixture.NetUnit.NetTerminal;
        using var client = await netTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(client);
        var server = client.PrepareBidirectionalConnection();
        server.ChangeStateInternal(Connection.State.Closed);
        Assert.True(server.IsClosed);

        // A packet carrying the connection id but an invalid ciphertext must not reopen the connection.
        var forged = PacketPool.Rent().AsMemory(0, PacketHeader.Length + ProtectedPacket.Length + 32);
        try
        {
            RandomVault.Default.NextBytes(forged.Span);
            BitConverter.TryWriteBytes(forged.Span, (ushort)0); // SourceRelayId
            BitConverter.TryWriteBytes(forged.Span.Slice(sizeof(ushort)), (ushort)0); // DestinationRelayId
            BitConverter.TryWriteBytes(forged.Span.Slice(8), (ushort)PacketType.Protected);
            BitConverter.TryWriteBytes(forged.Span.Slice(10), client.ConnectionId);
            netTerminal.ConnectionTerminal.ProcessReceive(server.DestinationEndpoint, false, (ushort)PacketType.Protected, forged, Mics.FastSystem);
            Assert.True(server.IsClosed);
        }
        finally
        {
            forged.Return();
        }

        // A frame encrypted with the connection key reopens it.
        var frame = new byte[KnockFrame.Length];
        BitConverter.TryWriteBytes(frame, (ushort)FrameType.Knock);
        BitConverter.TryWriteBytes(frame.AsSpan(sizeof(ushort)), 42u);
        Assert.True(client.CreatePacket(frame, out var authenticated));
        try
        {
            netTerminal.ConnectionTerminal.ProcessReceive(server.DestinationEndpoint, false, (ushort)PacketType.Protected, authenticated, Mics.FastSystem);
            Assert.True(server.IsOpen);
        }
        finally
        {
            authenticated.Return();
        }
    }

    [Fact]
    public async Task RemovedCongestionControlDoesNotSkipTheRemainingControllers()
    {
        // The send thread invokes ProcessSend() every round, so observe the round in which the removal happens.
        var terminal = this.fixture.NetUnit.NetTerminal.ConnectionTerminal;
        var tracker = new RoundTracker();
        var marker = new RecordingCongestionControl(tracker, RecordingCongestionControl.Role.Marker);
        var removed = new RecordingCongestionControl(tracker, RecordingCongestionControl.Role.Removed);
        var kept = new RecordingCongestionControl(tracker, RecordingCongestionControl.Role.Kept);
        lock (terminal.CongestionControlList)
        {
            terminal.CongestionControlList.AddLast(removed);
            terminal.CongestionControlList.AddLast(kept);
            terminal.CongestionControlList.AddFirst(marker);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            while (Volatile.Read(ref tracker.KeptRounds) < 2)
            {
                await Task.Delay(1, timeout.Token);
            }

            Assert.Equal(1, Volatile.Read(ref tracker.RemovedRounds));
            Assert.True(Volatile.Read(ref tracker.KeptProcessedInRemovalRound));
            lock (terminal.CongestionControlList)
            {
                Assert.DoesNotContain(removed, terminal.CongestionControlList);
                Assert.Contains(kept, terminal.CongestionControlList);
            }
        }
        finally
        {
            lock (terminal.CongestionControlList)
            {
                terminal.CongestionControlList.Remove(marker);
                terminal.CongestionControlList.Remove(removed);
                terminal.CongestionControlList.Remove(kept);
            }
        }
    }

    [Fact]
    public async Task CanceledSendReturnsCanceledWithoutWaitingForTheTransmissionTimeout()
    {
        using var connection = this.CreateConnection();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var stopwatch = Stopwatch.StartNew();
        var result = await connection.Send(1, 0, cancellation.Token);
        Assert.Equal(NetResult.Canceled, result);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), stopwatch.Elapsed.ToString());
    }

    [Fact]
    public async Task CancellationWhileWaitingForATransmissionSlotReturnsCanceled()
    {
        using var connection = this.CreateConnection();
        connection.Agreement.MaxTransmissions = 1;
        using var occupied = connection.TryCreateSendTransmission();
        Assert.NotNull(occupied);

        Assert.Equal(NetResult.Canceled, await connection.Send(1, 0, new CancellationToken(true)));
        Assert.Equal(NetResult.Canceled, (await connection.SendAndReceive<int, int>(1, 0, new CancellationToken(true))).Result);
        Assert.Equal(NetResult.Canceled, (await connection.SendAndReceiveStream(1, 0, new CancellationToken(true))).Result);
        var rpc = await ((Netsphere.Internal.IClientConnectionInternal)connection).RpcSendAndReceive(BytePool.RentedMemory.Empty, 1, new CancellationToken(true));
        Assert.Equal(NetResult.Canceled, rpc.Result);
    }

    [Theory]
    [InlineData(new byte[] { 0xd0, 0x01, })] // int8: not a string.
    [InlineData(new byte[] { 0xc0, })] // nil: not accepted for a non-nullable parameter.
    public async Task GeneratedBackendReleasesTheRequestOnDeserializationFailure(byte[] payload)
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        var method = GetServiceMethod<IReviewService>("Echo(string, System.Threading.CancellationToken)");
        var request = BytePool.Default.Rent(payload.Length).AsMemory(0, payload.Length);
        var owner = request.Owner!;
        payload.CopyTo(request.Span);
        var context = new TransmissionContext(server, 42, 1, method.Id, request);
        await method.Invoke(new ReviewService(), context);

        // The request bytes must not remain as the response payload.
        Assert.Equal(NetResult.DeserializationFailed, context.Result);
        Assert.True(context.RentMemory.IsEmpty);
        Assert.Equal(0, owner.ReferenceCount);
    }

    [Fact]
    public async Task GeneratedBackendReleasesTheRequestWhenNoStreamIsOpened()
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        var method = GetServiceMethod<IReviewService>("Open(System.Threading.CancellationToken)");
        var request = BytePool.Default.Rent(16).AsMemory(0, 16);
        var owner = request.Owner!;
        var context = new TransmissionContext(server, 42, 1, method.Id, request);
        await method.Invoke(new ReviewService(), context);

        Assert.Equal(NetResult.Success, context.Result);
        Assert.True(context.RentMemory.IsEmpty);
        Assert.Equal(0, owner.ReferenceCount);
    }

    [Fact]
    public async Task StreamRequestWithoutAStreamReturnsNull()
    {
        this.fixture.NetUnit.Services.EnableNetService<IReviewService>();
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        Assert.Null(await connection.GetService<IReviewService>().Open(TestContext.Current.CancellationToken));
        Assert.All(this.GetReceivers(connection), receiver => Assert.True(receiver.IsDisposed));
    }

    private static ServiceMethod GetServiceMethod<TService>(string signature)
    {
        var serviceType = typeof(TService);
        Assert.True(StaticNetService.ServiceInfoTable.TryGetValue(serviceType, out var serviceInfo));
        var id = ((ulong)serviceInfo.ServiceId << 32) | (uint)FarmHash.Hash64($"{serviceType.FullName}.{signature}");
        Assert.True(serviceInfo.NetObjectInfo.TryGetMethod(id, out var method));
        return method;
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

#pragma warning disable SA1401 // Fields should be private (read with Volatile/Interlocked).
    private sealed class RoundTracker
    {
        public int Round;
        public int RemovalRound = -1;
        public int RemovedRounds;
        public int KeptRounds;
        public bool KeptProcessedInRemovalRound;
    }
#pragma warning restore SA1401

    private sealed class RecordingCongestionControl(RoundTracker tracker, RecordingCongestionControl.Role role) : ICongestionControl
    {
        public enum Role
        {
            Marker, // First in the list: starts a new round.
            Removed, // Asks to be removed from the list.
            Kept, // Must still be processed in the round that removed the previous controller.
        }

        public Lock SyncObject { get; } = new();

        public int NumberInFlight => 0;

        public bool IsCongested => false;

        public bool Process(NetSender netSender, long elapsedMics, double elapsedMilliseconds)
        {// lock (ConnectionTerminal.CongestionControlList)
            switch (role)
            {
                case Role.Marker:
                    tracker.Round++;
                    return true;

                case Role.Removed:
                    tracker.RemovalRound = tracker.Round;
                    Interlocked.Increment(ref tracker.RemovedRounds);
                    return false;

                default:
                    if (tracker.Round == tracker.RemovalRound)
                    {
                        Volatile.Write(ref tracker.KeptProcessedInRemovalRound, true);
                    }

                    Interlocked.Increment(ref tracker.KeptRounds);
                    return true;
            }
        }

        public void AddInFlight(SendGene sendGene, int additional)
        {
        }

        public void RemoveInFlight(SendGene sendGene, bool ack)
        {
        }

        public void LossDetected(SendGene sendGene)
        {
        }

        public void AddRtt(int rttMics)
        {
        }
    }
}

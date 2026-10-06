// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using System.Reflection;
using Arc.Collections;
using Arc.Unit;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;
using Netsphere.Core;
using Netsphere.Crypto;
using Netsphere.Packet;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class ConnectionPacketConcurrencyAuditTest
{
    private readonly NetFixture fixture;

    public ConnectionPacketConcurrencyAuditTest(NetFixture fixture) => this.fixture = fixture;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingPublishesStateBeforeWaitingForTransmissions(bool server)
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var connections = new ConnectionTerminal(this.fixture.NetUnit.ServiceProvider, terminal);
        using var client = this.CreateConnection(connections);
        using var serverConnection = new ServerConnection(client);
        Connection connection = server ? serverConnection : client;
        if (server)
        {
            GetField<ServerConnection.GoshujinClass>(connections, "serverConnections").Add(serverConnection);
        }
        else
        {
            GetField<ClientConnection.GoshujinClass>(connections, "clientConnections").Add(client);
        }

        var transmissions = (SendTransmission.GoshujinClass)typeof(Connection).GetField("sendTransmissions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(connection)!;
        Task closing;
        bool closedBeforeCleanup;
        using (transmissions.LockObject.EnterScope())
        {
            closing = Task.Run(() => connections.CloseInternal(connection, false), TestContext.Current.CancellationToken);
            closedBeforeCleanup = SpinWait.SpinUntil(() => connection.IsClosed, TimeSpan.FromSeconds(3));
        }

        await closing.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.True(closedBeforeCleanup);
        Assert.Null(connection.TryCreateSendTransmission(42));
        Assert.Null(connection.TryCreateReceiveTransmission(42, null));
    }

    [Theory]
    [InlineData(Connection.State.Closed)]
    [InlineData(Connection.State.Disposed)]
    public void FirstGeneCannotCreateTransmissionAfterClosure(Connection.State state)
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        server.ChangeStateInternal(state);
        var memory = BytePool.Default.Rent(FirstGeneFrame.LengthExcludingFrameType + FirstGeneFrame.MaxGeneLength)
            .AsMemory(0, FirstGeneFrame.LengthExcludingFrameType + FirstGeneFrame.MaxGeneLength);
        try
        {
            memory.Span.Clear();
            BitConverter.TryWriteBytes(memory.Span.Slice(2), 42u);
            BitConverter.TryWriteBytes(memory.Span.Slice(6), (ushort)DataControl.Valid);
            BitConverter.TryWriteBytes(memory.Span.Slice(12), 2);
            server.ProcessReceive_FirstGene(server.DestinationEndpoint, memory);
            Assert.Equal(0, server.ReceiveTransmissionsCount);
            Assert.Equal(1, memory.Owner!.ReferenceCount);
        }
        finally
        {
            server.CloseAllTransmission();
            memory.Return();
        }
    }

    [Fact]
    public async Task ReuseRequiresTheRequestedPublicKey()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var connections = new ConnectionTerminal(this.fixture.NetUnit.ServiceProvider, terminal);
        using var connection = this.CreateConnection(connections);
        var clients = GetField<ClientConnection.GoshujinClass>(connections, "clientConnections");
        connection.IncrementOpenCount();
        clients.Add(connection);
        var differentIdentity = new NetNode(connection.DestinationNode.Address, SeedKey.NewEncryption().GetEncryptionPublicKey());

        Assert.Null(await connections.Connect(differentIdentity, Connection.ConnectMode.ReuseOnly, endpointResolution: EndpointResolution.Ipv4));
        using var reused = await connections.Connect(connection.DestinationNode, Connection.ConnectMode.ReuseOnly, endpointResolution: EndpointResolution.Ipv4);
        Assert.Same(connection, reused);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutgoingRelayDispatchesOnlyToMatchingBidirectionalServer(bool incoming)
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var connections = new ConnectionTerminal(this.fixture.NetUnit.ServiceProvider, terminal);
        using var client = this.CreateConnection(connections);
        client.UseIncomingRelay = incoming;
        using var server = new ServerConnection(client);
        GetField<ServerConnection.GoshujinClass>(connections, "serverConnections").Add(server);
        server.ChangeStateInternal(Connection.State.Closed);
        var frame = new byte[KnockFrame.Length];
        BitConverter.TryWriteBytes(frame, (ushort)FrameType.Knock);
        BitConverter.TryWriteBytes(frame.AsSpan(sizeof(ushort)), 42u);
        Assert.True(client.CreatePacket(frame, out var packet));
        try
        {
            connections.ProcessReceive(server.DestinationEndpoint, true, (ushort)PacketType.Protected, packet, Mics.FastSystem);
            Assert.Equal(!incoming, server.IsOpen);
        }
        finally
        {
            packet.Return();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetryCopiesOnlyWhileAnEarlierSendOwnsThePacket(bool previousSendQueued)
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var logs = this.fixture.NetUnit.ServiceProvider.GetRequiredService<LogUnit>().RootLogService;
        var packets = new PacketTerminal(terminal.NetBase, terminal, logs.GetLogger<PacketTerminal>()) { RetransmissionTimeoutMics = long.MaxValue };
        var sender = new NetSender(terminal, terminal.NetBase, logs.GetLogger<NetSender>());
        typeof(NetSender).GetMethod("Prepare", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(sender, null);
        var queue = GetField<Queue<NetSender.Item>>(sender, "itemsIpv4");
        PacketTerminal.CreatePacket(42, new PingPacket("retry"), out var request);
        var owner = request.Owner!;
        var original = request.Span.ToArray();
        packets.SendPacketWithoutRelay(new(0, new IPEndPoint(IPAddress.Loopback, 12345)), request, new(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            packets.ProcessSend(sender);
            if (!previousSendQueued)
            {
                Assert.True(queue.TryDequeue(out var sent));
                sent.MemoryOwner.Return();
            }

            packets.RetransmissionTimeoutMics = -1;
            packets.MaxResendCount = 1;
            packets.ProcessSend(sender);
            var retry = queue.Last();
            if (previousSendQueued)
            {
                Assert.NotSame(owner, retry.MemoryOwner.Owner);
                Assert.Equal(original, queue.First().MemoryOwner.Span.ToArray());
            }
            else
            {
                Assert.Same(owner, retry.MemoryOwner.Owner);
            }
        }
        finally
        {
            sender.Stop();
            packets.Stop();
        }

        Assert.Equal(0, owner.ReferenceCount);
    }

    private static T GetField<T>(object instance, string name)
        => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private ClientConnection CreateConnection(ConnectionTerminal? connections = null)
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var endpoint = new NetEndpoint(0, new IPEndPoint(IPAddress.Loopback, 12345));
        var connection = new ClientConnection(terminal.PacketTerminal, connections ?? terminal.ConnectionTerminal, 12345, new NetNode(new NetAddress(endpoint), Alternative.PublicKey), endpoint);
        connection.Initialize(new ConnectionAgreement { MaxBlockSize = 100_000 }, new byte[Connection.EmbryoSize]);
        return connection;
    }
}

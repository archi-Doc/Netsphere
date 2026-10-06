// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using Arc.Collections;
using Netsphere;
using Netsphere.Core;
using Netsphere.Crypto;
using Netsphere.Packet;
using Netsphere.Relay;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class RelayConcurrencyAuditTest
{
    private readonly NetFixture fixture;

    public RelayConcurrencyAuditTest(NetFixture fixture) => this.fixture = fixture;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(123, 0)]
    [InlineData(123, 37)]
    public void OutermostRelayWrapsPacketsFromOtherRelays(ushort sourceRelayId, int offset)
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        var agent = new RelayAgent(terminal.RelayControl, terminal);
        var block = new AssignRelayBlock(false, false);
        Assert.Equal(RelayResult.Success, agent.AddExchange(server, block, out var inner, out var outer, 10));
        var nodes = new RelayNode.GoshujinClass();
        nodes.Add(new(block, new(RelayResult.Success, inner, outer, 10, 1000, null), client));
        var key = new RelayKey(nodes);
        var remote = new NetEndpoint(sourceRelayId, new IPEndPoint(IPAddress.Loopback, 23456));
        agent.GetEndPoint_NotThreadSafe(new(remote), RelayAgent.EndpointOperation.SetUnrestricted);

        PacketTerminal.CreatePacket(42, new PingPacket("another relay"), out var content);
        var owner = PacketPool.Rent();
        owner.AsSpan().Fill(0xa5);
        var source = owner.AsMemory(offset, content.Length);
        content.Span.CopyTo(source.Span);
        content.Return();
        BitConverter.TryWriteBytes(source.Span, sourceRelayId);
        BitConverter.TryWriteBytes(source.Span.Slice(sizeof(ushort)), outer);
        var expected = source.Span.ToArray();
        BitConverter.TryWriteBytes(expected.AsSpan(sizeof(ushort)), (ushort)0);
        try
        {
            Assert.False(agent.ProcessRelay(remote, outer, source, out _));
            var queue = GetQueue(agent);
            Assert.True(queue.TryDequeue(out var item));
            var response = item.MemoryOwner;
            try
            {
                Assert.True(key.TryDecrypt(key.FirstEndpoint, ref response, out var original, out var hops));
                Assert.Equal(new NetAddress(remote), original);
                Assert.Equal(1, hops);
                Assert.Equal(expected, response.Span.ToArray());
                Assert.All(owner.AsSpan(0, offset).ToArray(), value => Assert.Equal((byte)0xa5, value));
                Assert.Equal(9, agent.ProcessPingRelay(inner)!.RelayPoint);
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
    public async Task RelayControlConnectionKeepsItsCircuitDirection(bool incoming)
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        using var connection = this.CreateConnection();
        var circuit = new RelayCircuit(terminal, incoming);
        Assert.Equal(RelayResult.Success, await circuit.AddRelay(circuit.NewAssignRelayBlock(), new(RelayResult.Success, 1, 2, 100, 1000, null), connection));
        Assert.Equal(incoming, connection.UseIncomingRelay);
        Assert.Same(incoming ? terminal.IncomingCircuit.RelayKey : terminal.OutgoingCircuit.RelayKey, connection.CorrespondingRelayKey);
        using var reverse = new ServerConnection(connection);
        Assert.Equal(incoming, reverse.UseIncomingRelay);
        Assert.Same(connection.CorrespondingRelayKey, reverse.CorrespondingRelayKey);
    }

    [Fact]
    public async Task IncomingRelayControlConnectionCanExchangeServiceMessages()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var seedKey = SeedKey.NewSignature();
        Assert.IsType<CertificateRelayControl>(terminal.RelayControl).SetCertificatePublicKey(seedKey.GetSignaturePublicKey());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var connection = await terminal.ConnectForRelay(Alternative.NetNode, true, 0, EndpointResolution.Ipv4);
        Assert.NotNull(connection);
        var circuit = terminal.IncomingCircuit;
        try
        {
            var block = circuit.NewAssignRelayBlock();
            var token = new CertificateToken<AssignRelayBlock>(block);
            connection.SignWithSalt(token, seedKey);
            var assignment = await connection.SendAndReceive<CertificateToken<AssignRelayBlock>, AssignRelayResponse>(token, 0, timeout.Token);
            Assert.Equal(NetResult.Success, assignment.Result);
            Assert.NotNull(assignment.Value);
            Assert.Equal(RelayResult.Success, await circuit.AddRelay(block, assignment.Value, connection));

            Assert.Equal(43, await connection.GetService<IBasicService>().IncrementInt(42).WaitAsync(timeout.Token));
        }
        finally
        {
            await circuit.Close();
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(int.MinValue)]
    public async Task RelayAssignmentRejectsAConnectionFromAnotherCircuitDepth(int numberOfRelays)
    {
        using var connection = this.CreateConnection();
        connection.MinimumNumberOfRelays = numberOfRelays;
        var circuit = new RelayCircuit(this.fixture.NetUnit.NetTerminal, false);
        var previousKey = circuit.RelayKey;
        Assert.Equal(RelayResult.ConnectionFailure, await circuit.AddRelay(circuit.NewAssignRelayBlock(), new(RelayResult.Success, 1, 2, 100, 1000, null), connection));
        Assert.Same(previousKey, circuit.RelayKey);
        Assert.Equal(numberOfRelays, connection.MinimumNumberOfRelays);
    }

    [Fact]
    public async Task FailedOuterSetupClosesTheUnusableCircuit()
    {
        using var first = this.CreateConnection();
        using var candidate = this.CreateConnection(connectionId: 12346);
        candidate.DestinationEndpoint = new(0, new IPEndPoint(IPAddress.Loopback, 12346));
        var circuit = new RelayCircuit(this.fixture.NetUnit.NetTerminal, false);
        Assert.Equal(RelayResult.Success, await circuit.AddRelay(circuit.NewAssignRelayBlock(), new(RelayResult.Success, 1, 2, 100, 1000, null), first));
        first.ChangeStateInternal(Connection.State.Closed);
        candidate.MinimumNumberOfRelays = 1;
        Assert.Equal(RelayResult.ConnectionFailure, await circuit.AddRelay(circuit.NewAssignRelayBlock(), new(RelayResult.Success, 3, 4, 100, 1000, null), candidate));
        Assert.Equal(0, circuit.NumberOfRelays);
        Assert.False(circuit.TryGetOutermostAddress(out _));
        Assert.Equal(1, candidate.MinimumNumberOfRelays);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingRelayCircuitPreservesOtherCircuitAndDirectConnections(bool incoming)
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var connections = new ConnectionTerminal(this.fixture.NetUnit.ServiceProvider, terminal);
        var clients = GetField<ClientConnection.GoshujinClass>(connections, "clientConnections");
        var servers = GetField<ServerConnection.GoshujinClass>(connections, "serverConnections");
        var all = new List<Connection>();
        for (var i = 0; i < 3; i++)
        {
            var client = this.CreateConnection(connections, (ulong)(12345 + i));
            client.UseIncomingRelay = i == 0;
            client.MinimumNumberOfRelays = i < 2 ? 1 : 0;
            var server = new ServerConnection(client);
            clients.Add(client);
            servers.Add(server);
            all.Add(client);
            all.Add(server);
        }

        try
        {
            connections.CloseRelayedConnections(incoming);
            foreach (var connection in all)
            {
                var selected = connection.MinimumNumberOfRelays > 0 && connection.UseIncomingRelay == incoming;
                Assert.Equal(selected, connection.IsDisposed);
                Assert.Equal(!selected, connection.IsOpen);
                if (connection is ClientConnection client)
                {
                    Assert.Equal(!selected, client.Goshujin == clients);
                }
                else if (connection is ServerConnection server)
                {
                    Assert.Equal(!selected, server.Goshujin == servers);
                }
            }
        }
        finally
        {
            foreach (var connection in all)
            {
                connections.CloseInternal(connection, false);
                connection.Dispose();
            }
        }
    }

    [Fact]
    public void RemovingRelayNodeForcesClosureWhileCallerOwnsAReference()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var connections = new ConnectionTerminal(this.fixture.NetUnit.ServiceProvider, terminal);
        using var client = this.CreateConnection(connections);
        client.SetOpenCount(2); // The circuit and its caller both retain the connection.
        client.MinimumNumberOfRelays = -1;
        GetField<ClientConnection.GoshujinClass>(connections, "clientConnections").Add(client);
        var nodes = new RelayNode.GoshujinClass();
        var node = new RelayNode(new(false, false), new(RelayResult.Success, 1, 2, 100, 1000, null), client);
        nodes.Add(node);

        node.Remove();

        Assert.True(client.IsClosed);
        Assert.Empty(nodes);
    }

    private static T GetField<T>(object instance, string name)
        => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static ConcurrentQueue<NetSender.Item> GetQueue(RelayAgent agent)
        => GetField<ConcurrentQueue<NetSender.Item>>(agent, "sendItems");

    private ClientConnection CreateConnection(ConnectionTerminal? connections = null, ulong connectionId = 12345)
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var connection = new ClientConnection(terminal.PacketTerminal, connections ?? terminal.ConnectionTerminal, connectionId, Alternative.NetNode, new(0, new IPEndPoint(IPAddress.Loopback, 12345)));
        connection.Initialize(new ConnectionAgreement { TransmissionTimeout = TimeSpan.FromSeconds(30) }, new byte[Connection.EmbryoSize]);
        return connection;
    }
}

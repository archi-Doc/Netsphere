// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using System.Reflection;
using Netsphere;
using Netsphere.Core;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class BidirectionalRelayCloseAuditTest
{
    private readonly NetFixture fixture;

    public BidirectionalRelayCloseAuditTest(NetFixture fixture) => this.fixture = fixture;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingRelayCannotRegisterReverseClientAfterClientSnapshot(bool incoming)
    {
        var terminal = new ConnectionTerminal(this.fixture.NetUnit.ServiceProvider, this.fixture.NetUnit.NetTerminal);
        var clients = GetField<ClientConnection.GoshujinClass>(terminal, "clientConnections");
        var servers = GetField<ServerConnection.GoshujinClass>(terminal, "serverConnections");
        using var sentinel = this.CreateClient(terminal, 1, incoming);
        using var sourceClient = this.CreateClient(terminal, 2, incoming);
        using var source = new ServerConnection(sourceClient);
        using var otherClient = this.CreateClient(terminal, 3, !incoming);
        using var otherSource = new ServerConnection(otherClient);
        clients.Add(sentinel);
        servers.Add(source);
        servers.Add(otherSource);

        Task closing;
        bool snapshotTaken;
        Exception? registrationError = null;
        ClientConnection? otherReverse = null;
        using (servers.LockObject.EnterScope())
        {
            closing = Task.Run(() => terminal.CloseRelayedConnections(incoming), TestContext.Current.CancellationToken);
            snapshotTaken = SpinWait.SpinUntil(() => sentinel.Goshujin is null, TimeSpan.FromSeconds(3));
            if (snapshotTaken)
            {
                // The server is still open, but the client snapshot has already passed. A new reverse
                // client here used to escape relay cleanup. The other circuit must remain usable.
                registrationError = Record.Exception(() => terminal.PrepareBidirectionalConnection(source));
                otherReverse = terminal.PrepareBidirectionalConnection(otherSource);
            }
        }

        try
        {
            await closing.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.True(snapshotTaken);
            Assert.IsType<ObjectDisposedException>(registrationError);
            Assert.True(source.IsDisposed);
            Assert.False(clients.ConnectionIdChain.TryGetValue(source.ConnectionId, out _));
            Assert.NotNull(otherReverse);
            Assert.True(otherReverse.IsOpen);
            Assert.True(otherSource.IsOpen);
            Assert.Throws<ObjectDisposedException>(() => terminal.PrepareBidirectionalConnection(source));
        }
        finally
        {
            await terminal.Terminate(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingRelayCannotRegisterReverseServerAfterBothSnapshots(bool incoming)
    {
        var terminal = new ConnectionTerminal(this.fixture.NetUnit.ServiceProvider, this.fixture.NetUnit.NetTerminal);
        var clients = GetField<ClientConnection.GoshujinClass>(terminal, "clientConnections");
        var servers = GetField<ServerConnection.GoshujinClass>(terminal, "serverConnections");
        using var first = this.CreateClient(terminal, 1, incoming);
        using var second = this.CreateClient(terminal, 2, incoming);
        clients.Add(first);
        clients.Add(second);
        var snapshot = clients.ToArray();
        var blocker = snapshot[0];
        var source = snapshot[1];
        var transmissions = (SendTransmission.GoshujinClass)typeof(Connection).GetField("sendTransmissions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(blocker)!;

        Task closing;
        bool snapshotsTaken;
        Exception? registrationError = null;
        using (transmissions.LockObject.EnterScope())
        {
            closing = Task.Run(() => terminal.CloseRelayedConnections(incoming), TestContext.Current.CancellationToken);
            snapshotsTaken = SpinWait.SpinUntil(() => blocker.IsDisposed, TimeSpan.FromSeconds(3));
            if (snapshotsTaken)
            {
                // Disposal is blocked on the earlier client's transmissions. Both owner snapshots have
                // completed, but this client has not changed state yet.
                registrationError = Record.Exception(() => terminal.PrepareBidirectionalConnection(source));
            }
        }

        try
        {
            await closing.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.True(snapshotsTaken);
            Assert.IsType<ObjectDisposedException>(registrationError);
            Assert.True(source.IsDisposed);
            Assert.False(servers.ConnectionIdChain.TryGetValue(source.ConnectionId, out _));
            Assert.Throws<ObjectDisposedException>(() => terminal.PrepareBidirectionalConnection(source));
        }
        finally
        {
            await terminal.Terminate(CancellationToken.None);
        }
    }

    private static T GetField<T>(object instance, string name)
        => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private ClientConnection CreateClient(ConnectionTerminal terminal, ulong id, bool incoming)
    {
        var endpoint = new NetEndpoint(0, new IPEndPoint(IPAddress.Loopback, 12345));
        var connection = new ClientConnection(this.fixture.NetUnit.NetTerminal.PacketTerminal, terminal, id, Alternative.NetNode, endpoint);
        connection.Initialize(new ConnectionAgreement(), new byte[Connection.EmbryoSize]);
        connection.MinimumNumberOfRelays = 1;
        connection.UseIncomingRelay = incoming;
        return connection;
    }
}

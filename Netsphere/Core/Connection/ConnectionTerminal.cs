// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Netsphere.Core;
using Netsphere.Crypto;
using Netsphere.Packet;
using Netsphere.Relay;
using Netsphere.Stats;

#pragma warning disable SA1214
#pragma warning disable SA1401 // Fields should be private

namespace Netsphere;

public class ConnectionTerminal
{// ConnectionStateCode: Open -> Closed -> Disposed
    private static readonly long AdditionalServerMics = Mics.FromSeconds(1);

    public ConnectionTerminal(IServiceProvider serviceProvider, NetTerminal netTerminal)
    {
        this.ServiceProvider = serviceProvider;
        this.NetBase = netTerminal.NetBase;
        this.NetTerminal = netTerminal;
        this.AckQueue = new(this);
        this.packetTerminal = this.NetTerminal.PacketTerminal;
        this.netStats = this.NetTerminal.NetStats;
        this.CongestionControlList.AddFirst(this.NoCongestionControl);

        this.logger = this.NetTerminal.UnitLogger.GetLogger<ConnectionTerminal>();
    }

    #region FieldAndProperty

    public NetBase NetBase { get; }

    internal IServiceProvider ServiceProvider { get; }

    internal UnitLogger UnitLogger => this.NetTerminal.UnitLogger;

    internal NetTerminal NetTerminal { get; }

    internal AckBuffer AckQueue { get; }

    internal ICongestionControl NoCongestionControl { get; } = new NoCongestionControl();

    internal uint ReceiveTransmissionGap { get; private set; }

    internal readonly Lock LockSend = new();
    internal UnorderedLinkedList<Connection> SendList = new(); // using (this.LockSend.EnterScope())
    internal UnorderedLinkedList<Connection> CongestedList = new(); // using (this.LockSend.EnterScope())

    // lock (this.CongestionControlList)
    internal UnorderedLinkedList<ICongestionControl> CongestionControlList = new();
    private long lastCongestionControlMics;

    private readonly PacketTerminal packetTerminal;
    private readonly NetStats netStats;
    private readonly ILogger logger;

    private readonly ClientConnection.GoshujinClass clientConnections = new();
    private readonly ServerConnection.GoshujinClass serverConnections = new();

    #endregion

    public void Clean()
    {
        var systemCurrentMics = Mics.GetSystem();

        (UnorderedMap<NetEndpoint, ClientConnection>.Node[] Nodes, int Max) client;
        TemporaryList<ClientConnection> clientToChange = default;
        using (this.clientConnections.LockObject.EnterScope())
        {
            client = this.clientConnections.DestinationEndpointChain.UnsafeGetNodes();
        }

        for (var i = 0; i < client.Max; i++)
        {
            if (client.Nodes[i].Value is { } clientConnection)
            {
                Debug.Assert(!clientConnection.IsDisposed);
                if (clientConnection.IsOpen)
                {
                    if (clientConnection.LastEventMics + clientConnection.Agreement.MinimumConnectionRetentionMics < systemCurrentMics)
                    {// Open -> Closed
                        clientConnection.Logger.TryGet(LogLevel.Debug)?.Log($"{clientConnection.ConnectionIdText} Close (unused)");
                        clientToChange.Add(clientConnection);

                        clientConnection.SendCloseFrame();
                    }
                }
                else if (clientConnection.IsClosed)
                {// Closed -> Dispose
                    if (clientConnection.LastEventMics + NetConstants.ConnectionClosedToDisposalMics < systemCurrentMics)
                    {
                        clientConnection.Logger.TryGet(LogLevel.Debug)?.Log($"{clientConnection.ConnectionIdText} Disposed");
                        clientToChange.Add(clientConnection);

                        clientConnection.CloseAllTransmission();
                    }
                }

                clientConnection.CleanTransmission();
            }
        }

        using (this.clientConnections.LockObject.EnterScope())
        {
            foreach (var clientConnection in clientToChange)
            {
                if (clientConnection.IsOpen)
                {// Open -> Closed
                    clientConnection.ChangeStateInternal(Connection.State.Closed);
                }
                else if (clientConnection.IsClosed)
                {// Closed -> Dispose
                    clientConnection.ChangeStateInternal(Connection.State.Disposed);
                    clientConnection.Goshujin = null;
                }
            }
        }

        (UnorderedMap<NetEndpoint, ServerConnection>.Node[] Nodes, int Max) server;
        TemporaryList<ServerConnection> serverToChange = default;
        using (this.serverConnections.LockObject.EnterScope())
        {
            server = this.serverConnections.DestinationEndpointChain.UnsafeGetNodes();
        }

        for (var i = 0; i < server.Max; i++)
        {
            if (server.Nodes[i].Value is { } serverConnection)
            {
                Debug.Assert(!serverConnection.IsDisposed);
                if (serverConnection.IsOpen)
                {
                    if (serverConnection.LastEventMics + serverConnection.Agreement.MinimumConnectionRetentionMics < systemCurrentMics)
                    {// Open -> Closed
                        serverConnection.Logger.TryGet(LogLevel.Debug)?.Log($"{serverConnection.ConnectionIdText} Close (unused)");
                        serverToChange.Add(serverConnection);

                        serverConnection.SendCloseFrame();
                    }
                }
                else if (serverConnection.IsClosed)
                {// Closed -> Dispose
                    if (serverConnection.LastEventMics + NetConstants.ConnectionClosedToDisposalMics + AdditionalServerMics < systemCurrentMics)
                    {
                        serverConnection.Logger.TryGet(LogLevel.Debug)?.Log($"{serverConnection.ConnectionIdText} Disposed");
                        serverToChange.Add(serverConnection);

                        serverConnection.CloseAllTransmission();
                    }
                }

                serverConnection.CleanTransmission();
            }
        }

        using (this.serverConnections.LockObject.EnterScope())
        {
            foreach (var serverConnection in serverToChange)
            {
                if (serverConnection.IsOpen)
                {// Open -> Closed
                    serverConnection.ChangeStateInternal(Connection.State.Closed);
                }
                else if (serverConnection.IsClosed)
                {// Closed -> Dispose
                    serverConnection.ChangeStateInternal(Connection.State.Disposed);
                    serverConnection.Goshujin = null;
                }
            }
        }
    }

    public async Task<ClientConnection?> ConnectForRelay(NetNode node, bool incomingRelay, int targetNumberOfRelays, EndpointResolution endpointResolution)
    {
        if (!this.NetTerminal.IsActive)
        {
            return null;
        }

        if (node.Address.RelayId != 0)
        {
            return null;
        }

        var address = node.Address;
        if (!this.netStats.TryCreateEndpoint(ref address, endpointResolution, out var endPoint))
        {
            return null;
        }

        var circuit = incomingRelay ? this.NetTerminal.IncomingCircuit : this.NetTerminal.OutgoingCircuit;
        if (targetNumberOfRelays < 0 ||
            circuit.NumberOfRelays != targetNumberOfRelays)
        {// When making a relay connection, it is necessary to specify the appropriate number of relays (the outermost layer of relays).
            return null;
        }

        // Create a new encryption key
        var seedKey = SeedKey.NewEncryption();
        var publicKey = seedKey.GetEncryptionPublicKey();

        // Create a new connection
        var packet = new ConnectPacket(publicKey, node.PublicKey.GetHashCode(), default);
        var t = await this.packetTerminal.SendAndReceive<ConnectPacket, ConnectPacketResponse>(node.Address, packet, targetNumberOfRelays, default, EndpointResolution.PreferIpv6, incomingRelay).ConfigureAwait(false); // < 0: target
        if (t.Value is null)
        {
            return default;
        }

        var newConnection = this.PrepareClientSide(node, endPoint, seedKey, node.PublicKey, packet, t.Value);
        newConnection.MinimumNumberOfRelays = targetNumberOfRelays;
        newConnection.AddRtt(t.RttMics);
        using (this.clientConnections.LockObject.EnterScope())
        {// ConnectionStateCode
            newConnection.SetOpenCount(2); // Set to 2 to prevent immediate disposal.
            newConnection.Goshujin = this.clientConnections;
        }

        return newConnection;
    }

    public async Task<ClientConnection?> Connect(NetNode node, Connection.ConnectMode mode = Connection.ConnectMode.ReuseIfAvailable, int minimumNumberOfRelays = 0, EndpointResolution endpointResolution = EndpointResolution.PreferIpv6)
    {
        if (!this.NetTerminal.IsActive)
        {
            return null;
        }

#if EnableOpenSesami == true
        if (node.Address.RelayId != 0)
        {// Open sesami
            var r1 = await this.packetTerminal.SendAndReceive<OpenSesamiPacket, OpenSesamiResponse>(node.Address, new()).ConfigureAwait(false);
            if (r1.Value is { } r2 && r2.SecretAddress.Validate())
            {
                node = new(r2.SecretAddress, node.PublicKey);
            }
        }
#endif

        var address = node.Address;
        if (!this.netStats.TryCreateEndpoint(ref address, endpointResolution, out var endPoint))
        {
            return null;
        }

        if (minimumNumberOfRelays < this.NetTerminal.MinimumNumberOfRelays)
        {
            minimumNumberOfRelays = this.NetTerminal.MinimumNumberOfRelays;
        }

        var seedKey = this.NetTerminal.NodeSeedKey;
        var publicKey = this.NetTerminal.NodePublicKey;
        if (minimumNumberOfRelays > 0)
        {
            // mode = Connection.ConnectMode.NoReuse; // Do not reuse connections.
            seedKey = SeedKey.NewEncryption(); // Do not reuse node encryption keys.
            publicKey = seedKey.GetEncryptionPublicKey();
        }

        using (this.clientConnections.LockObject.EnterScope())
        {
            if (mode == Connection.ConnectMode.ReuseIfAvailable ||
                mode == Connection.ConnectMode.ReuseOnly)
            {// Attempts to reuse a connection that has already been connected or disconnected (but not yet disposed).
                if (this.clientConnections.DestinationEndpointChain.TryGetValue(endPoint, out var connection))
                {
                    if (connection.MinimumNumberOfRelays >= minimumNumberOfRelays)
                    {
                        Debug.Assert(!connection.IsDisposed);
                        connection.IncrementOpenCount();
                        connection.ChangeStateInternal(Connection.State.Open);
                        return connection;
                    }
                }

                if (mode == Connection.ConnectMode.ReuseOnly)
                {
                    return default;
                }
            }
        }

        // Create a new connection
        var sourceNetNode = this.netStats.OwnNetNode?.Address.IsValidIpv4AndIpv6 == true ? this.netStats.OwnNetNode : default;
        var packet = new ConnectPacket(publicKey, node.PublicKey.GetHashCode(), sourceNetNode);
        var t = await this.packetTerminal.SendAndReceive<ConnectPacket, ConnectPacketResponse>(node.Address, packet, minimumNumberOfRelays, default).ConfigureAwait(false);
        var response = t.Value;
        if (response is null)
        {
            return default;
        }

        var ownEndpoint = response.SourceEndpoint;
        if (ownEndpoint.IsValid)
        {
            this.netStats.ReportEndpoint(ownEndpoint.EndPoint);
        }

        var newConnection = this.PrepareClientSide(node, endPoint, seedKey, node.PublicKey, packet, response);
        newConnection.MinimumNumberOfRelays = minimumNumberOfRelays;
        newConnection.AddRtt(t.RttMics);
        using (this.clientConnections.LockObject.EnterScope())
        {// ConnectionStateCode
            newConnection.IncrementOpenCount();
            newConnection.Goshujin = this.clientConnections;
        }

        return newConnection;
    }

    internal void CloseRelayedConnections()
    {
        TemporaryList<ClientConnection> list = default;
        using (this.clientConnections.LockObject.EnterScope())
        {
            foreach (var x in this.clientConnections)
            {
                if (x.MinimumNumberOfRelays > 0)
                {
                    list.Add(x);
                }
            }
        }

        foreach (var x in list)
        {
            x.TerminateInternal();
        }

        using (this.clientConnections.LockObject.EnterScope())
        {
            foreach (var x in list)
            {
                if (x.IsOpen)
                {
                    x.ChangeStateInternal(Connection.State.Closed);
                }

                if (x.IsClosed)
                {
                    x.ChangeStateInternal(Connection.State.Disposed);
                }

                x.Goshujin = null;
            }
        }
    }

    internal ClientConnection PrepareBidirectionalConnection(ServerConnection serverConnection)
    {
        using (this.clientConnections.LockObject.EnterScope())
        {
            if (this.clientConnections.ConnectionIdChain.TryGetValue(serverConnection.ConnectionId, out var connection))
            {
                connection.IncrementOpenCount();
                connection.ChangeStateInternal(Connection.State.Open);
            }
            else
            {
                connection = new ClientConnection(serverConnection);
                connection.Goshujin = this.clientConnections;
            }

            serverConnection.BidirectionalConnection = connection;
            return connection;
        }
    }

    internal ServerConnection PrepareBidirectionalConnection(ClientConnection clientConnection)
    {
        using (this.serverConnections.LockObject.EnterScope())
        {
            if (this.serverConnections.ConnectionIdChain.TryGetValue(clientConnection.ConnectionId, out var connection))
            {
                connection.ChangeStateInternal(Connection.State.Open);
            }
            else
            {
                connection = new ServerConnection(clientConnection);
                connection.Goshujin = this.serverConnections;
            }

            clientConnection.BidirectionalConnection = connection;
            return connection;
        }
    }

    internal ClientConnection PrepareClientSide(NetNode node, NetEndpoint endPoint, SeedKey clientSeedKey, EncryptionPublicKey serverPublicKey, ConnectPacket p, ConnectPacketResponse p2)
    {
        Span<byte> material = stackalloc byte[CryptoBox.KeyMaterialSize];
        clientSeedKey.DeriveKeyMaterial(serverPublicKey, material);

        // CreateEmbryo: Blake2B(Client salt(8), Server salt(8), Key material(32), Client public(32), Server public(32))
        var embryo = new byte[Connection.EmbryoSize];
        Span<byte> buffer = stackalloc byte[8 + 8 + CryptoBox.KeyMaterialSize + CryptoBox.PublicKeySize + CryptoBox.PublicKeySize]; // Client salt(8), Server salt(8), Key material(32), Client public(32), Server public(32)
        var span = buffer;
        BitConverter.TryWriteBytes(span, p.ClientSalt);
        span = span.Slice(sizeof(ulong));
        BitConverter.TryWriteBytes(span, p2.ServerSalt);
        span = span.Slice(sizeof(ulong));
        material.CopyTo(span);
        span = span.Slice(CryptoBox.KeyMaterialSize);
        clientSeedKey.GetEncryptionPublicKeySpan().CopyTo(span);
        span = span.Slice(CryptoBox.PublicKeySize);
        serverPublicKey.AsSpan().CopyTo(span);
        span = span.Slice(CryptoBox.PublicKeySize);
        Blake2B.Get512_Span(buffer, embryo);

        var connectionId = BitConverter.ToUInt64(embryo.AsSpan(0));
        var connection = new ClientConnection(this.NetTerminal.PacketTerminal, this, connectionId, node, endPoint);
        connection.Initialize(p2.Agreement, embryo);

        return connection;
    }

    internal bool PrepareServerSide(NetEndpoint endPoint, ConnectPacket p, ConnectPacketResponse p2, int relayNumber)
    {
        var node = new NetNode(in endPoint, p.ClientPublicKey);
        Span<byte> material = stackalloc byte[CryptoBox.KeyMaterialSize];
        this.NetTerminal.NodeSeedKey.DeriveKeyMaterial(p.ClientPublicKey, material);

        // CreateEmbryo: Blake2B(Client salt(8), Server salt(8), Key material(32), Client public(32), Server public(32))
        var embryo = new byte[Connection.EmbryoSize];
        Span<byte> buffer = stackalloc byte[8 + 8 + CryptoBox.KeyMaterialSize + CryptoBox.PublicKeySize + CryptoBox.PublicKeySize];
        var span = buffer;
        BitConverter.TryWriteBytes(span, p.ClientSalt);
        span = span.Slice(sizeof(ulong));
        BitConverter.TryWriteBytes(span, p2.ServerSalt);
        span = span.Slice(sizeof(ulong));
        material.CopyTo(span);
        span = span.Slice(CryptoBox.KeyMaterialSize);
        p.ClientPublicKey.AsSpan().CopyTo(span);
        span = span.Slice(CryptoBox.PublicKeySize);
        this.NetTerminal.NodeSeedKey.GetEncryptionPublicKeySpan().CopyTo(span);
        span = span.Slice(CryptoBox.PublicKeySize);
        Blake2B.Get512_Span(buffer, embryo);

        var connectionId = BitConverter.ToUInt64(embryo.AsSpan(0));
        var connection = new ServerConnection(this.NetTerminal.PacketTerminal, this, connectionId, node, endPoint);
        connection.Initialize(p2.Agreement, embryo);
        connection.MinimumNumberOfRelays = relayNumber;

        using (this.serverConnections.LockObject.EnterScope())
        {// ConnectionStateCode
            connection.Goshujin = this.serverConnections;
        }

        if (!this.netStats.NodeControl.HasSufficientActiveNodes &&
            p.SourceNode is { } sourceNode)
        {
            this.netStats.NodeControl.TryAddActiveNode(sourceNode);
        }

        /*if (this.netStats.NodeControl.RestorationNode is null)
        {
            this.netStats.NodeControl.RestorationNode = node;
        }*/

        return true;
    }

    internal void CloseInternal(Connection connection, bool sendCloseFrame)
    {
        connection.CloseSendTransmission();

        if (connection is ClientConnection clientConnection &&
            clientConnection.Goshujin is { } g)
        {
            ServerConnection? bidirectionalConnection;
            using (g.LockObject.EnterScope())
            {
                clientConnection.SetOpenCount(0);
                if (connection.CurrentState == Connection.State.Open)
                {// Open -> Close
                    connection.Logger.TryGet(LogLevel.Debug)?.Log($"{connection.ConnectionIdText} Open -> Closed, SendCloseFrame {sendCloseFrame}");

                    if (sendCloseFrame)
                    {
                        clientConnection.SendCloseFrame();
                    }

                    clientConnection.ChangeStateInternal(Connection.State.Closed);
                }

                bidirectionalConnection = clientConnection.BidirectionalConnection;
                if (bidirectionalConnection is not null)
                {
                    clientConnection.BidirectionalConnection = default;
                    bidirectionalConnection.BidirectionalConnection = default;
                }
            }

            if (bidirectionalConnection is not null)
            {
                this.CloseInternal(bidirectionalConnection, sendCloseFrame);
            }
        }
        else if (connection is ServerConnection serverConnection &&
            serverConnection.Goshujin is { } g2)
        {
            ClientConnection? bidirectionalConnection;
            using (g2.LockObject.EnterScope())
            {
                if (connection.CurrentState == Connection.State.Open)
                {// Open -> Close
                    connection.Logger.TryGet(LogLevel.Debug)?.Log($"{connection.ConnectionIdText} Open -> Closed, SendCloseFrame {sendCloseFrame}");

                    if (sendCloseFrame)
                    {
                        serverConnection.SendCloseFrame();
                    }

                    serverConnection.ChangeStateInternal(Connection.State.Closed);
                }

                bidirectionalConnection = serverConnection.BidirectionalConnection;
                if (bidirectionalConnection is not null)
                {
                    serverConnection.BidirectionalConnection = default;
                    bidirectionalConnection.BidirectionalConnection = default;
                }
            }

            if (bidirectionalConnection is not null)
            {
                this.CloseInternal(bidirectionalConnection, sendCloseFrame);
            }
        }
    }

    internal void ProcessSend(NetSender netSender)
    {
        // CongestionControl
        lock (this.CongestionControlList)
        {
            var currentMics = Mics.FastSystem;
            var elapsedMics = this.lastCongestionControlMics == 0 ? 0 : currentMics - this.lastCongestionControlMics;
            this.lastCongestionControlMics = currentMics;
            var elapsedMilliseconds = elapsedMics * 0.001d;

            var congestionControlNode = this.CongestionControlList.First;
            while (congestionControlNode is not null)
            {
                if (!congestionControlNode.Value.Process(netSender, elapsedMics, elapsedMilliseconds))
                {
                    this.CongestionControlList.Remove(congestionControlNode);
                }

                congestionControlNode = congestionControlNode.Next;
            }
        }

        using (this.LockSend.EnterScope())
        {
            // CongestedList: Move to SendList when congestion is resolved.
            var currentNode = this.CongestedList.Last; // To maintain order in SendList, process from the last node.
            while (currentNode is not null)
            {
                var previousNode = currentNode.Previous;

                var connection = currentNode.Value;
                if (connection.CongestionControl is null ||
                    !connection.CongestionControl.IsCongested)
                {// No congestion control or not congested
                    this.CongestedList.Remove(currentNode);
                    this.SendList.AddFirst(currentNode);
                }

                currentNode = previousNode;
            }

            // SendList: For fairness, send packets one at a time
            while (this.SendList.First is { } node)
            {
                if (!netSender.CanSend)
                {
                    return;
                }

                var connection = node.Value;
                var result = connection.ProcessSingleSend(netSender);
                if (result == ProcessSendResult.Complete)
                {// Delete the node if there is no transmission to send.
                    this.SendList.Remove(node);
                    connection.SendNode = null;
                }
                else if (result == ProcessSendResult.Remaining)
                {// If there are remaining packets, move it to the end.
                    this.SendList.MoveToLast(node);
                }
                else
                {// If in a congested state, move it to the CongestedList.
                    this.SendList.Remove(node);
                    this.CongestedList.AddFirst(node);
                }
            }
        }
    }

    internal void ProcessReceive(NetEndpoint endpoint, bool outgoingRelay, ushort packetUInt16, BytePool.RentMemory toBeShared, long currentSystemMics)
    {// Checked: toBeShared.Length
        // PacketHeaderCode
        var connectionId = BitConverter.ToUInt64(toBeShared.Span.Slice(RelayHeader.RelayIdLength + 6)); // ConnectionId
        if (NetConstants.LogLowLevelNet)
        {
            // this.logger.TryGet(LogLevel.Debug)?.Log($"{(ushort)connectionId:x4} Receive actual");
        }

        if (packetUInt16 < 384)
        {// Client -> Server
            if (outgoingRelay)
            {
                return;
            }

            ServerConnection? connection = default;
            using (this.serverConnections.LockObject.EnterScope())
            {
                this.serverConnections.ConnectionIdChain.TryGetValue(connectionId, out connection);

                if (connection?.CurrentState == Connection.State.Closed)
                {// Reopen (Closed -> Open)
                    connection.ChangeStateInternal(Connection.State.Open);
                }
            }

            if (connection is not null &&
                connection.DestinationEndpoint.Equals(endpoint))
            {
                connection.ProcessReceive(endpoint, toBeShared, currentSystemMics);
            }
        }
        else
        {// Server -> Client (Response)
            ClientConnection? connection = default;
            using (this.clientConnections.LockObject.EnterScope())
            {
                this.clientConnections.ConnectionIdChain.TryGetValue(connectionId, out connection);
            }

            if (connection is not null &&
                connection.DestinationEndpoint.Equals(endpoint))
            {
                connection.ProcessReceive(endpoint, toBeShared, currentSystemMics);
            }
        }
    }

    internal void SetReceiveTransmissionGapForTest(uint gap)
    {
        this.ReceiveTransmissionGap = gap;
    }

    internal async Task Terminate(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ClientConnection[] clients;
            using (this.clientConnections.LockObject.EnterScope())
            {
                clients = this.clientConnections.ToArray();
            }

            foreach (var x in clients)
            {
                x.TerminateInternal();
            }

            using (this.clientConnections.LockObject.EnterScope())
            {
                foreach (var x in clients)
                {
                    if (x.IsEmpty)
                    {
                        if (x.IsOpen)
                        {
                            x.ChangeStateInternal(Connection.State.Closed);
                        }

                        if (x.IsClosed)
                        {
                            x.ChangeStateInternal(Connection.State.Disposed);
                        }

                        x.Goshujin = null;
                    }
                }
            }

            ServerConnection[] servers;
            using (this.serverConnections.LockObject.EnterScope())
            {
                servers = this.serverConnections.ToArray();
            }

            foreach (var x in servers)
            {
                x.TerminateInternal();
            }

            using (this.serverConnections.LockObject.EnterScope())
            {
                foreach (var x in servers)
                {
                    if (x.IsEmpty)
                    {
                        if (x.IsOpen)
                        {
                            x.ChangeStateInternal(Connection.State.Closed);
                        }

                        if (x.IsClosed)
                        {
                            x.ChangeStateInternal(Connection.State.Disposed);
                        }

                        x.Goshujin = null;
                    }
                }
            }

            if (this.clientConnections.Count == 0 &&
                this.serverConnections.Count == 0)
            {
                return;
            }
            else
            {
                try
                {
                    await Task.Delay(NetConstants.TerminateTerminalDelayMilliseconds, cancellationToken);
                }
                catch
                {
                    return;
                }
            }
        }
    }
}

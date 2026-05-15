// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Netsphere.Packet;

namespace Netsphere;

[ValueLinkObject(Isolation = IsolationLevel.Serializable, Restricted = true)]
public sealed partial class ServerConnection : Connection, IEquatable<ServerConnection>, IComparable<ServerConnection>
{
    #region FieldAndProperty

    public override bool IsClient => false;

    public override bool IsServer => true;

    public ClientConnection? BidirectionalConnection { get; internal set; } // lock (this.ConnectionTerminal.clientConnections.SyncObject)

    internal ushort InnerRelayId { get; set; }

    private ServerConnectionContext context;

    #endregion

    [Link(Primary = true, Type = ChainType.Unordered, TargetMember = "ConnectionId")]
    [Link(Type = ChainType.Unordered, Name = "DestinationEndpoint", TargetMember = "DestinationEndpoint")]
    internal ServerConnection(PacketTerminal packetTerminal, ConnectionTerminal connectionTerminal, ulong connectionId, NetNode node, NetEndpoint endPoint)
        : base(packetTerminal, connectionTerminal, connectionId, node, endPoint)
    {// CreateServerConnection (Client->Server)
        this.context = this.NetBase.NewServerConnectionContext(this);
    }

    internal ServerConnection(ClientConnection clientConnection)
        : base(clientConnection)
    {// CreateServerConnection (Bidirectional)
        this.BidirectionalConnection = clientConnection;
        this.context = this.NetBase.NewServerConnectionContext(this);
    }

    public ServerConnectionContext GetContext()
        => this.context;

    public TContext GetContext<TContext>()
        where TContext : ServerConnectionContext
        => (TContext)this.context;

    public ClientConnection PrepareBidirectionalConnection()
    {
        if (this.BidirectionalConnection is { } connection)
        {
            return connection;
        }
        else
        {
            return this.ConnectionTerminal.PrepareBidirectionalConnection(this);
        }
    }

    public bool Equals(ServerConnection? other)
    {
        if (other is null)
        {
            return false;
        }

        return this.ConnectionId == other.ConnectionId;
    }

    public override int GetHashCode()
        => (int)this.ConnectionId;

    public int CompareTo(ServerConnection? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (this.ConnectionId < other.ConnectionId)
        {
            return -1;
        }
        else if (this.ConnectionId > other.ConnectionId)
        {
            return 1;
        }
        else
        {
            return 0;
        }
    }

    public void Close()
        => this.Dispose();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal override void UpdateLastEventMics()
    {
        var mics = Mics.FastSystem;
        this.LastEventMics = mics;
        this.BidirectionalConnection?.LastEventMics = mics;
    }

    internal override void OnStateChanged()
    {
        if (this.CurrentState == State.Disposed)
        {// Disposed
            this.context.DisposeActual();
        }
    }
}

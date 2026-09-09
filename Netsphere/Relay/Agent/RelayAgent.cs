// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Netsphere.Core;
using Netsphere.Stats;

namespace Netsphere.Relay;

/// <summary>
/// Allocates relays and forwards packets on a relay server.
/// </summary>
public partial class RelayAgent
{
    private const long CleanIntervalMics = 10_000_000; // 10 seconds
    private const long UnrestrictedRetensionMics = 60_000_000; // 1 minute
    private const int EndPointCacheSize = 100;

    internal enum EndpointOperation
    {
        None,
        Update,
        SetUnrestricted,
        SetRestricted,
    }

    [ValueLinkObject]
    private partial class EndPointItem
    {
        [Link(Primary = true, Name = "LinkedList", Type = ChainType.LinkedList)]
        public EndPointItem(NetAddress netAddress, IPEndPoint? endPoint)
        {
            this.NetAddress = netAddress;
            this.EndPoint = endPoint;
        }

        [Link(Type = ChainType.Unordered, AddValue = false)]
        public NetAddress NetAddress { get; }

        public IPEndPoint? EndPoint { get; }

        public long UnrestrictedMics { get; internal set; }

        public bool IsUnrestricted
        {
            get
            {
                if (this.UnrestrictedMics == 0)
                {
                    return false;
                }
                else if (Mics.FastSystem - this.UnrestrictedMics > UnrestrictedRetensionMics)
                {
                    this.UnrestrictedMics = 0;
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }
    }

    internal RelayAgent(IRelayControl relayControl, NetTerminal netTerminal)
    {
        this.relayControl = relayControl;
        this.netTerminal = netTerminal;
        this.logger = this.netTerminal.LogUnit.RootLogService.GetLogger<RelayAgent>();
    }

    #region FieldAndProperty

    public int NumberOfExchanges
    {
        get
        {
            using (this.items.LockObject.EnterScope())
            {
                return this.items.Count;
            }
        }
    }

    private readonly ILogger logger;
    private readonly IRelayControl relayControl;
    private readonly NetTerminal netTerminal;

    private readonly RelayExchange.GoshujinClass items = new();

    private readonly EndPointItem.GoshujinClass endPointCache = new();
    private readonly ConcurrentQueue<NetSender.Item> sendItems = new();

    private long lastCleanMics;
    private long lastRestrictedMics;
    private bool stopped;

    #endregion

    public override string ToString()
    {
        var sb = new StringBuilder();
        using (this.items.LockObject.EnterScope())
        {
            foreach (var x in this.items)
            {
                sb.AppendLine($"[{x.InnerRelayId}]{x.ServerConnection.DestinationEndpoint} - [{x.OuterRelayId}]{x.OuterEndpoint}");
            }
        }

        return sb.ToString();
    }

    public RelayResult AddExchange(ServerConnection serverConnection, AssignRelayBlock block, out RelayId innerRelayId, out RelayId outerRelayId)
    {
        innerRelayId = 0;
        outerRelayId = 0;
        if (block.InnerKeyAndNonce is not { Length: Aegis128L.KeySize + Aegis128L.NonceSize })
        {
            return RelayResult.InvalidEndpoint;
        }

        using (this.items.LockObject.EnterScope())
        {
            if (this.stopped)
            {
                return RelayResult.ConnectionFailure;
            }

            if (this.items.Count >= this.relayControl.MaxRelayExchanges)
            {
                return RelayResult.RelayExchangeLimit;
            }

            while (true)
            {
                innerRelayId = (RelayId)RandomVault.Default.NextUInt32();
                if (innerRelayId != 0 && !this.items.RelayIdChain.ContainsKey(innerRelayId))
                {
                    break;
                }
            }

            while (true)
            {
                outerRelayId = (RelayId)RandomVault.Default.NextUInt32();
                if (outerRelayId != 0 && outerRelayId != innerRelayId && !this.items.RelayIdChain.ContainsKey(outerRelayId))
                {
                    break;
                }
            }

            this.items.Add(new(this.relayControl, innerRelayId, outerRelayId, serverConnection, block));
        }

        return RelayResult.Success;
    }

    public long AddRelayPoint(RelayId relayId, long relayPoint)
    {
        using (this.items.LockObject.EnterScope())
        {
            var exchange = this.items.RelayIdChain.FindFirst(relayId);
            if (exchange is null)
            {
                return 0;
            }

            var prev = exchange.RelayPoint;
            if (relayPoint >= 0)
            {
                exchange.RelayPoint += Math.Min(relayPoint, Math.Max(0, this.relayControl.DefaultMaxRelayPoint - prev));
            }
            else
            {
                exchange.RelayPoint -= relayPoint == long.MinValue ? prev : Math.Min(prev, -relayPoint);
            }

            return exchange.RelayPoint - prev;
        }
    }

    public void Clean()
    {
        if (Mics.FastSystem - this.lastCleanMics < CleanIntervalMics)
        {
            return;
        }

        using (this.items.LockObject.EnterScope())
        {
            if (Mics.FastSystem - this.lastCleanMics < CleanIntervalMics)
            {
                return;
            }

            this.lastCleanMics = Mics.FastSystem;
            TemporaryList<RelayExchange> deleteList = default;
            foreach (var x in this.items)
            {
                if (this.lastCleanMics - x.LastAccessMics > x.RelayRetensionMics ||
                    !x.ServerConnection.IsOpen)
                {
                    deleteList.Add(x);
                }
            }

            foreach (var x in deleteList)
            {
                if (NetConstants.LogRelay)
                {
                    this.logger.GetWriter(LogLevel.Information)?.Write($"Removed (Clean) {x.ToString()}");
                }

                x.Remove();
            }
        }
    }

    internal void ProcessSetupRelay(TransmissionContext transmissionContext)
    {
        if (!TinyhandSerializer.TryDeserialize<SetupRelayBlock>(transmissionContext.RentMemory.Memory.Span, out var t))
        {
            transmissionContext.Return();
            transmissionContext.SendResultAndForget(NetResult.DeserializationFailed);
            return;
        }

        transmissionContext.Return();

        if (!t.OuterEndpoint.IsValid || t.OuterEndpoint.RelayId == 0 ||
            t.OuterKeyAndNonce is not { Length: Aegis128L.KeySize + Aegis128L.NonceSize })
        {
            transmissionContext.SendAndForget(new SetupRelayResponse(RelayResult.InvalidEndpoint), SetupRelayBlock.DataId);
            return;
        }

        var serverConnection = transmissionContext.ServerConnection;
        if (serverConnection.InnerRelayId == 0)
        {
            transmissionContext.SendAndForget(new SetupRelayResponse(RelayResult.InvalidEndpoint), SetupRelayBlock.DataId);
            return;
        }

        using (this.items.LockObject.EnterScope())
        {
            var exchange = this.items.RelayIdChain.FindFirst(serverConnection.InnerRelayId);
            if (exchange is null)
            {
                transmissionContext.SendAndForget(new SetupRelayResponse(RelayResult.InvalidEndpoint), SetupRelayBlock.DataId);
                return;
            }

            exchange.OuterEndpoint = t.OuterEndpoint;
            exchange.OuterKeyAndNonce = t.OuterKeyAndNonce;
            transmissionContext.SendAndForget(new SetupRelayResponse(RelayResult.Success), SetupRelayBlock.DataId);
        }
    }

    internal bool ProcessRelay(NetEndpoint endpoint, RelayId destinationRelayId, BytePool.RentMemory source, out BytePool.RentMemory decrypted)
    {// This is all the code that performs the actual relay processing.
        decrypted = default;
        if (source.RentArray is null || source.Length < Packet.PacketHeader.Length || source.Length > NetConstants.MaxPacketLength)
        {// Invalid data
            return false;
        }

        // Setup, cleanup, IPv4 and IPv6 reception share exchange and endpoint state.
        using var scope = this.items.LockObject.EnterScope();
        if (this.stopped)
        {
            return false;
        }

        var span = source.Span.Slice(RelayHeader.RelayIdLength);
        var exchange = this.items.RelayIdChain.FindFirst(destinationRelayId);
        if (exchange is null)
        {
            goto Exit;
        }

        var serverConnection = exchange.ServerConnection;
        if (exchange.InnerRelayId == destinationRelayId)
        {// InnerRelayId: Inner(Originator or Inner relay) -> Self or Outer relay
            if (!serverConnection.DestinationEndpoint.EndPointEquals(endpoint))
            {// Despite coming from the inner node, the endpoint does not match.
                if (NetConstants.LogLowRelay)
                {
                    this.logger.GetWriter(LogLevel.Information)?.Write($"Inner({endpoint}) : invalid endpoint");
                }

                goto Exit;
            }

            // Inner Decrypt (RelayTagCode): source=Relay source id(2), destination id(2), salt(4), Data, Tag(16)
            if (!RelayHelper.TryDecrypt(exchange.InnerKeyAndNonce, ref source, out span))
            {
                if (NetConstants.LogLowRelay)
                {
                    this.logger.GetWriter(LogLevel.Information)?.Write($"Inner({endpoint}) : decryption failue2");
                }

                goto Exit;
            }

            // Since it comes from the inner, it is a packet that starts with RelayHeader.
            if (span.Length < RelayHeader.Length)
            {// Invalid data
                goto Exit;
            }

            // Decrypt
            var salt4 = MemoryMarshal.Read<uint>(span);
            var headerSpan = span;
            span = span.Slice(sizeof(uint));

            Span<byte> nonce32 = stackalloc byte[32];
            RelayHelper.CreateNonce(salt4, serverConnection.EmbryoSalt, serverConnection.EmbryoSecret, nonce32);
            if (!Aegis256.TryDecrypt(span, span, nonce32, serverConnection.EmbryoKey, default, 0))
            {
                if (NetConstants.LogLowRelay)
                {
                    this.logger.GetWriter(LogLevel.Information)?.Write($"Inner({endpoint}) : decryption failue");
                }

                goto Exit;
            }

            var relayHeader = MemoryMarshal.Read<RelayHeader>(headerSpan);
            if (relayHeader.Zero == 0)
            { // Decrypted. Process the packet on this node.
                if (source.Length < RelayHeader.Length + Packet.PacketHeader.Length)
                {
                    goto Exit;
                }

                span = span.Slice(RelayHeader.Length - RelayHeader.PlainLength - RelayHeader.RelayIdLength);
                decrypted = source.Slice(RelayHeader.Length);
                // decrypted = source.RentArray.AsMemory(RelayHeader.Length, span.Length);
                if (relayHeader.NetAddress == NetAddress.Relay)
                {// Initiator -> This node
                    if (!exchange.DecrementAndCheck())
                    {
                        goto Exit;
                    }

                    MemoryMarshal.Write(span, endpoint.RelayId); // SourceRelayId
                    span = span.Slice(sizeof(RelayId));
                    MemoryMarshal.Write(span, destinationRelayId); // DestinationRelayId

                    if (NetConstants.LogLowRelay)
                    {
                        this.logger.GetWriter(LogLevel.Information)?.Write($"Inner({endpoint}) -> this({destinationRelayId}) Self");
                    }

                    return true;
                }
                else
                {// Initiator -> Other (unrestricted)
                    if (exchange.OuterEndpoint.IsValid)
                    {// Inner relay
                        if (NetConstants.LogLowRelay)
                        {
                            this.logger.GetWriter(LogLevel.Information)?.Write($"Inner({endpoint}) -> this({destinationRelayId}): OuterEndpoint is already set");
                        }

                        goto Exit;
                    }

                    MemoryMarshal.Write(span, exchange.OuterRelayId); // SourceRelayId
                    span = span.Slice(sizeof(RelayId));
                    MemoryMarshal.Write(span, relayHeader.NetAddress.RelayId); // DestinationRelayId

                    var operation = EndpointOperation.Update;
                    var packetType = MemoryMarshal.Read<Netsphere.Packet.PacketType>(span.Slice(sizeof(RelayId) + sizeof(uint)));
                    if (packetType == Packet.PacketType.Connect)
                    {// Connect
                        operation = EndpointOperation.SetUnrestricted;
                    }
                    else
                    {// Other
                        operation = EndpointOperation.Update;
                    }

                    // Close -> EndPointOperation.SetRestricted ?

                    var ep2 = this.GetEndPointCore(relayHeader.NetAddress, operation);
                    if (ep2.EndPoint is not null && exchange.DecrementAndCheck())
                    {
                        decrypted.IncrementAndShare();
                        this.sendItems.Enqueue(new(ep2.EndPoint, decrypted));
                    }

                    if (NetConstants.LogLowRelay)
                    {
                        this.logger.GetWriter(LogLevel.Information)?.Write($"Inner({endpoint}) -> this({destinationRelayId}) -> {relayHeader.NetAddress}: {decrypted.Memory.Length} bytes {exchange.OuterRelayId}->{relayHeader.NetAddress.RelayId}");
                    }
                }
            }
            else
            {// Not decrypted. Relay the packet to the next node.
                if (exchange.OuterEndpoint.EndPoint is { } ep)
                {// -> Outer relay
                    if (!exchange.DecrementAndCheck())
                    {
                        goto Exit;
                    }

                    // Outer Encrypt (RelayTagCode)
                    RelayHelper.Encrypt(exchange.OuterKeyAndNonce, ref source);

                    MemoryMarshal.Write(source.Span, exchange.OuterRelayId);
                    MemoryMarshal.Write(source.Span.Slice(sizeof(RelayId)), exchange.OuterEndpoint.RelayId);
                    source.IncrementAndShare();
                    this.sendItems.Enqueue(new(ep, source));

                    if (NetConstants.LogLowRelay)
                    {
                        this.logger.GetWriter(LogLevel.Information)?.Write($"Inner({endpoint}) -> this({destinationRelayId}) -> Outer({exchange.OuterEndpoint}): {source.Memory.Length} bytes");
                    }
                }
                else
                {// No outer relay. Discard
                    if (NetConstants.LogLowRelay)
                    {
                        this.logger.GetWriter(LogLevel.Information)?.Write($"Inner({endpoint}) -> relay: No outer relay");
                    }
                }
            }
        }
        else
        {// OuterRelayId: Outer(Outer relay or unrestricted) -> Inner relay
            if (exchange.OuterEndpoint.IsValid)
            {// Not outermost relay
                if (exchange.OuterEndpoint.EndPointEquals(endpoint))
                {// Outer relay -> Inner: Encrypt
                    // Outer Decrypt (RelayTagCode)
                    if (MemoryMarshal.Read<RelayId>(source.Span) != 0 &&
                        !RelayHelper.TryDecrypt(exchange.OuterKeyAndNonce, ref source, out span))
                    {
                        if (NetConstants.LogLowRelay)
                        {
                            this.logger.GetWriter(LogLevel.Information)?.Write($"Outer({endpoint}) : decryption failue2");
                        }

                        goto Exit;
                    }
                }
                else
                {// Other (unrestricted or restricted)
                    if (NetConstants.LogLowRelay)
                    {// Packets from endpoints other than the outer relay are not accepted.
                        this.logger.GetWriter(LogLevel.Information)?.Write($"Outer({endpoint}) : not accepted");
                    }

                    goto Exit;
                }
            }
            else
            {// Outermost relay
                // Other (unrestricted or restricted)
                var ep2 = this.GetEndPointCore(new(endpoint), EndpointOperation.None);
                if (!ep2.Unrestricted)
                {// Restricted
                    if (exchange.AllowUnknownIncoming &&
                       exchange.RestrictedIntervalMics != 0 &&
                       Mics.FastSystem > this.lastRestrictedMics + exchange.RestrictedIntervalMics)
                    {// Unknown incoming
                        goto AcceptIncoming;
                    }

                    if (exchange.AllowOpenSesami)
                    {// Open sesami
                        var packetType = MemoryMarshal.Read<Netsphere.Packet.PacketType>(span.Slice(sizeof(uint)));
                        if (packetType == Packet.PacketType.OpenSesami)
                        {
                            goto AcceptIncoming;
                        }
                    }

                    // Discard
                    if (NetConstants.LogLowRelay)
                    {// Packets from endpoints other than the outer relay are not accepted.
                        this.logger.GetWriter(LogLevel.Information)?.Write($"Outermost({endpoint}) : discard");
                    }

                    goto Exit;

AcceptIncoming:
                    this.lastRestrictedMics = Mics.FastSystem;
                }
            }

            var sourceRelayId = MemoryMarshal.Read<RelayId>(source.Span);
            if (sourceRelayId == 0)
            {// RelayId(Source/Destination), RelayHeader, Content(span)
                if (source.Length > NetConstants.MaxPacketLength - RelayHeader.Length - Aegis128L.MinTagSize ||
                    source.RentArray!.Array.Length < source.Length + RelayHeader.Length + Aegis128L.MinTagSize)
                {
                    goto Exit;
                }

                var sourceSpan = source.RentArray!.Array.AsSpan(RelayHeader.RelayIdLength);
                span.CopyTo(sourceSpan.Slice(RelayHeader.Length));

                var contentLength = span.Length;

                // RelayHeader
                var relayHeader = new RelayHeader(RandomVault.Default.NextUInt32(), new(endpoint));
                MemoryMarshal.Write(sourceSpan, relayHeader);
                sourceSpan = sourceSpan.Slice(RelayHeader.Length);

                sourceSpan = sourceSpan.Slice(contentLength);

                source = source.RentArray.AsMemory(0, RelayHeader.RelayIdLength + RelayHeader.Length + contentLength);
                span = source.Span.Slice(RelayHeader.RelayIdLength);
            }

            // Encrypt
            var salt4 = MemoryMarshal.Read<uint>(span);
            span = span.Slice(sizeof(uint));
            Span<byte> nonce32 = stackalloc byte[32];
            RelayHelper.CreateNonce(salt4, serverConnection.EmbryoSalt, serverConnection.EmbryoSecret, nonce32);
            Aegis256.Encrypt(span, span, nonce32, serverConnection.EmbryoKey, default, 0);

            // Inner Encrypt (RelayTagCode)
            RelayHelper.Encrypt(exchange.InnerKeyAndNonce, ref source);

            if (serverConnection.DestinationEndpoint.EndPoint is { } ep)
            {
                if (!exchange.DecrementAndCheck())
                {
                    goto Exit;
                }

                MemoryMarshal.Write(source.Span, exchange.InnerRelayId); // SourceRelayId
                MemoryMarshal.Write(source.Span.Slice(sizeof(RelayId)), serverConnection.DestinationEndpoint.RelayId); // DestinationRelayId
                source.IncrementAndShare();
                this.sendItems.Enqueue(new(ep, source));

                if (NetConstants.LogLowRelay)
                {// Packets from endpoints other than the outer relay are not accepted.
                    this.logger.GetWriter(LogLevel.Information)?.Write($"Outer({endpoint}) -> this({destinationRelayId}) -> {serverConnection.DestinationEndpoint} : {source.Memory.Length} bytes");
                }
            }
        }

Exit:
        decrypted = default;
        return false;
    }

    internal void Stop()
    {
        using (this.items.LockObject.EnterScope())
        {
            this.stopped = true;
            this.items.ClearAll();
            this.endPointCache.ClearAll();
            while (this.sendItems.TryDequeue(out var item))
            {
                item.MemoryOwner.Return();
            }
        }
    }

    internal void ProcessSend(NetSender netSender)
    {
        while (netSender.CanSend &&
            this.sendItems.TryDequeue(out var item))
        {
            netSender.Send_NotThreadSafe(item.EndPoint, item.MemoryOwner);
        }
    }

    internal (IPEndPoint? EndPoint, bool Unrestricted) GetEndPoint_NotThreadSafe(NetAddress netAddress, EndpointOperation operation)
    {
        using (this.items.LockObject.EnterScope())
        {
            return this.GetEndPointCore(netAddress, operation);
        }
    }

    internal PingRelayResponse? ProcessPingRelay(RelayId destinationRelayId)
    {
        if (this.NumberOfExchanges == 0)
        {
            return null;
        }

        RelayExchange? exchange;
        PingRelayResponse? packet;
        using (this.items.LockObject.EnterScope())
        {
            exchange = this.items.RelayIdChain.FindFirst(destinationRelayId);
            if (exchange is null)
            {
                return null;
            }

            packet = new PingRelayResponse(exchange);
        }

        return packet;
    }

    private (IPEndPoint? EndPoint, bool Unrestricted) GetEndPointCore(NetAddress netAddress, EndpointOperation operation)
    {// Caller holds items.LockObject, which is not reentrant.
        if (!this.endPointCache.NetAddressChain.TryGetValue(netAddress, out var item))
        {
            this.netTerminal.NetStats.TryCreateEndpoint(ref netAddress, EndpointResolution.PreferIpv6, out var endpoint);
            item = new(netAddress, endpoint.EndPoint);
            this.endPointCache.Add(item);
            if (this.endPointCache.Count > EndPointCacheSize &&
                this.endPointCache.LinkedListChain.First is { } i)
            {
                i.Goshujin = default;
            }
        }
        else
        {
            this.endPointCache.LinkedListChain.AddLast(item);
        }

        var unrestricted = false;
        if (operation == EndpointOperation.SetUnrestricted)
        {// Unrestricted
            unrestricted = true;
            item.UnrestrictedMics = Mics.FastSystem;
        }
        else if (operation == EndpointOperation.SetRestricted)
        {// Restricted
            item.UnrestrictedMics = 0;
        }
        else
        {// None, Update
            if (item.UnrestrictedMics == 0 || Mics.FastSystem - item.UnrestrictedMics > UnrestrictedRetensionMics)
            {// Restricted
                item.UnrestrictedMics = 0;
            }
            else
            {// Unrestricted
                unrestricted = true;
                if (operation == EndpointOperation.Update)
                {
                    item.UnrestrictedMics = Mics.FastSystem;
                }
            }
        }

        return (item.EndPoint, unrestricted);
    }
}

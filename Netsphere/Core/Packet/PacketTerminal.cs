// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Netsphere.Core;
using Netsphere.Relay;
using Netsphere.Stats;
using Tinyhand.IO;

namespace Netsphere.Packet;

public sealed partial class PacketTerminal
{
    [ValueLinkObject(Isolation = IsolationLevel.Serializable)]
    private sealed partial class Item
    {
        // ResponseTcs == null: WaitingToSend -> (Send) -> (Remove)
        // ResponseTcs != null: WaitingToSend -> WaitingForResponse -> Complete or Resend
        [Link(Type = ChainType.LinkedList, Name = "WaitingToSendList", AutoLink = true)]
        [Link(Type = ChainType.LinkedList, Name = "WaitingForResponseList", AutoLink = false)]
        public Item(IPEndPoint endPoint, ulong packetId, BytePool.RentMemory dataToBeMoved, TaskCompletionSource<NetResponse>? responseTcs)
        {
            if (dataToBeMoved.Span.Length < PacketHeader.Length)
            {
                throw new InvalidOperationException();
            }

            this.EndPoint = endPoint;
            this.PacketId = packetId;
            this.MemoryOwner = dataToBeMoved;
            this.ResponseTcs = responseTcs;
        }

        [Link(Primary = true, Type = ChainType.Unordered)]
        public ulong PacketId { get; set; }

        public IPEndPoint EndPoint { get; }

        public BytePool.RentMemory MemoryOwner { get; }

        public TaskCompletionSource<NetResponse>? ResponseTcs { get; }

        public long SentMics { get; set; }

        public int ResentCount { get; set; }

        public void Remove()
        {
            this.MemoryOwner.Return();
            this.Goshujin = null;
        }
    }

    public PacketTerminal(NetBase netBase, NetTerminal netTerminal, ILogger<PacketTerminal> logger)
    {
        this.netBase = netBase;
        this.netTerminal = netTerminal;
        this.logger = logger;

        this.RetransmissionTimeoutMics = NetConstants.DefaultRetransmissionTimeoutMics;
        this.MaxResendCount = 2;
    }

    public long RetransmissionTimeoutMics { get; set; }

    public int MaxResendCount { get; set; }

    private readonly NetBase netBase;
    private readonly NetTerminal netTerminal;
    private readonly ILogger logger;
    private readonly Item.GoshujinClass items = new();

    public static void CreatePacket<TPacket>(ulong packetId, TPacket packet, out BytePool.RentMemory rentMemory)
        where TPacket : IPacket, ITinyhandSerializable<TPacket>
    {
        if (packetId == 0)
        {
            packetId = RandomVault.Default.NextUInt64();
        }

        var writer = TinyhandWriter.CreateFromBytePool();

        // PacketHeaderCode
        scoped Span<byte> header = stackalloc byte[PacketHeader.Length];
        var span = header;

        BitConverter.TryWriteBytes(span, (RelayId)0); // SourceRelayId
        span = span.Slice(sizeof(RelayId));
        BitConverter.TryWriteBytes(span, (RelayId)0); // DestinationRelayId
        span = span.Slice(sizeof(RelayId));

        BitConverter.TryWriteBytes(span, 0u); // Hash
        span = span.Slice(sizeof(uint));

        BitConverter.TryWriteBytes(span, (ushort)TPacket.PacketType); // PacketType
        span = span.Slice(sizeof(ushort));

        BitConverter.TryWriteBytes(span, packetId); // Id
        span = span.Slice(sizeof(ulong));

        writer.WriteSpan(header);

        TinyhandSerializer.SerializeObject(ref writer, packet);

        rentMemory = writer.FlushAndGetRentMemory();
        writer.Dispose();

        // Get checksum
        span = rentMemory.Span;
        BitConverter.TryWriteBytes(span.Slice(RelayHeader.RelayIdLength), (uint)XxHash3.Hash64(span.Slice(RelayHeader.RelayIdLength + sizeof(uint))));
    }

    /// <summary>
    /// Sends a packet to a specified address and waits for a response.
    /// </summary>
    /// <typeparam name="TSend">The type of the packet to send. Must implement IPacket and ITinyhandSerializable.</typeparam>
    /// <typeparam name="TReceive">The type of the packet to receive. Must implement IPacket and ITinyhandSerializable.</typeparam>
    /// <param name="netAddress">The address to send the packet to.</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="relayNumber">Specify the minimum number of relays or the target relay [default is 0].<br/>
    /// relayNumber &lt; 0: The target relay.<br/>
    /// relayNumber == 0: Relays are not necessary.<br/>
    /// relayNumber &gt; 0: The minimum number of relays.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="endpointResolution">The endpoint resolution strategy.</param>
    /// <param name="incomingCircuit">true if the incoming circuit is used; otherwise, false.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the <see cref="NetResult"/>, the received packet value of type <typeparamref name="TReceive"/>, and the round-trip time in microseconds.</returns>
    public async Task<(NetResult Result, TReceive? Value, int RttMics)> SendAndReceive<TSend, TReceive>(NetAddress netAddress, TSend packet, int relayNumber = 0, CancellationToken cancellationToken = default, EndpointResolution endpointResolution = EndpointResolution.PreferIpv6, bool incomingCircuit = false)
        where TSend : IPacket, ITinyhandSerializable<TSend>
        where TReceive : IPacket, ITinyhandSerializable<TReceive>
    {
        if (!this.netTerminal.IsActive)
        {
            return (NetResult.Closed, default, 0);
        }

        var responseTcs = new TaskCompletionSource<NetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        CreatePacket(0, packet, out var rentMemory); // CreatePacketCode
        var result = this.SendPacket(netAddress, rentMemory, responseTcs, relayNumber, endpointResolution, incomingCircuit);
        if (result != NetResult.Success)
        {
            return (result, default, 0);
        }

        if (NetConstants.LogLowLevelNet)
        {
            // this.logger.TryGet(LogLevel.Debug)?.Log($"{this.netTerminal.NetTerminalString} to {endPoint.ToString()} {rentMemory.Length} {typeof(TSend).Name}/{typeof(TReceive).Name}");
        }

        try
        {
            var response = await this.netTerminal.Wait(responseTcs.Task, this.netTerminal.PacketTransmissionTimeout, cancellationToken).ConfigureAwait(false);

            if (response.IsFailure)
            {
                return new(response.Result, default, 0);
            }

            TReceive? receive;
            try
            {
                receive = TinyhandSerializer.DeserializeObject<TReceive>(response.Received.Span.Slice(PacketHeader.Length));
            }
            catch
            {
                return new(NetResult.DeserializationFailed, default, 0);
            }
            finally
            {
                response.Return();
            }

            return (NetResult.Success, receive, (int)response.Additional);
        }
        catch
        {
            return (NetResult.Timeout, default, 0);
        }
    }

    /*/// <summary>
    /// Sends a packet to a specified address and waits for a response.
    /// </summary>
    /// <typeparam name="TSend">The type of the packet to send. Must implement IPacket and ITinyhandSerializable.</typeparam>
    /// <typeparam name="TReceive">The type of the packet to receive. Must implement IPacket and ITinyhandSerializable.</typeparam>
    /// <param name="endPoint">The endpoint to send the packet to.</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="relayNumber">Specify the minimum number of relays or the target relay [default is 0].<br/>
    /// &lt; 0: The target relay.<br/>
    /// 0: Relays are not necessary.<br/>
    /// 0 &gt;: The minimum number of relays.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task<(NetResult Result, TReceive? Value, int RttMics)> SendAndReceive<TSend, TReceive>(NetEndpoint endPoint, TSend packet, int relayNumber = 0)
    where TSend : IPacket, ITinyhandSerializable<TSend>
    where TReceive : IPacket, ITinyhandSerializable<TReceive>
    {
        if (!this.netTerminal.IsActive)
        {
            return (NetResult.Closed, default, 0);
        }

        var responseTcs = new TaskCompletionSource<NetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        CreatePacket(0, packet, out var rentMemory);
        this.AddSendPacket(endPoint, rentMemory, responseTcs);

        if (NetConstants.LogLowLevelNet)
        {
            // this.logger.TryGet(LogLevel.Debug)?.Log($"{this.netTerminal.NetTerminalString} to {endPoint.ToString()} {rentMemory.Length} {typeof(TSend).Name}/{typeof(TReceive).Name}");
        }

        try
        {
            var response = await this.netTerminal.Wait(responseTcs.Task, this.netTerminal.PacketTransmissionTimeout, default).ConfigureAwait(false);

            if (response.IsFailure)
            {
                return new(response.Result, default, 0);
            }

            TReceive? receive;
            try
            {
                receive = TinyhandSerializer.DeserializeObject<TReceive>(response.Received.Span.Slice(PacketHeader.Length));
            }
            catch
            {
                return new(NetResult.DeserializationFailed, default, 0);
            }
            finally
            {
                response.Return();
            }

            return (NetResult.Success, receive, (int)response.Additional);
        }
        catch
        {
            return (NetResult.Timeout, default, 0);
        }
    }*/

    internal void ProcessSend(NetSender netSender)
    {
        using (this.items.LockObject.EnterScope())
        {
            while (this.items.WaitingToSendListChain.First is { } item)
            {// Waiting to send
                if (!netSender.CanSend)
                {
                    return;
                }

                if (NetConstants.LogLowLevelNet)
                {
                    // this.logger.TryGet(LogLevel.Debug)?.Log($"{this.netTerminal.NetTerminalString} to {item.EndPoint.ToString()}, Send packet id:{item.PacketId}");
                }

                if (item.ResponseTcs is not null)
                {// WaitingToSend -> WaitingForResponse
                    netSender.Send_NotThreadSafe(item.EndPoint, item.MemoryOwner.IncrementAndShare());
                    item.SentMics = Mics.FastSystem;
                    this.items.WaitingToSendListChain.Remove(item);
                    this.items.WaitingForResponseListChain.AddLast(item);
                }
                else
                {// WaitingToSend -> Remove (without response)
                    netSender.Send_NotThreadSafe(item.EndPoint, item.MemoryOwner.IncrementAndShare());
                    item.Remove();
                }
            }

            while (this.items.WaitingForResponseListChain.First is { } item && (Mics.FastSystem - item.SentMics) > this.RetransmissionTimeoutMics)
            {// Waiting for response list
                if (!netSender.CanSend)
                {
                    return;
                }

                if (item.ResentCount >= this.MaxResendCount)
                {// The maximum number of resend attempts reached.
                    item.Remove();
                    continue;
                }

                // PacketHeaderCode
                var span = item.MemoryOwner.Span;
                if (MemoryMarshal.Read<RelayId>(span.Slice(sizeof(RelayId))) == 0)
                {// No relay
                    // Reset packet id in order to improve the accuracy of RTT measurement.
                    var newPacketId = RandomVault.Default.NextUInt64();
                    item.PacketIdValue = newPacketId;

                    BitConverter.TryWriteBytes(span.Slice(RelayHeader.RelayIdLength + 6), newPacketId);
                    BitConverter.TryWriteBytes(span.Slice(RelayHeader.RelayIdLength), (uint)XxHash3.Hash64(span.Slice(RelayHeader.RelayIdLength + 4)));
                }

                netSender.Send_NotThreadSafe(item.EndPoint, item.MemoryOwner.IncrementAndShare());
                item.SentMics = Mics.FastSystem;
                item.ResentCount++;
                this.items.WaitingForResponseListChain.AddLast(item);
            }
        }
    }

    internal void ProcessReceive(NetEndpoint endpoint, int relayNumber, bool incomingRelay, RelayId destinationRelayId, ushort packetUInt16, BytePool.RentMemory toBeShared, long currentSystemMics)
    {// Checked: toBeShared.Length
        if (NetConstants.LogLowLevelNet)
        {
            // this.logger.TryGet(LogLevel.Debug)?.Log($"Receive actual");
        }

        // PacketHeaderCode
        var span = toBeShared.Span;
        if (BitConverter.ToUInt32(span.Slice(RelayHeader.RelayIdLength)) != (uint)XxHash3.Hash64(span.Slice(RelayHeader.RelayIdLength + sizeof(uint))))
        {// Checksum
            return;
        }

        var packetType = (PacketType)packetUInt16;
        var packetId = BitConverter.ToUInt64(span.Slice(RelayHeader.RelayIdLength + sizeof(uint) + sizeof(PacketType)));

        span = span.Slice(PacketHeader.Length);
        if (packetUInt16 < 127)
        {// Packet types (0-127), Client -> Server
            if (relayNumber != 0 && !incomingRelay)
            {// Outgoing relay
                return;
            }

            if (packetType == PacketType.Connect)
            {// ConnectPacket
                if (!this.netTerminal.IsActive)
                {
                    return;
                }
                else if (!this.netBase.NetOptions.EnableServer)
                {
                    return;
                }

                if (TinyhandSerializer.TryDeserialize<ConnectPacket>(span, out var p))
                {
                    if (p.ServerPublicKeyChecksum != this.netTerminal.NodePublicKey.GetHashCode())
                    {// Public Key does not match
                        return;
                    }

                    Task.Run(() =>
                    {
                        var packet = new ConnectPacketResponse(this.netBase.DefaultAgreement, endpoint);
                        this.netTerminal.ConnectionTerminal.PrepareServerSide(endpoint, p, packet, relayNumber);
                        CreatePacket(packetId, packet, out var rentMemory); // CreatePacketCode (no relay)
                        // this.SendPacketWithoutRelay(endpoint, rentMemory, default);
                        this.SendPacketWithRelay(endpoint, rentMemory, incomingRelay, relayNumber);
                    });

                    return;
                }
            }
            else if (packetType == PacketType.Ping)
            {// PingPacket
                var netOptions = this.netBase.NetOptions;
                if (netOptions.EnablePing ||
                    endpoint.IsPrivateOrLocalLoopbackAddress())
                {
                    var packet = new PingPacketResponse(endpoint, netOptions.NodeName, netOptions.NetsphereId);
                    CreatePacket(packetId, packet, out var rentMemory); // CreatePacketCode (no relay)
                    // this.SendPacketWithoutRelay(endpoint, rentMemory, default);
                    this.SendPacketWithRelay(endpoint, rentMemory, incomingRelay, relayNumber);

                    if (NetConstants.LogLowLevelNet)
                    {
                        // this.logger.TryGet()?.Log($"{this.netTerminal.NetTerminalString} to {endPoint.ToString()} PingResponse");
                    }
                }

                return;
            }
            else if (packetType == PacketType.Punch)
            {// PunchPacket
                var netOptions = this.netBase.NetOptions;
                if (netOptions.EnablePing)
                {
                    if (TinyhandSerializer.TryDeserialize<PunchPacket>(span, out var p) &&
                        p.DestinationEndpoint.IsValid)
                    {
                        // Console.WriteLine($"PunchPacket {p.ToString()}");
                        if (p.RelayEndpoint.IsValid)
                        {// Relay
                            var packet = new PunchPacket(default, p.DestinationEndpoint);
                            CreatePacket(packetId, packet, out var rentMemory); // CreatePacketCode (no relay)
                            this.SendPacketWithoutRelay(p.RelayEndpoint, rentMemory, default);
                        }
                        else
                        {// Response
                            var packet = new PunchPacketResponse();
                            CreatePacket(packetId, packet, out var rentMemory); // CreatePacketCode (no relay)
                            this.SendPacketWithoutRelay(p.DestinationEndpoint, rentMemory, default);
                        }
                    }
                }

                return;
            }
            else if (packetType == PacketType.GetInformation)
            {// GetInformationPacket
                if (this.netBase.AllowUnsafeConnection)
                {
                    var packet = new GetInformationPacketResponse(this.netTerminal.NodePublicKey, this.netTerminal.NetStats.OwnNetNode);
                    CreatePacket(packetId, packet, out var rentMemory); // CreatePacketCode (no relay)
                    // this.SendPacketWithoutRelay(endpoint, rentMemory, default);
                    this.SendPacketWithRelay(endpoint, rentMemory, incomingRelay, relayNumber);
                }

                return;
            }
            else if (packetType == PacketType.PingRelay)
            {// PingRelay
                var packet = this.netTerminal.RelayAgent.ProcessPingRelay(destinationRelayId);
                if (packet is not null)
                {
                    // Console.WriteLine($"{relayNumber} {packet.ToString()} -> {endpoint}");
                    CreatePacket(packetId, packet, out var rentMemory);
                    // this.SendPacketWithoutRelay(endpoint, rentMemory, default);
                    this.SendPacketWithRelay(endpoint, rentMemory, incomingRelay, relayNumber);
                }

                return;
            }
            else if (NetConstants.EnableOpenSesami &&
                packetType == PacketType.OpenSesami &&
                relayNumber > 0 && incomingRelay)
            {
                if (TinyhandSerializer.TryDeserialize<OpenSesamiPacket>(span, out var p))
                {
                    OpenSesamiResponse packet;
                    if (this.netTerminal.IncomingCircuit.AllowOpenSesami &&
                        this.netTerminal.NetStats.OwnNetNode is { } ownNetNode)
                    {
                        // Punch
                        var punchPacket = new PunchPacket();
                        CreatePacket(packetId, punchPacket, out var punchMemory); // CreatePacketCode (no relay)
                        this.SendPacketWithoutRelay(endpoint, punchMemory, default);

                        packet = new(ownNetNode.Address);
                    }
                    else
                    {
                        packet = new();
                    }

                    CreatePacket(packetId, packet, out var rentMemory); // CreatePacketCode (no relay)
                    this.SendPacketWithoutRelay(endpoint, rentMemory, default);
                }
            }
            else if (this.netBase.RespondPacketFunc is { } func)
            {
                var memory = toBeShared.Slice(toBeShared.Length - span.Length).Memory;
                if (func(packetId, packetType, memory) is { } rentMemory)
                {
                    // CreatePacket(packetId, packet, out var rentMemory);
                    this.SendPacketWithRelay(endpoint, rentMemory, incomingRelay, relayNumber);
                }
            }
        }
        else if (packetUInt16 < 255)
        {// Packet response types (128-255), Server -> Client (Response)
            Item? item;
            using (this.items.LockObject.EnterScope())
            {
                if (this.items.PacketIdChain.TryGetValue(packetId, out item))
                {
                    item.Remove();
                }
            }

            if (item is not null)
            {
                if (NetConstants.LogLowLevelNet)
                {
                    this.logger.TryGet(LogLevel.Debug)?.Log($"{this.netTerminal.NetTerminalString} received {toBeShared.Span.Length} {packetType.ToString()}");
                }

                if (item.ResponseTcs is { } tcs)
                {
                    var elapsedMics = currentSystemMics > item.SentMics ? (int)(currentSystemMics - item.SentMics) : 0;
                    tcs.SetResult(new(NetResult.Success, 0, elapsedMics, toBeShared.IncrementAndShare()));
                }
            }
        }
        else
        {
        }
    }

    internal unsafe NetResult SendPacket(NetAddress netAddress, BytePool.RentMemory dataToBeMoved, TaskCompletionSource<NetResponse>? responseTcs, int relayNumber, EndpointResolution endpointResolution, bool incomingRelay)
    {
        var length = dataToBeMoved.Span.Length;
        if (length < PacketHeader.Length ||
            length > NetConstants.MaxPacketLength)
        {
            return NetResult.InvalidData;
        }

        var circuit = incomingRelay ? this.netTerminal.IncomingCircuit : this.netTerminal.OutgoingCircuit;
        if (relayNumber > 0)
        {// The minimum number of relays
            if (circuit.NumberOfRelays < relayNumber)
            {
                return NetResult.InvalidRelay;
            }
        }
        else if (relayNumber < 0)
        {// The target relay
            if (circuit.NumberOfRelays < -relayNumber)
            {
                return NetResult.InvalidRelay;
            }
        }

        var packetId = BitConverter.ToUInt64(dataToBeMoved.Span.Slice(RelayHeader.RelayIdLength + 6)); // PacketHeaderCode
        NetEndpoint endpoint;
        if (relayNumber == 0)
        {// No relay
            if (!this.netTerminal.TryCreateEndpoint(ref netAddress, endpointResolution, out endpoint))
            {
                return NetResult.NoNetwork;
            }

            var span = dataToBeMoved.Span;
            BitConverter.TryWriteBytes(span, (RelayId)0); // SourceRelayId
            span = span.Slice(sizeof(RelayId));
            BitConverter.TryWriteBytes(span, netAddress.RelayId); // DestinationRelayId
        }
        else
        {// Relay
            if (!circuit.RelayKey.TryEncrypt(relayNumber, netAddress, dataToBeMoved.Span, out var encrypted, out endpoint))
            {
                dataToBeMoved.Return();
                return NetResult.InvalidRelay;
            }

            dataToBeMoved.Return();
            dataToBeMoved = encrypted;
        }

        if (endpoint.EndPoint is null)
        {
            return NetResult.InvalidEndpoint;
        }

        var item = new Item(endpoint.EndPoint, packetId, dataToBeMoved, responseTcs);
        using (this.items.LockObject.EnterScope())
        {
            item.Goshujin = this.items;

            // Send immediately (This enhances performance in a local environment, but since it's meaningless in an actual network, it has been disabled)
            /*var netSender = this.netTerminal.NetSender;
            if (!item.Ack)
            {// Without ack
                netSender.SendImmediately(item.EndPoint, item.MemoryOwner.Span);
                item.Goshujin = null;
            }
            else
            {// Ack (sent list)
                netSender.SendImmediately(item.EndPoint, item.MemoryOwner.Span);
                item.SentMics = netSender.CurrentSystemMics;
                item.SentCount++;
                this.items.ToSendListChain.Remove(item);
                this.items.SentListChain.AddLast(item);
            }*/
        }

        // this.logger.TryGet(LogLevel.Debug)?.Log("AddSendPacket");

        return NetResult.Success;
    }

    internal unsafe NetResult SendPacketWithRelay(NetEndpoint endpoint, BytePool.RentMemory dataToBeMoved, bool incomingRelay, int relayNumber)
    {
        var length = dataToBeMoved.Span.Length;
        if (length < PacketHeader.Length ||
            length > NetConstants.MaxPacketLength)
        {
            return NetResult.InvalidData;
        }

        var circuit = incomingRelay ? this.netTerminal.IncomingCircuit : this.netTerminal.OutgoingCircuit;
        if (relayNumber > 0)
        {// The minimum number of relays
            if (circuit.NumberOfRelays < relayNumber)
            {
                return NetResult.InvalidRelay;
            }
        }
        else if (relayNumber < 0)
        {// The target relay
            if (circuit.NumberOfRelays < -relayNumber)
            {
                return NetResult.InvalidRelay;
            }
        }

        var packetId = BitConverter.ToUInt64(dataToBeMoved.Span.Slice(RelayHeader.RelayIdLength + 6)); // PacketHeaderCode
        if (relayNumber == 0)
        {// No relay
            var span = dataToBeMoved.Span;
            BitConverter.TryWriteBytes(span, (RelayId)0); // SourceRelayId
            span = span.Slice(sizeof(RelayId));
            BitConverter.TryWriteBytes(span, endpoint.RelayId); // DestinationRelayId
        }
        else
        {// Relay
            if (!circuit.RelayKey.TryEncrypt(relayNumber, new NetAddress(endpoint), dataToBeMoved.Span, out var encrypted, out endpoint))
            {
                dataToBeMoved.Return();
                return NetResult.InvalidRelay;
            }

            dataToBeMoved.Return();
            dataToBeMoved = encrypted;
        }

        if (endpoint.EndPoint is null)
        {
            return NetResult.InvalidEndpoint;
        }

        var item = new Item(endpoint.EndPoint, packetId, dataToBeMoved, default);
        using (this.items.LockObject.EnterScope())
        {
            item.Goshujin = this.items;
        }

        return NetResult.Success;
    }

    internal unsafe NetResult SendPacketWithoutRelay(NetEndpoint endpoint, BytePool.RentMemory dataToBeMoved, TaskCompletionSource<NetResponse>? responseTcs)
    {
        var length = dataToBeMoved.Span.Length;
        if (length < PacketHeader.Length ||
            length > NetConstants.MaxPacketLength)
        {
            return NetResult.InvalidData;
        }

        if (!endpoint.IsValid)
        {
            return NetResult.InvalidEndpoint;
        }

        // PacketHeaderCode
        var span = dataToBeMoved.Span;
        BitConverter.TryWriteBytes(span, (RelayId)0); // SourceRelayId
        span = span.Slice(sizeof(RelayId));
        BitConverter.TryWriteBytes(span, endpoint.RelayId); // DestinationRelayId
        var packetId = BitConverter.ToUInt64(dataToBeMoved.Span.Slice(sizeof(RelayId) + sizeof(uint))); // PacketId

        var item = new Item(endpoint.EndPoint, packetId, dataToBeMoved, responseTcs);
        using (this.items.LockObject.EnterScope())
        {
            item.Goshujin = this.items;
        }

        return NetResult.Success;
    }
}

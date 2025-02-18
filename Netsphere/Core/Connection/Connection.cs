// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Arc.Collections;
using Netsphere.Core;
using Netsphere.Crypto;
using Netsphere.Packet;
using Netsphere.Relay;

#pragma warning disable SA1202
#pragma warning disable SA1214
#pragma warning disable SA1401

namespace Netsphere;

// byte[32 = EmbryoKeyLength] Key, byte[16 = EmbryoIvLength] Iv
internal readonly record struct Embryo(ulong Salt, byte[] Key, byte[] Iv);

public abstract class Connection : IDisposable
{
    private const int LowerRttLimit = 5_000; // 5ms
    private const int UpperRttLimit = 1_000_000; // 1000ms
    private const int DefaultRtt = 100_000; // 100ms
    internal const int EmbryoSize = 64;

    public enum ConnectMode
    {
        ReuseIfAvailable,
        ReuseOnly,
        NoReuse,
    }

    public enum State
    {
        Open,
        Closed,
        Disposed,
    }

    public Connection(PacketTerminal packetTerminal, ConnectionTerminal connectionTerminal, ulong connectionId, NetNode node, NetEndpoint endPoint)
    {
        this.NetBase = connectionTerminal.NetBase;
        this.Logger = this.NetBase.UnitLogger.GetLogger(this.GetType());
        this.PacketTerminal = packetTerminal;
        this.ConnectionTerminal = connectionTerminal;
        this.ConnectionId = connectionId;
        this.DestinationNode = node;
        this.DestinationEndpoint = endPoint;

        this.smoothedRtt = DefaultRtt;
        this.minimumRtt = 0;
        this.UpdateLastEventMics();
    }

    public Connection(Connection connection)
        : this(connection.PacketTerminal, connection.ConnectionTerminal, connection.ConnectionId, connection.DestinationNode, connection.DestinationEndpoint)
    {
        this.Initialize(connection.Agreement, connection.embryo);
    }

    #region FieldAndProperty

    public NetBase NetBase { get; }

    public NetTerminal NetTerminal => this.ConnectionTerminal.NetTerminal;

    internal ConnectionTerminal ConnectionTerminal { get; }

    internal PacketTerminal PacketTerminal { get; }

    public ulong ConnectionId { get; }

    public string ConnectionIdText
        => ((ushort)this.ConnectionId).ToString("x4");

    public NetNode DestinationNode { get; }

    public NetEndpoint DestinationEndpoint { get; internal set; }

    /// <summary>
    /// Gets the minimum number of relays for the connection.
    /// 0: Connection that does not require a relay.
    /// Above 0: Connection that requires the specified number of relays.
    /// Below 0: Relay connection corresponding to the (-n) th relay.
    /// </summary>
    public int MinimumNumberOfRelays { get; internal set; }

    public ConnectionAgreement Agreement { get; private set; } = ConnectionAgreement.Default;

    public State CurrentState { get; private set; }

    public abstract bool IsClient { get; }

    public abstract bool IsServer { get; }

    public bool IsActive
        => this.NetTerminal.IsActive && this.CurrentState == State.Open;

    public bool IsOpen
        => this.CurrentState == State.Open;

    public bool IsClosed
        => this.CurrentState == State.Closed;

    public bool IsDisposed
        => this.CurrentState == State.Disposed;

    public bool IsClosedOrDisposed
        => this.CurrentState == State.Closed || this.CurrentState == State.Disposed;

    public int SmoothedRtt
        => this.smoothedRtt;

    public int MinimumRtt
        => this.minimumRtt == 0 ? this.smoothedRtt : this.minimumRtt;

    public int LatestRtt
        => this.latestRtt;

    public int RttVar
        => this.rttvar;

    // this.smoothedRtt + Math.Max(this.rttvar * 4, 1_000) + NetConstants.AckDelayMics; // 10ms
    public int RetransmissionTimeout
        => this.smoothedRtt + (this.smoothedRtt >> 2) + (this.rttvar << 2) + NetConstants.AckDelayMics;

    public int TaichiTimeout
        => this.RetransmissionTimeout * this.Taichi;

    public int SendCount
        => this.sendCount;

    public int ResendCount
        => this.resendCount;

    public double DeliveryRatio
    {
        get
        {
            var total = this.sendCount + this.resendCount;
            return total == 0 ? 1.0d : (this.sendCount / (double)total);
        }
    }

    internal ILogger Logger { get; }

    internal int SendTransmissionsCount
        => this.sendTransmissions.Count;

    internal bool IsEmpty
        => this.sendTransmissions.Count == 0 &&
        this.receiveTransmissions.Count == 0;

    internal bool CloseIfTransmissionHasTimedOut()
    {
        if (this.LastEventMics + NetConstants.TransmissionTimeoutMics < Mics.FastSystem)
        {// Timeout
            this.ConnectionTerminal.CloseInternal(this, true);
            return true;
        }
        else
        {
            return false;
        }
    }

    internal long LastEventMics { get; private set; } // When any packet, including an Ack, is received, it's updated to the latest time.

    internal ICongestionControl? CongestionControl; // ConnectionTerminal.SyncSend
    internal UnorderedLinkedList<SendTransmission> SendList = new(); // lock (this.ConnectionTerminal.SyncSend)
    internal UnorderedLinkedList<Connection>.Node? SendNode; // lock (this.ConnectionTerminal.SyncSend)

    internal RelayKey CorrespondingRelayKey => this.IsClient ? this.NetTerminal.OutgoingCircuit.RelayKey : this.NetTerminal.IncomingCircuit.RelayKey;

    #region Embryo

    private byte[] embryo = Array.Empty<byte>();

    // public ulong ConnectionId => BitConverter.ToUInt64(this.embryo.AsSpan(0)); // Assigned in the constructor.

    public ulong EmbryoSalt => BitConverter.ToUInt64(this.embryo.AsSpan(8));

    public ulong EmbryoSecret => BitConverter.ToUInt64(this.embryo.AsSpan(16));

    // public ulong EmbryoReserved => BitConverter.ToUInt64(this.embryo.AsSpan(24));

    public ReadOnlySpan<byte> EmbryoKey => this.embryo.AsSpan(32, Aegis256.KeySize); // embryo[32..]

    #endregion

    private SendTransmission.GoshujinClass sendTransmissions = new(); // using (this.sendTransmissions.LockObject.EnterScope())
    private UnorderedLinkedList<SendTransmission> sendAckedList = new();

    // ReceiveTransmissionCode, using (this.receiveTransmissions.LockObject.EnterScope())
    private ReceiveTransmission.GoshujinClass receiveTransmissions = new();
    private UnorderedLinkedList<ReceiveTransmission> receiveReceivedList = new();
    private UnorderedLinkedList<ReceiveTransmission> receiveDisposedList = new();

    // RTT
    private int minimumRtt; // Minimum rtt (mics)
    private int smoothedRtt; // Smoothed rtt (mics)
    private int latestRtt; // Latest rtt (mics)
    private int rttvar; // Rtt variation (mics)
    private int sendCount;
    private int resendCount;

    // Ack
    internal long AckMics; // using (AckBuffer.lockObject.EnterScope())
    internal Queue<AckBuffer.ReceiveTransmissionAndAckGene>? AckQueue; // using (AckBuffer.lockObject.EnterScope())

    // Connection lost
    internal int Taichi = 1;

    #endregion

    /*internal Embryo UnsafeGetEmbryo()
        => this.embryo;*/

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void UpdateAckedNode(SendTransmission sendTransmission)
    {// lock (Connection.sendTransmissions.SyncObject)
        sendTransmission.AckedMics = Mics.FastSystem;
        this.sendTransmissions.AckedListChain.AddLast(sendTransmission);

        /*if (sendTransmission.AckedNode is null)
        {
            sendTransmission.AckedNode = this.sendAckedList.AddLast(sendTransmission);
        }
        else
        {
            this.sendAckedList.MoveToLast(sendTransmission.AckedNode);
        }*/
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ChangeStateInternal(State state)
    {// lock (this.clientConnections.SyncObject) or lock (this.serverConnections.SyncObject)
        if (this.CurrentState == state)
        {
            if (this.CurrentState == State.Open)
            {
                this.UpdateLastEventMics();
            }

            return;
        }

        this.CurrentState = state;
        this.UpdateLastEventMics();
        this.OnStateChanged();
    }

    public void SignWithSalt<T>(T value, SeedKey seedKey)
        where T : ITinyhandSerializable<T>, ISignAndVerify
    {
        value.Salt = this.EmbryoSalt;
        seedKey.Sign(value);
    }

    public bool ValidateAndVerifyWithSalt<T>(T value)
        where T : ITinyhandSerializable<T>, ISignAndVerify
    {
        return NetHelper.ValidateAndVerify(value, this);
    }

    /// <summary>
    /// Close the connection without considering OpenCount.
    /// </summary>
    internal void CloseInternal()
        => this.Dispose();

    internal void ResetTaichi()
        => this.Taichi = 1;

    internal void DoubleTaichi()
    {
        this.Taichi <<= 1;
        if (this.Taichi < 1)
        {
            this.Taichi = 1;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void UpdateLastEventMics()
        => this.LastEventMics = Mics.FastSystem;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ICongestionControl GetCongestionControl()
    {
        return this.CongestionControl is null ? this.ConnectionTerminal.NoCongestionControl : this.CongestionControl;
    }

    internal void CreateCongestionControl()
    {
        while (true)
        {
            if (this.CongestionControl is not null)
            {
                return;
            }

            var congestionControl = new CubicCongestionControl(this);
            if (Interlocked.CompareExchange(ref this.CongestionControl, congestionControl, null) == null)
            {
                lock (this.ConnectionTerminal.CongestionControlList)
                {
                    this.ConnectionTerminal.CongestionControlList.AddLast(congestionControl);
                }

                return;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void AddSend(SendTransmission transmission)
    {
        var list = this.ConnectionTerminal.SendList;
        using (this.ConnectionTerminal.LockSend.EnterScope())
        {
            if (this.SendNode is null)
            {
                this.SendNode = list.AddLast(this);
            }

            if (transmission.SendNode is null)
            {
                transmission.SendNode = this.SendList.AddLast(transmission);
            }
        }
    }

    internal SendTransmission? TryCreateSendTransmission()
    {
        if (!this.IsActive)
        {
            return default;
        }

        using (this.sendTransmissions.LockObject.EnterScope())
        {
            if (this.IsClosedOrDisposed ||
                this.SendTransmissionsCount >= this.Agreement.MaxTransmissions)
            {
                return default;
            }

            uint transmissionId;
            do
            {
                transmissionId = RandomVault.Default.NextUInt32();
            }
            while (transmissionId == 0 || this.sendTransmissions.TransmissionIdChain.ContainsKey(transmissionId));

            var sendTransmission = new SendTransmission(this, transmissionId);
            sendTransmission.Goshujin = this.sendTransmissions;
            return sendTransmission;
        }
    }

    internal SendTransmission? TryCreateSendTransmission(uint transmissionId)
    {
        using (this.sendTransmissions.LockObject.EnterScope())
        {
            if (this.IsClosedOrDisposed)
            {
                return default;
            }

            /* To maintain consistency with the number of SendTransmission on the client side, limit the number of ReceiveTransmission in ProcessReceive_FirstGene().
            if (this.NumberOfSendTransmissions >= this.Agreement.MaxTransmissions)
            {
                return default;
            }*/

            if (transmissionId == 0 || this.sendTransmissions.TransmissionIdChain.ContainsKey(transmissionId))
            {
                return default;
            }

            var sendTransmission = new SendTransmission(this, transmissionId);
            sendTransmission.Goshujin = this.sendTransmissions;
            return sendTransmission;
        }
    }

    internal async ValueTask<SendTransmissionAndTimeout> TryCreateSendTransmission(TimeSpan timeout, CancellationToken cancellationToken)
    {
Retry:
        if (!this.IsActive || timeout < TimeSpan.Zero)
        {
            return default;
        }

        using (this.sendTransmissions.LockObject.EnterScope())
        {
            if (this.IsClosedOrDisposed)
            {
                return default;
            }

            if (this.SendTransmissionsCount >= this.Agreement.MaxTransmissions)
            {
                goto Wait;
            }

            uint transmissionId;
            do
            {
                transmissionId = RandomVault.Default.NextUInt32();
            }
            while (transmissionId == 0 || this.sendTransmissions.TransmissionIdChain.ContainsKey(transmissionId));

            var sendTransmission = new SendTransmission(this, transmissionId);
            sendTransmission.Goshujin = this.sendTransmissions;
            return new(sendTransmission, timeout);
        }

Wait:
        await Task.Delay(NetConstants.CreateTransmissionDelay, cancellationToken).ConfigureAwait(false);
        timeout -= NetConstants.CreateTransmissionDelay;
        goto Retry;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void CleanTransmission()
    {
        if (this.sendTransmissions.Count > 0)
        {
            using (this.sendTransmissions.LockObject.EnterScope())
            {
                this.CleanSendTransmission();
            }
        }
    }

    internal void CleanSendTransmission()
    {// using (this.sendTransmissions.LockObject.EnterScope())
        // Release send transmissions that have elapsed a certain time since the last ack.
        var currentMics = Mics.FastSystem;
        while (this.sendAckedList.First is { } node)
        {
            var transmission = node.Value;
            if (currentMics < transmission.AckedMics + NetConstants.TransmissionTimeoutMics)
            {
                break;
            }

            transmission.DisposeTransmission();
            // node.List.Remove(node);
            // transmission.AckedNode = null;
            transmission.Goshujin = null;
        }
    }

    internal void CleanReceiveTransmission()
    {// using (this.receiveTransmissions.LockObject.EnterScope())
        Debug.Assert(this.receiveTransmissions.Count == (this.receiveReceivedList.Count + this.receiveDisposedList.Count));

        // Release receive transmissions that have elapsed a certain time after being disposed.
        var currentMics = Mics.FastSystem;
        while (this.receiveDisposedList.First is { } node)
        {
            var transmission = node.Value;
            if (currentMics < transmission.ReceivedOrDisposedMics + NetConstants.TransmissionDisposalMics)
            {
                break;
            }

            node.List.Remove(node);
            transmission.ReceivedOrDisposedNode = null;
            transmission.Goshujin = null;
        }

        // Release receive transmissions that have elapsed a certain time since the last data reception.
        while (this.receiveReceivedList.First is { } node)
        {
            var transmission = node.Value;
            if (currentMics < transmission.ReceivedOrDisposedMics + NetConstants.TransmissionTimeoutMics)
            {
                break;
            }

            node.List.Remove(node);
            transmission.DisposeTransmission();
            transmission.ReceivedOrDisposedNode = null;
            transmission.Goshujin = null;
        }
    }

    internal ReceiveTransmission? TryCreateReceiveTransmission(uint transmissionId, TaskCompletionSource<NetResponse>? receivedTcs)
    {
        transmissionId += this.ConnectionTerminal.ReceiveTransmissionGap;

        using (this.receiveTransmissions.LockObject.EnterScope())
        {
            this.CleanReceiveTransmission();

            if (this.IsClosedOrDisposed)
            {
                return default;
            }

            if (this.receiveTransmissions.TransmissionIdChain.TryGetValue(transmissionId, out var receiveTransmission))
            {
                return default;
            }

            receiveTransmission = new ReceiveTransmission(this, transmissionId, receivedTcs);
            receiveTransmission.ReceivedOrDisposedMics = Mics.FastSystem;
            receiveTransmission.ReceivedOrDisposedNode = this.receiveReceivedList.AddLast(receiveTransmission);
            receiveTransmission.Goshujin = this.receiveTransmissions;
            return receiveTransmission;
        }
    }

    /*internal ReceiveTransmission? TryCreateOrReuseReceiveTransmission(uint transmissionId, TaskCompletionSource<NetResponse>? receivedTcs)
    {
        transmissionId += this.ConnectionTerminal.ReceiveTransmissionGap;

        using (this.receiveTransmissions.LockObject.EnterScope())
        {
            // this.CleanReceiveTransmission();

            if (this.IsClosedOrDisposed)
            {
                return default;
            }

            if (this.receiveTransmissions.TransmissionIdChain.TryGetValue(transmissionId, out var receiveTransmission))
            {
                if (receiveTransmission.Mode == NetTransmissionMode.Initial)
                {
                    receiveTransmission.Reset(receivedTcs);
                    return receiveTransmission;
                }
                else if (receiveTransmission.Mode == NetTransmissionMode.Disposed)
                {
                    receiveTransmission.ReceivedOrDisposedMics = Mics.FastSystem;
                    if (receiveTransmission.ReceivedOrDisposedNode is { } node)
                    {
                        node.List.Remove(node);
                    }

                    receiveTransmission.ReceivedOrDisposedNode = this.receiveReceivedList.AddLast(receiveTransmission);
                    receiveTransmission.Reset(receivedTcs);
                    return receiveTransmission;
                }
                else
                {
                    return default;
                }
            }

            receiveTransmission = new ReceiveTransmission(this, transmissionId, receivedTcs);
            receiveTransmission.ReceivedOrDisposedMics = Mics.FastSystem;
            receiveTransmission.ReceivedOrDisposedNode = this.receiveReceivedList.AddLast(receiveTransmission);
            receiveTransmission.Goshujin = this.receiveTransmissions;
            return receiveTransmission;
        }
    }*/

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool RemoveTransmission(SendTransmission transmission)
    {
        using (this.sendTransmissions.LockObject.EnterScope())
        {
            if (transmission.Goshujin == this.sendTransmissions)
            {
                transmission.Goshujin = null;
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool RemoveTransmission(ReceiveTransmission transmission)
    {
        using (this.receiveTransmissions.LockObject.EnterScope())
        {
            if (transmission.Goshujin == this.receiveTransmissions)
            {
                transmission.ReceivedOrDisposedMics = Mics.FastSystem; // Disposed mics
                if (transmission.ReceivedOrDisposedNode is { } node)
                {// ReceivedList -> DisposedList
                    node.List.Remove(node);
                    transmission.ReceivedOrDisposedNode = this.receiveDisposedList.AddLast(transmission);
                    Debug.Assert(transmission.ReceivedOrDisposedNode.List != null);
                }
                else
                {// -> DisposedList
                    transmission.ReceivedOrDisposedNode = this.receiveDisposedList.AddLast(transmission);
                }

                // transmission.Goshujin = null; // Delay the release to return ACK even after the receive transmission has ended.
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    internal void Initialize(ConnectionAgreement agreement, byte[] embryo)
    {
        this.Agreement = agreement;
        this.embryo = embryo;
    }

    internal void AddRtt(int rttMics)
    {
        if (rttMics < LowerRttLimit)
        {
            rttMics = LowerRttLimit;
        }
        else if (rttMics > UpperRttLimit)
        {
            rttMics = UpperRttLimit;
        }

        this.latestRtt = rttMics;
        if (this.minimumRtt == 0)
        {// Initial
            this.minimumRtt = rttMics;
            this.smoothedRtt = rttMics;
            this.rttvar = rttMics >> 1;
        }
        else
        {// Update
            if (this.minimumRtt > rttMics)
            {// minRtt is greater then the latest rtt.
                this.minimumRtt = rttMics;
            }

            var adjustedRtt = rttMics; // - ackDelay
            this.smoothedRtt = ((this.smoothedRtt * 7) >> 3) + (adjustedRtt >> 3);
            var rttvarSample = Math.Abs(this.smoothedRtt - adjustedRtt);
            this.rttvar = ((this.rttvar * 3) >> 2) + (rttvarSample >> 2);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void IncrementResendCount()
    {
        this.resendCount++; // Not thread-safe, though it doesn't matter.
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void IncrementSendCount()
    {
        this.sendCount++; // Not thread-safe, though it doesn't matter.
    }

    internal void SendPriorityFrame(scoped Span<byte> frame)
    {// Close, Ack, Knock
        if (!this.CreatePacket(frame, out var rentArray))
        {
            return;
        }

        this.PacketTerminal.SendPacket(this.DestinationNode.Address, rentArray, default, this.MinimumNumberOfRelays, EndpointResolution.PreferIpv6, false);
    }

    internal void SendCloseFrame()
    {
        // this.SendPriorityFrame([]); // Close 1 (Obsolete)

        Span<byte> frame = stackalloc byte[2]; // Close 2
        var span = frame;
        BitConverter.TryWriteBytes(span, (ushort)FrameType.Close);
        span = span.Slice(sizeof(ushort));
        this.SendPriorityFrame(frame);
    }

    internal ProcessSendResult ProcessSingleSend(NetSender netSender)
    {// lock (this.ConnectionTerminal.SyncSend)
        if (this.IsClosedOrDisposed)
        {// Connection closed
            return ProcessSendResult.Complete;
        }

        var node = this.SendList.First;
        if (node is null)
        {// No transmission to send
            return ProcessSendResult.Complete;
        }

        var transmission = node.Value;
        Debug.Assert(transmission.SendNode == node);

        if (this.LastEventMics + NetConstants.TransmissionTimeoutMics < Mics.FastSystem)
        {// Timeout
            this.ConnectionTerminal.CloseInternal(this, true);
            return ProcessSendResult.Complete;
        }

        var congestionControl = this.GetCongestionControl();
        if (congestionControl.IsCongested)
        {// If in a congested state, return ProcessSendResult.Congestion.
            return ProcessSendResult.Congested;
        }

        var result = transmission.ProcessSingleSend(netSender);
        if (result == ProcessSendResult.Complete)
        {// Delete the node if there is no gene to send.
            this.SendList.Remove(node);
            transmission.SendNode = default;
        }
        else if (result == ProcessSendResult.Remaining)
        {// If there are remaining genes, move it to the end.
            this.SendList.MoveToLast(node);
        }

        return this.SendList.Count == 0 ? ProcessSendResult.Complete : ProcessSendResult.Remaining;
    }

    internal void ProcessReceive(NetEndpoint endpoint, BytePool.RentMemory toBeShared, long currentSystemMics)
    {// Checked: endpoint, toBeShared.Length
        if (this.CurrentState == State.Disposed)
        {
            return;
        }

        // PacketHeaderCode
        var span = toBeShared.Span.Slice(RelayHeader.RelayIdLength); // SourceRelayId/DestinationRelayId
        var salt4 = BitConverter.ToUInt32(span); // Salt
        span = span.Slice(sizeof(uint));

        var packetType = (PacketType)BitConverter.ToUInt16(span); // PacketType
        span = span.Slice(10);

        // span: frame
        if (span.Length == 0)
        {// Close 1 (Obsolete)
            // this.ConnectionTerminal.CloseInternal(this, false);
            return;
        }

        if (packetType == PacketType.Protected || packetType == PacketType.ProtectedResponse)
        {// ProtectedPacketCode
            if (span.Length < sizeof(ulong))
            {
                return;
            }

            var nonce8 = BitConverter.ToUInt64(span); // Nonce
            span = span.Slice(8);
            if (!this.TryDecrypt(salt4, nonce8, span, PacketPool.MaxPacketSize - PacketHeader.Length, out var written))
            {
                return;
            }

            if (written < 2)
            {
                return;
            }

            var rentMemory = toBeShared.Slice(PacketHeader.Length + ProtectedPacket.Length + 2, written - 2);
            var frameType = (FrameType)BitConverter.ToUInt16(span); // FrameType
            if (frameType == FrameType.Close)
            {// Close 2
                this.ConnectionTerminal.CloseInternal(this, false);
            }
            else if (frameType == FrameType.Ack)
            {// Ack
                this.ProcessReceive_Ack(endpoint, rentMemory);
            }
            else if (frameType == FrameType.FirstGene)
            {// FirstGene
                this.ProcessReceive_FirstGene(endpoint, rentMemory);
            }
            else if (frameType == FrameType.FollowingGene)
            {// FollowingGene
                this.ProcessReceive_FollowingGene(endpoint, rentMemory);
            }
            else if (frameType == FrameType.Knock)
            {// Knock
                this.ProcessReceive_Knock(endpoint, rentMemory);
            }
            else if (frameType == FrameType.KnockResponse)
            {// KnockResponse
                this.ProcessReceive_KnockResponse(endpoint, rentMemory);
            }
        }
    }

    internal void ProcessReceive_Ack(NetEndpoint endPoint, BytePool.RentMemory toBeShared)
    {// uint TransmissionId, ushort NumberOfPairs, { int StartGene, int EndGene } x pairs
        var span = toBeShared.Span;
        using (this.sendTransmissions.LockObject.EnterScope())
        {
            while (span.Length >= 8)
            {
                var maxReceivePosition = BitConverter.ToInt32(span);
                span = span.Slice(sizeof(int));
                var transmissionId = BitConverter.ToUInt32(span);
                span = span.Slice(sizeof(uint));

                if (maxReceivePosition < 0)
                {// Burst (Complete)
                    if (this.sendTransmissions.TransmissionIdChain.TryGetValue(transmissionId, out var transmission))
                    {
                        this.UpdateAckedNode(transmission);
                        this.UpdateLastEventMics();
                        transmission.ProcessReceive_AckBurst();
                    }
                    else
                    {// SendTransmission has already been disposed due to reasons such as having already received response data.
                    }
                }
                else
                {// Block/Stream
                    if (span.Length < 6)
                    {
                        break;
                    }

                    var successiveReceivedPosition = BitConverter.ToInt32(span);
                    span = span.Slice(sizeof(int));
                    var numberOfPairs = BitConverter.ToUInt16(span);
                    span = span.Slice(sizeof(ushort));

                    var length = numberOfPairs * 8;
                    if (span.Length < length)
                    {
                        break;
                    }

                    if (this.sendTransmissions.TransmissionIdChain.TryGetValue(transmissionId, out var transmission))
                    {
                        this.UpdateAckedNode(transmission);
                        this.UpdateLastEventMics();
                        transmission.ProcessReceive_AckBlock(maxReceivePosition, successiveReceivedPosition, span, numberOfPairs);
                    }
                    else
                    {// SendTransmission has already been disposed due to reasons such as having already received response data.
                    }

                    span = span.Slice(length);
                }
            }

            Debug.Assert(span.Length == 0);
        }
    }

    internal void ProcessReceive_FirstGene(NetEndpoint endPoint, BytePool.RentMemory toBeShared)
    {// First gene
        var span = toBeShared.Span;
        if (span.Length < FirstGeneFrame.LengthExcludingFrameType)
        {
            return;
        }

        // FirstGeneFrameCode
        var transmissionMode = BitConverter.ToUInt16(span);
        span = span.Slice(sizeof(ushort)); // 2
        var transmissionId = BitConverter.ToUInt32(span);
        span = span.Slice(sizeof(uint)); // 4
        var dataControl = (DataControl)BitConverter.ToUInt16(span);
        span = span.Slice(sizeof(ushort)); // 2
        var rttHint = BitConverter.ToInt32(span);
        span = span.Slice(sizeof(int)); // 4
        var totalGenes = BitConverter.ToInt32(span);

        if (rttHint > 0)
        {
            this.AddRtt(rttHint);
        }

        ReceiveTransmission? transmission;
        long maxStreamLength = 0;
        ulong dataId = 0;
        using (this.receiveTransmissions.LockObject.EnterScope())
        {
            if (this.IsClient)
            {// Client side
                if (!this.receiveTransmissions.TransmissionIdChain.TryGetValue(transmissionId, out transmission))
                {// On the client side, it's necessary to create ReceiveTransmission in advance.
                    return;
                }
                else if (transmission.Mode != NetTransmissionMode.Initial)
                {// Processing the first packet is limited to the initial state, as the state gets cleared.
                    this.ConnectionTerminal.AckQueue.AckBlock(this, transmission, 0); // Resend the ACK in case it was not received.
                    return;
                }

                if (transmissionMode == 0 && totalGenes <= this.Agreement.MaxBlockGenes)
                {// Block mode
                    transmission.SetState_Receiving(totalGenes);
                }
                else if (transmissionMode == 1)
                {// Stream mode
                    maxStreamLength = BitConverter.ToInt64(span);
                    span = span.Slice(sizeof(int) + sizeof(uint)); // 8
                    dataId = BitConverter.ToUInt64(span);

                    if (!this.Agreement.CheckStreamLength(maxStreamLength))
                    {
                        return;
                    }

                    transmission.SetState_ReceivingStream(maxStreamLength);
                }
                else
                {
                    return;
                }
            }
            else
            {// Server side
                if (this.receiveTransmissions.TransmissionIdChain.TryGetValue(transmissionId, out transmission))
                {// The same TransmissionId already exists.
                    this.ConnectionTerminal.AckQueue.AckBlock(this, transmission, 0); // Resend the ACK in case it was not received.
                    return;
                }

                this.CleanReceiveTransmission();

                // New transmission
                if (this.receiveReceivedList.Count >= this.Agreement.MaxTransmissions)
                {// Maximum number reached.
                    return;
                }

                if (transmissionMode == 0 && totalGenes <= this.Agreement.MaxBlockGenes)
                {// Block mode
                    transmission = new(this, transmissionId, default);
                    transmission.SetState_Receiving(totalGenes);
                }
                else if (transmissionMode == 1)
                {// Stream mode
                    maxStreamLength = BitConverter.ToInt64(span);
                    span = span.Slice(sizeof(int) + sizeof(uint)); // 8
                    dataId = BitConverter.ToUInt64(span);

                    if (!this.Agreement.CheckStreamLength(maxStreamLength))
                    {
                        return;
                    }

                    transmission = new(this, transmissionId, default);
                    transmission.SetState_ReceivingStream(maxStreamLength);
                }
                else
                {
                    return;
                }

                transmission.Goshujin = this.receiveTransmissions;
                transmission.ReceivedOrDisposedMics = Mics.FastSystem; // Received mics
                transmission.ReceivedOrDisposedNode = this.receiveReceivedList.AddLast(transmission);
            }
        }

        this.UpdateLastEventMics();

        // FirstGeneFrameCode (DataKind + DataId + Data...)
        transmission.ProcessReceive_Gene(dataControl, 0, toBeShared.Slice(16));

        if (transmission.Mode == NetTransmissionMode.Stream)
        {// Invoke stream
            if (this is ServerConnection serverConnection)
            {
                serverConnection.GetContext().InvokeStream(transmission, dataId, maxStreamLength);
            }
            else if (this is ClientConnection clientConnection)
            {
                transmission.StartStream(dataId, maxStreamLength);
            }
        }
    }

    internal void ProcessReceive_FollowingGene(NetEndpoint endPoint, BytePool.RentMemory toBeShared)
    {// Following gene
        var span = toBeShared.Span;
        if (span.Length < FollowingGeneFrame.LengthExcludingFrameType)
        {
            return;
        }

        // FollowingGeneFrameCode
        var transmissionId = BitConverter.ToUInt32(span);
        span = span.Slice(sizeof(uint));
        var dataControl = (DataControl)BitConverter.ToUInt16(span);
        span = span.Slice(sizeof(ushort)); // 2

        var dataPosition = BitConverter.ToInt32(span);
        if (dataPosition == 0)
        {
            return;
        }

        ReceiveTransmission? transmission;
        using (this.receiveTransmissions.LockObject.EnterScope())
        {
            if (!this.receiveTransmissions.TransmissionIdChain.TryGetValue(transmissionId, out transmission))
            {// No transmission
                return;
            }

            // ReceiveTransmissionsCode
            if (transmission.ReceivedOrDisposedNode is { } node &&
                node.List == this.receiveReceivedList)
            {
                transmission.ReceivedOrDisposedMics = Mics.FastSystem; // Received mics
                this.receiveReceivedList.MoveToLast(node);
            }
        }

        this.UpdateLastEventMics();
        transmission.ProcessReceive_Gene(dataControl, dataPosition, toBeShared.Slice(FollowingGeneFrame.LengthExcludingFrameType));
    }

    internal void ProcessReceive_Knock(NetEndpoint endPoint, BytePool.RentMemory toBeShared)
    {// KnockResponseFrameCode
        if (toBeShared.Memory.Length < (KnockFrame.Length - 2))
        {
            return;
        }

        ReceiveTransmission? transmission;
        var transmissionId = BitConverter.ToUInt32(toBeShared.Span);
        using (this.receiveTransmissions.LockObject.EnterScope())
        {
            if (!this.receiveTransmissions.TransmissionIdChain.TryGetValue(transmissionId, out transmission))
            {
                return;
            }
        }

        Span<byte> frame = stackalloc byte[KnockResponseFrame.Length];
        var span = frame;
        BitConverter.TryWriteBytes(span, (ushort)FrameType.KnockResponse);
        span = span.Slice(sizeof(ushort));
        BitConverter.TryWriteBytes(span, transmission.TransmissionId);
        span = span.Slice(sizeof(uint));
        BitConverter.TryWriteBytes(span, transmission.MaxReceivePosition);
        span = span.Slice(sizeof(int));

        this.SendPriorityFrame(frame);
    }

    internal void ProcessReceive_KnockResponse(NetEndpoint endPoint, BytePool.RentMemory toBeShared)
    {// KnockResponseFrameCode
        var span = toBeShared.Span;
        if (span.Length < (KnockResponseFrame.Length - 2))
        {
            return;
        }

        var transmissionId = BitConverter.ToUInt32(span);
        span = span.Slice(sizeof(uint));
        using (this.sendTransmissions.LockObject.EnterScope())
        {
            if (this.sendTransmissions.TransmissionIdChain.TryGetValue(transmissionId, out var transmission))
            {
                var maxReceivePosition = BitConverter.ToInt32(span);
                span = span.Slice(sizeof(int));

                transmission.MaxReceivePosition = maxReceivePosition;

                this.Logger.TryGet(LogLevel.Debug)?.Log($"KnockResponse: {maxReceivePosition}");
            }
        }
    }

    internal bool CreatePacket(scoped ReadOnlySpan<byte> frame, out BytePool.RentMemory rentArray)
    {// ProtectedPacketCode
        Debug.Assert(frame.Length > 0);
        if (frame.Length > PacketHeader.MaxFrameLength)
        {
            rentArray = default;
            return false;
        }

        var packetType = this is ClientConnection ? PacketType.Protected : PacketType.ProtectedResponse;
        var arrayOwner = PacketPool.Rent();
        var span = arrayOwner.AsSpan();
        var salt4 = RandomVault.Default.NextUInt32();

        // PacketHeaderCode, CreatePacketCode
        BitConverter.TryWriteBytes(span, (RelayId)0); // SourceRelayId
        span = span.Slice(sizeof(RelayId));
        BitConverter.TryWriteBytes(span, this.DestinationEndpoint.RelayId); // DestinationRelayId
        span = span.Slice(sizeof(RelayId));

        BitConverter.TryWriteBytes(span, salt4); // Salt
        span = span.Slice(sizeof(uint));

        BitConverter.TryWriteBytes(span, (ushort)packetType); // PacketType
        span = span.Slice(sizeof(ushort));

        BitConverter.TryWriteBytes(span, this.ConnectionId); // Id
        span = span.Slice(sizeof(ulong));

        var nonce8 = RandomVault.Default.NextUInt64();
        BitConverter.TryWriteBytes(span, nonce8); // Nonce8
        span = span.Slice(sizeof(ulong));

        this.Encrypt(salt4, nonce8, frame, arrayOwner.AsSpan(PacketHeader.Length + ProtectedPacket.Length), out var written);
        rentArray = arrayOwner.AsMemory(0, PacketHeader.Length + ProtectedPacket.Length + written);
        return true;
    }

    internal void CreatePacket(scoped Span<byte> frameHeader, scoped ReadOnlySpan<byte> frameContent, out BytePool.RentMemory rentMemory)
    {// ProtectedPacketCode
        Debug.Assert((frameHeader.Length + frameContent.Length) <= PacketHeader.MaxFrameLength);

        var packetType = this is ClientConnection ? PacketType.Protected : PacketType.ProtectedResponse;
        var arrayOwner = PacketPool.Rent();
        var span = arrayOwner.AsSpan();
        var salt4 = RandomVault.Default.NextUInt32();

        // PacketHeaderCode, CreatePacketCode
        BitConverter.TryWriteBytes(span, (RelayId)0); // SourceRelayId
        span = span.Slice(sizeof(RelayId));
        BitConverter.TryWriteBytes(span, this.DestinationEndpoint.RelayId); // DestinationRelayId
        span = span.Slice(sizeof(RelayId));

        BitConverter.TryWriteBytes(span, salt4); // Salt
        span = span.Slice(sizeof(uint));

        BitConverter.TryWriteBytes(span, (ushort)packetType); // PacketType
        span = span.Slice(sizeof(ushort));

        BitConverter.TryWriteBytes(span, this.ConnectionId); // Id
        span = span.Slice(sizeof(ulong));

        var nonce8 = RandomVault.Default.NextUInt64();
        BitConverter.TryWriteBytes(span, nonce8); // Nonce8
        span = span.Slice(sizeof(ulong));

        frameHeader.CopyTo(span);
        span = span.Slice(frameHeader.Length);
        frameContent.CopyTo(span);

        span = arrayOwner.Array.AsSpan(PacketHeader.Length + ProtectedPacket.Length);
        this.Encrypt(salt4, nonce8, span.Slice(0, frameHeader.Length + frameContent.Length), span, out var written);

        rentMemory = arrayOwner.AsMemory(0, PacketHeader.Length + ProtectedPacket.Length + written);
    }

    internal void CreateAckPacket(BytePool.RentArray rentArray, int length, out int packetLength)
    {// ProtectedPacketCode
        var packetType = this is ClientConnection ? PacketType.Protected : PacketType.ProtectedResponse;
        var span = rentArray.AsSpan();
        var salt4 = RandomVault.Default.NextUInt32();

        // PacketHeaderCode, CreatePacketCode
        BitConverter.TryWriteBytes(span, (RelayId)0); // SourceRelayId
        span = span.Slice(sizeof(RelayId));
        BitConverter.TryWriteBytes(span, this.DestinationEndpoint.RelayId); // RelayId
        span = span.Slice(sizeof(RelayId));

        BitConverter.TryWriteBytes(span, salt4); // Salt
        span = span.Slice(sizeof(uint));

        BitConverter.TryWriteBytes(span, (ushort)packetType); // PacketType
        span = span.Slice(sizeof(ushort));

        BitConverter.TryWriteBytes(span, this.ConnectionId); // Id
        span = span.Slice(sizeof(ulong));

        var nonce8 = RandomVault.Default.NextUInt64();
        BitConverter.TryWriteBytes(span, nonce8); // Nonce8
        span = span.Slice(sizeof(ulong));

        BitConverter.TryWriteBytes(span, (ushort)FrameType.Ack); // Frame type

        this.Encrypt(salt4, nonce8, span.Slice(0, sizeof(ushort) + length), span, out var written);
        packetLength = PacketHeader.Length + ProtectedPacket.Length + written;
    }

    /// <inheritdoc/>
    public virtual void Dispose()
    {// Close the connection, but defer actual disposal for reuse.
        this.ConnectionTerminal.CloseInternal(this, true);
    }

    public override string ToString()
    {
        var connectionString = "Connection";
        if (this is ServerConnection)
        {
            connectionString = "Server";
        }
        else if (this is ClientConnection)
        {
            connectionString = "Client";
        }

        return $"{connectionString} Id:{(ushort)this.ConnectionId:x4}, EndPoint:{this.DestinationEndpoint.ToString()}, Delivery:{this.DeliveryRatio.ToString("F2")} ({this.SendCount}/{this.SendCount + this.ResendCount})";
    }

    internal void Encrypt(uint salt4, ulong nonce8, ReadOnlySpan<byte> source, Span<byte> destination, out int written)
    {
        Debug.Assert(destination.Length >= (source.Length + ProtectedPacket.TagSize));

        Span<byte> nonce = stackalloc byte[32];
        this.CreateNonce(salt4, nonce8, nonce);

        written = source.Length + ProtectedPacket.TagSize;
        Aegis256.Encrypt(destination.Slice(0, written), source, nonce, this.EmbryoKey);
    }

    internal bool TryDecrypt(uint salt4, ulong nonce8, Span<byte> span, int spanMax, out int written)
    {
        Span<byte> nonce = stackalloc byte[32];
        this.CreateNonce(salt4, nonce8, nonce);

        written = span.Length - ProtectedPacket.TagSize;
        return Aegis256.TryDecrypt(span[..^ProtectedPacket.TagSize], span, nonce, this.EmbryoKey);
    }

    internal void CloseAllTransmission()
    {
        using (this.sendTransmissions.LockObject.EnterScope())
        {
            foreach (var x in this.sendTransmissions)
            {
                if (x.IsDisposed)
                {
                    continue;
                }

                x.DisposeTransmission();
                // x.Goshujin = null;
            }

            // Since it's within a lock statement, manually clear it.
            this.sendTransmissions.TransmissionIdChain.Clear();
        }

        using (this.receiveTransmissions.LockObject.EnterScope())
        {
            foreach (var x in this.receiveTransmissions)
            {
                x.DisposeTransmission();

                if (x.ReceivedOrDisposedNode is { } node)
                {
                    node.List.Remove(node);
                    x.ReceivedOrDisposedNode = null;
                }

                // x.Goshujin = null;
            }

            // Since it's within a lock statement, manually clear it.
            // ReceiveTransmissionsCode
            this.receiveTransmissions.TransmissionIdChain.Clear();
            this.receiveReceivedList.Clear();
            this.receiveDisposedList.Clear();
        }
    }

    internal void CloseSendTransmission()
    {
        using (this.sendTransmissions.LockObject.EnterScope())
        {
            foreach (var x in this.sendTransmissions)
            {
                if (x.IsDisposed)
                {
                    continue;
                }

                x.DisposeTransmission();
                // x.Goshujin = null;
            }

            // Since it's within a lock statement, manually clear it.
            this.sendTransmissions.TransmissionIdChain.Clear();
        }
    }

    internal virtual void OnStateChanged()
    {
        if (this.CurrentState == State.Disposed)
        {
        }
    }

    internal void TerminateInternal()
    {
        Queue<SendTransmission>? sendQueue = default;
        using (this.sendTransmissions.LockObject.EnterScope())
        {
            foreach (var x in this.sendTransmissions)
            {
                if (x.Mode == NetTransmissionMode.Stream ||
                    x.Mode == NetTransmissionMode.StreamCompleted)
                {// Terminate stream transmission.
                    x.DisposeTransmission();
                }

                if (x.IsDisposed)
                {
                    sendQueue ??= new();
                    sendQueue.Enqueue(x);
                }
            }

            if (sendQueue is not null)
            {
                while (sendQueue.TryDequeue(out var t))
                {
                    t.Goshujin = default;
                }
            }
        }

        Queue<ReceiveTransmission>? receiveQueue = default;
        using (this.receiveTransmissions.LockObject.EnterScope())
        {
            foreach (var x in this.receiveTransmissions)
            {
                if (x.Mode == NetTransmissionMode.Stream ||
                    x.Mode == NetTransmissionMode.StreamCompleted)
                {// Terminate stream transmission.
                    x.DisposeTransmission();
                }

                if (x.IsDisposed)
                {
                    receiveQueue ??= new();
                    receiveQueue.Enqueue(x);
                }
            }

            if (receiveQueue is not null)
            {
                while (receiveQueue.TryDequeue(out var t))
                {
                    t.Goshujin = default;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CreateNonce(uint salt4, ulong nonce8, Span<byte> nonce)
    {
        Debug.Assert(nonce.Length == 32);

        var s = nonce;
        MemoryMarshal.Write(s, salt4);
        s = s.Slice(sizeof(uint));
        MemoryMarshal.Write(s, salt4);
        s = s.Slice(sizeof(uint));
        MemoryMarshal.Write(s, nonce8);
        s = s.Slice(sizeof(ulong));
        MemoryMarshal.Write(s, this.ConnectionId);
        s = s.Slice(sizeof(ulong));
        MemoryMarshal.Write(s, this.EmbryoSecret);
        s = s.Slice(sizeof(ulong));
    }
}

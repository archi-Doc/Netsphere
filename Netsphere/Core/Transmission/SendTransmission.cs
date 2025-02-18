// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Collections;
using Netsphere.Packet;

namespace Netsphere.Core;

public enum NetTransmissionMode
{
    Initial,
    Burst,
    Block,
    Stream,
    StreamCompleted,
    Disposed,
}

[ValueLinkObject(Isolation = IsolationLevel.Serializable, Restricted = true)]
internal sealed partial class SendTransmission : IDisposable
{
    private const int CongestionControlThreshold = 5;

    /* State transitions
     *  SendAndReceiveAsync (Client) : Initial -> Send/Receive Ack -> Receive -> Disposed
     *  SendAsync                   (Client) : Initial -> Send/Receive Ack -> tcs / Disposed
     *  (Server) : Initial -> Receive -> (Invoke) -> Disposed
     *  (Server) : Initial -> Receive -> (Invoke) -> Send/Receive Ack -> tcs / Disposed
     */

    [Link(Type = ChainType.LinkedList, Name = "AckedList")]
    public SendTransmission(Connection connection, uint transmissionId)
    {// lock (Connection.sendTransmissions.SyncObject)
        this.Connection = connection;
        this.TransmissionId = transmissionId;
        this.AckedMics = Mics.FastSystem;
    }

    #region FieldAndProperty

    public Connection Connection { get; }

    [Link(Primary = true, Type = ChainType.Unordered)]
    public uint TransmissionId { get; }

    public string TransmissionIdText
        => ((ushort)this.TransmissionId).ToString("x4");

    public NetTransmissionMode Mode { get; private set; } // using (this.lockObject.EnterScope())

    public int GeneSerialMax { get; private set; }

    internal bool IsDisposed
        => this.Mode == NetTransmissionMode.Disposed;

    internal TaskCompletionSource<NetResult>? SentTcs
        => this.sentTcs;

#pragma warning disable SA1401 // Fields should be private

    internal UnorderedLinkedList<SendTransmission>.Node? SendNode; // lock (ConnectionTerminal.SyncSend)
    internal int MaxReceivePosition;

    // internal UnorderedLinkedList<SendTransmission>.Node? AckedNode; // lock (Connection.sendTransmissions.SyncObject)
    internal long AckedMics;

#pragma warning restore SA1401 // Fields should be private

    private readonly Lock lockObject = new();
    private TaskCompletionSource<NetResult>? sentTcs;
    private SendGene? gene0; // Gene 0
    private SendGene? gene1; // Gene 1
    private SendGene? gene2; // Gene 2
    private SendGene.GoshujinClass? genes; // Multiple genes
    private int sendGeneSerial;
    private int lastLossPosition;
    private long lastLossMics;

    #endregion

    // private int skip19 = 0;//

    public void Dispose()
    {
        if (this.IsDisposed)
        {
            return;
        }

        this.Connection.RemoveTransmission(this);
        this.DisposeTransmission();
    }

    internal void DisposeTransmission()
    {
        using (this.lockObject.EnterScope())
        {
            this.DisposeInternal();
        }
    }

    internal void DisposeInternal()
    {// using (this.lockObject.EnterScope())
        // Console.WriteLine($"Dispose send transmission: {this.Connection.ToString()} {this.TransmissionIdText} {this.Mode.ToString()} {this.GeneSerialMax}");
        if (this.IsDisposed)
        {
            return;
        }

        this.Mode = NetTransmissionMode.Disposed;
        this.gene0?.Dispose(false);
        this.gene1?.Dispose(false);
        this.gene2?.Dispose(false);
        if (this.genes is not null)
        {
            foreach (var x in this.genes)
            {
                x.DisposeMemory();
            }

            this.genes = default; // this.genes.Clear();
        }

        if (this.sentTcs is not null)
        {
            this.sentTcs.SetResult(NetResult.Closed);
            this.sentTcs = null;
        }
    }

    internal async Task<NetResult> Wait(Task<NetResult> task, int timeoutInMilliseconds, CancellationToken cancellationToken)
    {// I don't think this is a smart approach, but...
        var remainingMilliseconds = timeoutInMilliseconds;
        while (true)
        {
            if (!this.Connection.NetTerminal.IsActive)
            {// NetTerminal
                return NetResult.Closed;
            }

            if (!this.Connection.IsActive)
            {// Connection
                return NetResult.Closed;
            }

            if (this.IsDisposed)
            {// Transmission
                return NetResult.Closed;
            }

            try
            {
                var result = await task.WaitAsync(NetConstants.WaitIntervalTimeSpan, cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (TimeoutException)
            {
                if (remainingMilliseconds < 0)
                {// Wait indefinitely.
                }
                else if (remainingMilliseconds > NetConstants.WaitIntervalMilliseconds)
                {// Reduce the time and continue waiting.
                    remainingMilliseconds -= NetConstants.WaitIntervalMilliseconds;
                }
                else
                {// Timeout
                    return NetResult.Timeout;
                }
            }
        }
    }

    internal ProcessSendResult ProcessSingleSend(NetSender netSender)
    {// lock (this.ConnectionTerminal.SyncSend)
        using (this.lockObject.EnterScope())
        {
            if (this.Mode == NetTransmissionMode.Burst)
            {
                if (this.gene0?.CurrentState == SendGene.State.Initial)
                {
                    if (!this.gene0.Send_NotThreadSafe(netSender, 0))
                    {// Cannot send
                        return ProcessSendResult.Complete;
                    }
                }

                if (this.gene1?.CurrentState == SendGene.State.Initial)
                {
                    if (!this.gene1.Send_NotThreadSafe(netSender, 1))
                    {// Cannot send
                        return ProcessSendResult.Complete;
                    }
                }

                if (this.gene2?.CurrentState == SendGene.State.Initial)
                {
                    if (!this.gene2.Send_NotThreadSafe(netSender, 2))
                    {// Cannot send
                        return ProcessSendResult.Complete;
                    }
                }

                return ProcessSendResult.Complete;
            }
            else if (this.genes is not null)
            {// Block or Stream
                while (this.sendGeneSerial < this.GeneSerialMax)
                {
                    if (this.genes.GeneSerialListChain.Get(this.sendGeneSerial++) is { } gene)
                    {
                        if (gene.CurrentState == SendGene.State.Initial)
                        {
                            /*var doNotTransmit = false;//
                            if (gene.GeneSerial > 0 && gene.GeneSerial < 10)
                            {
                                if (this.skip19++ < 9)
                                {
                                    doNotTransmit = true;
                                }
                            }*/

                            if (!gene.Send_NotThreadSafe(netSender, 0))
                            {// Cannot send
                                return ProcessSendResult.Complete;
                            }

                            return this.sendGeneSerial >= this.GeneSerialMax ? ProcessSendResult.Complete : ProcessSendResult.Remaining;
                        }
                    }
                }

                return ProcessSendResult.Complete;
            }
            else
            {
                return ProcessSendResult.Complete;
            }
        }
    }

    internal NetResult SendBlock(uint dataKind, ulong dataId, BytePool.RentMemory block, TaskCompletionSource<NetResult>? sentTcs)
    {
        using (this.lockObject.EnterScope())
        {
            if (this.Connection.IsClosedOrDisposed ||
                this.Mode != NetTransmissionMode.Initial)
            {
                return NetResult.Closed;
            }

            var info = NetHelper.CalculateGene(block.Span.Length);
            this.Connection.UpdateLastEventMics();
            this.sentTcs = sentTcs;

            var span = block.Span;
            if (info.NumberOfGenes <= NetHelper.BurstGenes)
            {// Burst
                this.Mode = NetTransmissionMode.Burst;
                if (this.Connection.SendTransmissionsCount >= CongestionControlThreshold)
                {// Enable congestion control when the number of SendTransmissions exceeds the threshold.
                    this.Connection.CreateCongestionControl();
                }

                if (info.NumberOfGenes == 1)
                {// gene0
                    this.GeneSerialMax = info.NumberOfGenes;
                    this.gene0 = new(this);

                    this.CreateFirstPacket_Block(info.NumberOfGenes, dataKind, dataId, span, out var rentMemory);
                    this.gene0.SetSend(rentMemory);
                }
                else if (info.NumberOfGenes == 2)
                {// gene0, gene1
                    this.GeneSerialMax = info.NumberOfGenes;
                    this.gene0 = new(this);
                    this.gene1 = new(this);

                    this.CreateFirstPacket_Block(info.NumberOfGenes, dataKind, dataId, span.Slice(0, (int)info.FirstGeneSize), out var rentMemory);
                    this.gene0.SetSend(rentMemory);

                    span = span.Slice((int)info.FirstGeneSize);
                    Debug.Assert(span.Length == info.LastGeneSize);
                    this.CreateFollowingPacket(DataControl.Valid, 1, span, out rentMemory);
                    this.gene1.SetSend(rentMemory);
                }
                else if (info.NumberOfGenes == 3)
                {// gene0, gene1, gene2
                    this.GeneSerialMax = info.NumberOfGenes;
                    this.gene0 = new(this);
                    this.gene1 = new(this);
                    this.gene2 = new(this);

                    this.CreateFirstPacket_Block(info.NumberOfGenes, dataKind, dataId, span.Slice(0, (int)info.FirstGeneSize), out var rentMemory);
                    this.gene0.SetSend(rentMemory);

                    span = span.Slice((int)info.FirstGeneSize);
                    this.CreateFollowingPacket(DataControl.Valid, 1, span.Slice(0, FollowingGeneFrame.MaxGeneLength), out rentMemory);
                    this.gene1.SetSend(rentMemory);

                    span = span.Slice(FollowingGeneFrame.MaxGeneLength);
                    Debug.Assert(span.Length == info.LastGeneSize);
                    this.CreateFollowingPacket(DataControl.Valid, 2, span, out rentMemory);
                    this.gene2.SetSend(rentMemory);
                }
                else
                {
                    return NetResult.UnknownError;
                }
            }
            else
            {// Multiple genes
                if (info.NumberOfGenes > this.Connection.Agreement.MaxBlockGenes)
                {
                    return NetResult.BlockSizeLimit;
                }

                this.Mode = NetTransmissionMode.Block;
                this.Connection.CreateCongestionControl();

                this.GeneSerialMax = info.NumberOfGenes;
                this.genes = new();
                this.genes.GeneSerialListChain.Resize(info.NumberOfGenes);

                var firstGene = new SendGene(this);
                this.CreateFirstPacket_Block(info.NumberOfGenes, dataKind, dataId, span.Slice(0, (int)info.FirstGeneSize), out var rentMemory);
                firstGene.SetSend(rentMemory);
                span = span.Slice((int)info.FirstGeneSize);
                firstGene.Goshujin = this.genes;
                this.genes.GeneSerialListChain.Add(firstGene);

                for (var i = 1; i < info.NumberOfGenes; i++)
                {
                    var size = (int)(i == info.NumberOfGenes - 1 ? info.LastGeneSize : FollowingGeneFrame.MaxGeneLength);
                    var gene = new SendGene(this);
                    this.CreateFollowingPacket(DataControl.Valid, i, span.Slice(0, size), out rentMemory);
                    gene.SetSend(rentMemory);

                    span = span.Slice(size);
                    gene.Goshujin = this.genes;
                    this.genes.GeneSerialListChain.Add(gene);
                }

                Debug.Assert(span.Length == 0);
            }
        }

        this.Connection.AddSend(this);

        return NetResult.Success;
    }

    internal NetResult SendStream(long maxLength)
    {
        using (this.lockObject.EnterScope())
        {
            if (this.Connection.IsClosedOrDisposed ||
                this.Mode != NetTransmissionMode.Initial)
            {
                return NetResult.Closed;
            }

            this.Connection.UpdateLastEventMics();
            this.Mode = NetTransmissionMode.Stream;
            this.Connection.CreateCongestionControl();
            this.sentTcs = new TaskCompletionSource<NetResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            this.GeneSerialMax = 0;
            this.genes = new();

            var info = NetHelper.CalculateGene(maxLength);
            var bufferGenes = Math.Min(this.Connection.Agreement.StreamBufferGenes, info.NumberOfGenes + 1); // +1 for last complete gene.
            this.genes.GeneSerialListChain.Resize(bufferGenes);
            this.MaxReceivePosition = bufferGenes;
        }

        return NetResult.Success;
    }

    internal bool TrySendControl(SendStreamBase stream, DataControl dataControl)
    {
        if (!this.Connection.IsActive)
        {
            return false;
        }

        using (this.lockObject.EnterScope())
        {
            if (this.Mode != NetTransmissionMode.Stream)
            {
                return false;
            }
            else
            {// Stream -> StreamCompleted
                this.Mode = NetTransmissionMode.StreamCompleted;
            }

            var chain = this.genes?.GeneSerialListChain;
            if (chain is null)
            {
                return false;
            }

            if (this.GeneSerialMax >= this.MaxReceivePosition || !chain.CanAdd)
            {
                return false;
            }

            var gene = new SendGene(this);
            BytePool.RentMemory rentMemory;
            if (this.GeneSerialMax == 0)
            {// First gene
                this.CreateFirstPacket_Stream(dataControl, 0, stream.DataId, ReadOnlySpan<byte>.Empty, out rentMemory);
            }
            else
            {// Following gene
                this.CreateFollowingPacket(dataControl, this.GeneSerialMax, ReadOnlySpan<byte>.Empty, out rentMemory);
            }

            gene.SetSend(rentMemory);
            gene.Goshujin = this.genes;
            chain.Add(gene);
        }

        this.Connection.AddSend(this);
        return true;
    }

    internal async Task<NetResult> ProcessSend(SendStreamBase stream, DataControl dataControl, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (this.Mode != NetTransmissionMode.Stream)
        {
            // return this.Mode == NetTransmissionMode.StreamCompleted ? NetResult.Completed : NetResult.Closed;
            if (this.Mode == NetTransmissionMode.StreamCompleted)
            {
                return buffer.Length == 0 ? NetResult.Success : NetResult.Completed;
            }
            else
            {
                return NetResult.Closed;
            }
        }

        var addSend = false;
        while (true)
        {
            var delay = NetConstants.InitialSendStreamDelayMilliseconds;

Loop:
            if (!this.Connection.IsActive)
            {
                return NetResult.Closed;
            }

            if (this.MaxReceivePosition == 0)
            {// MaxReceivePosition becomes 0 if the server's ReceiveTransmission is disposed.
                return NetResult.Canceled;
            }
            else if (this.GeneSerialMax >= this.MaxReceivePosition)
            {
                SendKnockFrame();

                // await Console.Out.WriteLineAsync($"Delay {this.MaxReceivePosition}/{this.GeneSerialMax}");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = Math.Min(delay << 1, NetConstants.MaxSendStreamDelayMilliseconds);

                if (this.Connection.CloseIfTransmissionHasTimedOut())
                {
                    return NetResult.Closed;
                }

                goto Loop;
            }

            using (this.lockObject.EnterScope())
            {
                if (this.Mode != NetTransmissionMode.Stream)
                {
                    // return this.Mode == NetTransmissionMode.StreamCompleted ? NetResult.Completed : NetResult.Closed;
                    if (this.Mode == NetTransmissionMode.StreamCompleted)
                    {
                        return buffer.Length == 0 ? NetResult.Success : NetResult.Completed;
                    }
                    else
                    {
                        return NetResult.Closed;
                    }
                }

                var chain = this.genes?.GeneSerialListChain;
                if (chain is null)
                {
                    return NetResult.Closed;
                }

                while (this.GeneSerialMax < this.MaxReceivePosition &&
                    chain.CanAdd)
                {
                    // Debug.Assert(chain.CanAdd); // Consumed < items.Length;
                    int size;
                    var gene = new SendGene(this);
                    BytePool.RentMemory rentMemory;
                    if (this.GeneSerialMax == 0)
                    {// First gene
                        size = Math.Min(buffer.Length, FirstGeneFrame.MaxGeneLength);
                        this.CreateFirstPacket_Stream(dataControl, stream.RemainingLength, stream.DataId, buffer.Slice(0, size).Span, out rentMemory);
                    }
                    else
                    {// Following gene
                        if (stream.RemainingLength > FollowingGeneFrame.MaxGeneLength)
                        {
                            size = Math.Min(buffer.Length, FollowingGeneFrame.MaxGeneLength);
                        }
                        else
                        {
                            size = Math.Min(buffer.Length, (int)stream.RemainingLength);
                        }

                        this.CreateFollowingPacket(dataControl, this.GeneSerialMax, buffer.Slice(0, size).Span, out rentMemory);
                    }

                    // Console.WriteLine($"packet: {size}");
                    gene.SetSend(rentMemory);
                    gene.Goshujin = this.genes;
                    chain.Add(gene);
                    addSend = true;
                    Debug.Assert(gene.GeneSerial == this.GeneSerialMax);

                    buffer = buffer.Slice(size);
                    this.GeneSerialMax++;
                    stream.RemainingLength -= size;
                    stream.SentLength += size;
                    if (stream.RemainingLength == 0 ||
                        dataControl == DataControl.Complete ||
                        dataControl == DataControl.Cancel)
                    {// Complete or Cancel
                        this.Mode = NetTransmissionMode.StreamCompleted;
                        goto Exit;
                    }
                    else if (buffer.Length == 0)
                    {
                        goto Exit;
                    }
                }
            }

            // AddSend
            if (addSend)
            {
                addSend = false;
                this.Connection.AddSend(this);
            }
        }

Exit:
        if (addSend)
        {
            this.Connection.AddSend(this);
        }

        return NetResult.Success;

        void SendKnockFrame()
        {// KnockFrameCode
            Span<byte> frame = stackalloc byte[KnockFrame.Length];
            var span = frame;
            BitConverter.TryWriteBytes(span, (ushort)FrameType.Knock);
            span = span.Slice(sizeof(ushort));
            BitConverter.TryWriteBytes(span, this.TransmissionId);
            span = span.Slice(sizeof(uint));
            this.Connection.SendPriorityFrame(frame);
        }
    }

    internal void ProcessReceive_AckBurst()
    {
        using (this.lockObject.EnterScope())
        {
            if (this.Mode != NetTransmissionMode.Burst)
            {
                return;
            }

            this.ProcessReceive_AckBurstInternal();
        }
    }

    internal void ProcessReceive_AckBurstInternal()
    {// using (this.lockObject.EnterScope())
        this.Connection.Logger.TryGet(LogLevel.Debug)?.Log($"{this.Connection.ConnectionIdText} ReceiveAck Burst {this.GeneSerialMax}");

        if (this.gene0 is not null)
        {
            if (this.gene0.CurrentState == SendGene.State.Sent)
            {// Exclude resent genes as they do not allow for accurate RTT measurement.
                this.Connection.AddRtt((int)(Mics.FastSystem - this.gene0.SentMics));
            }

            this.gene0.Dispose(true);
            this.gene0 = null;
        }

        if (this.gene1 is not null)
        {
            if (this.gene1.CurrentState == SendGene.State.Sent)
            {// Exclude resent genes as they do not allow for accurate RTT measurement.
                this.Connection.AddRtt((int)(Mics.FastSystem - this.gene1.SentMics));
            }

            this.gene1.Dispose(true);
            this.gene1 = null;
        }

        if (this.gene2 is not null)
        {
            if (this.gene2.CurrentState == SendGene.State.Sent)
            {// Exclude resent genes as they do not allow for accurate RTT measurement.
                this.Connection.AddRtt((int)(Mics.FastSystem - this.gene2.SentMics));
            }

            this.gene2.Dispose(true);
            this.gene2 = null;
        }

        /*if (sentCount != 0)
        {
            int rtt;
            if (sentCount == 1)
            {
                rtt = (int)(Mics.FastSystem - sentAccumulated);
            }
            else if (sentCount == 2)
            {
                rtt = (int)(Mics.FastSystem - (sentAccumulated >> 1));
            }
            else
            {
                rtt = (int)(Mics.FastSystem - (sentAccumulated / sentCount));
            }

            this.Connection.AddRtt(rtt);
            this.Connection.GetCongestionControl().AddRtt(rtt);
        }*/

        // Send transmission complete
        if (this.sentTcs is not null)
        {
            this.sentTcs.SetResult(NetResult.Success);
            this.sentTcs = null;
        }

        // Remove from sendTransmissions and dispose.
        this.Goshujin = null;
        this.DisposeInternal();
    }

    internal void ProcessReceive_AckBlock(int maxReceivePosition, int successiveReceivedPosition, scoped Span<byte> span, ushort numberOfPairs)
    {// using (SendTransmissions.lockObject.EnterScope())
        var completeFlag = false;
        int lossPosition = -1;
        var congestionControl = this.Connection.GetCongestionControl();
        using (this.lockObject.EnterScope())
        {
            // if (this.Mode == NetTransmissionMode.Burst)
            if (this.genes is null)
            {// Burst (Complete)
                this.ProcessReceive_AckBurstInternal();
                return;
            }

            this.MaxReceivePosition = maxReceivePosition;
            while (numberOfPairs-- > 0)
            {
                var startGene = BitConverter.ToInt32(span);
                span = span.Slice(sizeof(int));
                var endGene = BitConverter.ToInt32(span);
                span = span.Slice(sizeof(int));

                if (startGene < 0 || startGene >= this.GeneSerialMax ||
                    endGene < 0 || endGene > this.GeneSerialMax)
                {
                    continue;
                }

                // NetTransmissionMode.Block
                this.Connection.Logger.TryGet(LogLevel.Debug)?.Log($"{this.Connection.ConnectionIdText} ReceiveAck {startGene} - {endGene - 1}");
                var chain = this.genes.GeneSerialListChain;

                // [chain.StartPosition, successiveReceivedPosition)
                for (var i = chain.StartPosition; i < successiveReceivedPosition; i++)
                {
                    if (chain.Get(i) is { } gene)
                    {
                        if (gene.CurrentState == SendGene.State.Sent)
                        {// Exclude resent genes as they do not allow for accurate RTT measurement.
                            var rtt = (int)(Mics.FastSystem - gene.SentMics);

                            if (NetConstants.LogLowLevelNet)
                            {
                                // this.Connection.Logger.TryGet(LogLevel.Debug)?.Log($"ReceiveAck {gene.GeneSerial} {rtt} mics");
                            }

                            this.Connection.AddRtt(rtt);
                            congestionControl.AddRtt(rtt);
                        }

                        gene.Dispose(true); // this.genes.GeneSerialListChain.Remove(gene);
                    }
                }

                // Console.WriteLine($"ReceiveCapacity {receiveCapacity}");
                if (startGene < successiveReceivedPosition)
                {
                    startGene = successiveReceivedPosition - 1;
                }

                // [startGene, endGene)
                for (var i = startGene; i < endGene; i++)
                {
                    if (chain.Get(i) is { } gene)
                    {
                        if (gene.CurrentState == SendGene.State.Sent)
                        {// Exclude resent genes as they do not allow for accurate RTT measurement.
                            var rtt = (int)(Mics.FastSystem - gene.SentMics);

                            if (NetConstants.LogLowLevelNet)
                            {
                                // this.Connection.Logger.TryGet(LogLevel.Debug)?.Log($"ReceiveAck {gene.GeneSerial} {rtt} mics");
                            }

                            this.Connection.AddRtt(rtt);
                            congestionControl.AddRtt(rtt);
                        }

                        gene.Dispose(true); // this.genes.GeneSerialListChain.Remove(gene);
                    }
                }

                if (endGene - chain.StartPosition > 3)
                {// Loss detection: Packet Threshold kPacketThreshold 3 (RFC5681, RFC6675)
                    lossPosition = startGene;
                }

                /*else if (dif >= 1)
                {// Time Threshold
                    var threshold = (Math.Max(this.Connection.SmoothedRtt, this.Connection.LatestRtt) * 9) >> 3;
                    if (chain.StartPosition > 0 &&
                        chain.Get(chain.StartPosition - 1) is { } g1 &&
                        (Mics.FastSystem - g1.SentMics) > threshold)
                    {
                        if (startGene > lossPosition)
                        {
                            lossPosition = startGene;
                        }
                    }
                    else if (chain.Get(chain.StartPosition) is { } g2 &&
                        (Mics.FastSystem - g2.SentMics) > threshold)
                    {
                        if (startGene > lossPosition)
                        {
                            lossPosition = startGene;
                        }
                    }
                }*/

                if (this.Mode == NetTransmissionMode.Block)
                {
                    completeFlag = this.genes.GeneSerialListChain.Count == 0;
                }
                else if (this.Mode == NetTransmissionMode.StreamCompleted)
                {
                    completeFlag = this.genes.GeneSerialListChain.StartPosition >= this.GeneSerialMax;
                }
            }

            if (lossPosition >= 0 && this.genes?.GeneSerialListChain is { } c)
            {// Loss detected
                if (Mics.FastSystem - this.lastLossMics > this.Connection.SmoothedRtt)
                {
                    this.lastLossPosition = 0;
                    this.lastLossMics = Mics.FastSystem;
                }

                var startPosition = Math.Max(this.lastLossPosition, c.StartPosition);

                this.Connection.Logger.TryGet(LogLevel.Debug)?.Log($"Loss detected Start: {startPosition} Loss: {lossPosition} In-flight: {congestionControl.NumberInFlight}");
                for (var i = startPosition; i < lossPosition; i++)
                {
                    if (c.Get(i) is { } gene)
                    {
                        gene.CongestionControl.LossDetected(gene);
                    }
                }

                this.lastLossPosition = Math.Max(this.lastLossPosition, lossPosition);
            }

            if (completeFlag)
            {// Send transmission complete
                if (this.sentTcs is not null)
                {
                    this.sentTcs.SetResult(NetResult.Success);
                    this.sentTcs = null;
                }

                // Remove from sendTransmissions and dispose.
                this.Goshujin = null;
                this.DisposeInternal();
            }
        }
    }

    /*internal void SendStreamFrame(StreamFrameType streamFrameType)
    {
        Debug.Assert(this.Mode == NetTransmissionMode.Stream);

        Span<byte> frame = stackalloc byte[StreamFrame.Length];
        var span = frame;
        BitConverter.TryWriteBytes(span, (ushort)FrameType.Stream);
        span = span.Slice(sizeof(ushort));
        BitConverter.TryWriteBytes(span, this.TransmissionId);
        span = span.Slice(sizeof(uint));
        BitConverter.TryWriteBytes(span, (ushort)streamFrameType);
        span = span.Slice(sizeof(ushort));
        this.Connection.SendPriorityFrame(frame);

        this.Mode = NetTransmissionMode.StreamCompleted;
    }*/

    private void CreateFirstPacket_Block(int totalGene, uint dataKind, ulong dataId, ReadOnlySpan<byte> block, out BytePool.RentMemory rentMemory)
    {
        Debug.Assert(block.Length <= FirstGeneFrame.MaxGeneLength);

        // FirstGeneFrameCode
        Span<byte> frameHeader = stackalloc byte[FirstGeneFrame.Length];
        var span = frameHeader;

        BitConverter.TryWriteBytes(span, (ushort)FrameType.FirstGene); // Frame type
        span = span.Slice(sizeof(ushort));

        BitConverter.TryWriteBytes(span, (ushort)TransmissionMode.Block); // TransmissionMode
        span = span.Slice(sizeof(ushort));

        BitConverter.TryWriteBytes(span, this.TransmissionId); // TransmissionId
        span = span.Slice(sizeof(uint));

        BitConverter.TryWriteBytes(span, (ushort)DataControl.Valid); // DataControl
        span = span.Slice(sizeof(ushort));

        BitConverter.TryWriteBytes(span, this.Connection.SmoothedRtt); // Rtt hint
        span = span.Slice(sizeof(int));

        BitConverter.TryWriteBytes(span, totalGene); // TotalGene
        span = span.Slice(sizeof(int));

        BitConverter.TryWriteBytes(span, dataKind); // Data kind
        span = span.Slice(sizeof(uint));

        BitConverter.TryWriteBytes(span, dataId); // Data id
        span = span.Slice(sizeof(ulong));

        Debug.Assert(span.Length == 0);
        this.Connection.CreatePacket(frameHeader, block, out rentMemory);
    }

    private void CreateFirstPacket_Stream(DataControl dataControl, long maxStreamLength, ulong dataId, ReadOnlySpan<byte> block, out BytePool.RentMemory rentMemory)
    {
        Debug.Assert(block.Length <= FirstGeneFrame.MaxGeneLength);

        // FirstGeneFrameCode
        Span<byte> frameHeader = stackalloc byte[FirstGeneFrame.Length];
        var span = frameHeader;

        BitConverter.TryWriteBytes(span, (ushort)FrameType.FirstGene); // Frame type
        span = span.Slice(sizeof(ushort));

        BitConverter.TryWriteBytes(span, (ushort)TransmissionMode.Stream); // TransmissionMode
        span = span.Slice(sizeof(ushort));

        BitConverter.TryWriteBytes(span, this.TransmissionId); // TransmissionId
        span = span.Slice(sizeof(uint));

        BitConverter.TryWriteBytes(span, (ushort)dataControl); // DataControl
        span = span.Slice(sizeof(ushort));

        BitConverter.TryWriteBytes(span, this.Connection.SmoothedRtt); // Rtt hint
        span = span.Slice(sizeof(int));

        BitConverter.TryWriteBytes(span, maxStreamLength); // MaxStreamLength
        span = span.Slice(sizeof(long));

        BitConverter.TryWriteBytes(span, dataId); // Data id
        span = span.Slice(sizeof(ulong));

        Debug.Assert(span.Length == 0);
        this.Connection.CreatePacket(frameHeader, block, out rentMemory);
    }

    private void CreateFollowingPacket(DataControl dataControl, int dataPosition, ReadOnlySpan<byte> block, out BytePool.RentMemory rentMemory)
    {
        Debug.Assert(block.Length <= FollowingGeneFrame.MaxGeneLength);

        // FollowingGeneFrameCode
        Span<byte> frameHeader = stackalloc byte[FollowingGeneFrame.Length];
        var span = frameHeader;

        BitConverter.TryWriteBytes(span, (ushort)FrameType.FollowingGene); // Frame type
        span = span.Slice(sizeof(ushort));

        BitConverter.TryWriteBytes(span, this.TransmissionId); // TransmissionId
        span = span.Slice(sizeof(uint));

        BitConverter.TryWriteBytes(span, (ushort)dataControl); // DataControl
        span = span.Slice(sizeof(ushort));

        BitConverter.TryWriteBytes(span, dataPosition); // DataPosition
        span = span.Slice(sizeof(int));

        Debug.Assert(span.Length == 0);
        this.Connection.CreatePacket(frameHeader, block, out rentMemory);
    }
}

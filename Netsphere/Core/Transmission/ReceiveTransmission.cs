// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Diagnostics;
using Arc.Collections;
using Netsphere.Packet;

namespace Netsphere.Core;

[ValueLinkObject(Isolation = IsolationLevel.Serializable, Restricted = true)]
internal sealed partial class ReceiveTransmission : IDisposable
{
    // [Link(Name = "DisposedList", Type = ChainType.QueueList, AutoLink = false)]
    public ReceiveTransmission(Connection connection, uint transmissionId, TaskCompletionSource<NetResponse>? receivedTcs)
    {
        this.Connection = connection;
        this.TransmissionId = transmissionId;
        this.receivedTcs = receivedTcs;
    }

    #region FieldAndProperty

    public Connection Connection { get; }

    [Link(Primary = true, Type = ChainType.Unordered)]
    public uint TransmissionId { get; }

    public NetTransmissionMode Mode { get; private set; } // using (this.lockObject.EnterScope())

    public int MaxReceivePosition
    {
        get
        {
            if (this.genes is null)
            {// Disposed (Canceled)
                return 0;
            }
            else
            {
                return this.genes.DataPositionListChain.EndPosition;
            }
        }
    }

    public int SuccessiveReceivedPosition
        => this.successiveReceivedPosition;

    internal bool IsDisposed
        => this.Mode == NetTransmissionMode.Disposed;

#pragma warning disable SA1401 // Fields should be private
    // Received/Disposed list, lock (Connection.receiveTransmissions.SyncObject)
    internal UnorderedLinkedList<ReceiveTransmission>.Node? ReceivedOrDisposedNode;
    internal long ReceivedOrDisposedMics;
    internal Queue<int>? AckGene; // using (AckBuffer.lockObject.EnterScope())
#pragma warning restore SA1401 // Fields should be private

    private readonly Lock lockObject = new();
    private int totalGene;
    private TaskCompletionSource<NetResponse>? receivedTcs;
    private int successiveReceivedPosition;
    private ReceiveGene? gene0; // Gene 0
    private ReceiveGene? gene1; // Gene 1
    private ReceiveGene? gene2; // Gene 2
    private ReceiveGene.GoshujinClass? genes; // Multiple genes

    #endregion

    public void Dispose()
    {
        this.Connection.RemoveTransmission(this);
        this.DisposeTransmission();
    }

    internal void DisposeTransmission()
    {
        if (this.IsDisposed)
        {
            return;
        }

        using (this.lockObject.EnterScope())
        {
            this.DisposeInternal();
        }
    }

    internal void DisposeInternal()
    {
        if (this.IsDisposed)
        {
            return;
        }

        this.Mode = NetTransmissionMode.Disposed;
        this.gene0?.Dispose();
        this.gene1?.Dispose();
        this.gene2?.Dispose();
        if (this.genes is not null)
        {
            foreach (var x in this.genes)
            {
                x.Dispose();
            }

            this.genes = default; // this.genes.Clear();
        }

        if (this.receivedTcs is not null)
        {
            this.receivedTcs.SetResult(new(NetResult.Closed));
            this.receivedTcs = null;
        }
    }

    internal async Task<NetResponse> Wait(Task<NetResponse> task, int timeoutInMilliseconds, CancellationToken cancellationToken)
    {// I don't think this is a smart approach, but...
        var remainingMilliseconds = timeoutInMilliseconds;
        while (true)
        {
            if (!this.Connection.NetTerminal.IsActive)
            {// NetTerminal
                return new(NetResult.Closed);
            }

            if (!this.Connection.IsActive)
            {// Connection
                return new(NetResult.Closed);
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
                    return new(NetResult.Timeout);
                }
            }

            if (this.IsDisposed)
            {// Transmission
                return new(NetResult.Closed);
            }
        }
    }

    internal void Reset(TaskCompletionSource<NetResponse>? receivedTcs)
    {
        this.Mode = NetTransmissionMode.Initial;
        this.receivedTcs = receivedTcs;
    }

    internal void SetState_Receiving(int totalGene)
    {// Since it's called immediately after the object's creation, 'using (this.lockObject.EnterScope())' is probably not necessary.
        if (totalGene <= NetHelper.BurstGenes)
        {
            this.Mode = NetTransmissionMode.Burst;
        }
        else
        {
            this.Mode = NetTransmissionMode.Block;

            this.genes = new();
            this.genes.DataPositionListChain.Resize(totalGene);
            for (var i = 0; i < totalGene; i++)
            {
                var gene = new ReceiveGene(this);
                gene.Goshujin = this.genes;
                this.genes.DataPositionListChain.Add(gene);
            }
        }

        this.totalGene = totalGene;
    }

    internal void SetState_ReceivingStream(long maxLength)
    {// Since it's called immediately after the object's creation, 'using (this.lockObject.EnterScope())' is probably not necessary.
        this.Mode = NetTransmissionMode.Stream;
        this.totalGene = -1;

        var info = NetHelper.CalculateGene(maxLength);
        var numberOfGenes = Math.Min(this.Connection.Agreement.StreamBufferGenes, info.NumberOfGenes + 1); // +1 for last complete gene.

        this.genes = new();
        this.genes.DataPositionListChain.Resize(numberOfGenes);
        for (var i = 0; i < numberOfGenes; i++)
        {
            var gene = new ReceiveGene(this);
            gene.Goshujin = this.genes;
            this.genes.DataPositionListChain.Add(gene);
        }
    }

    internal void ProcessReceive_Gene(DataControl dataControl, int dataPosition, BytePool.RentMemory toBeShared)
    {// this.Mode == NetTransmissionMode.Burst or NetTransmissionMode.Block or NetTransmissionMode.Stream
        var completeFlag = false;
        uint dataKind = 0;
        ulong dataId = 0;
        BytePool.RentMemory rentMemory = default;
        using (this.lockObject.EnterScope())
        {
            if (this.Mode == NetTransmissionMode.Disposed)
            {// The case that the ACK has not arrived after the receive transmission was disposed.
                this.Connection.ConnectionTerminal.AckQueue.AckBlock(this.Connection, this, dataPosition);
                return;
            }
            else if (this.Mode == NetTransmissionMode.Initial)
            {// The packet must be discarded since the first packet has not been received and the receiving mode is unknown.
                return;
            }

            if (this.Mode == NetTransmissionMode.Burst)
            {// Single send/recv
                if (dataPosition == 0)
                {
                    this.gene0 ??= new(this);
                    this.gene0.SetRecv(dataControl, toBeShared);
                }
                else if (dataPosition == 1)
                {
                    this.gene1 ??= new(this);
                    this.gene1.SetRecv(dataControl, toBeShared);
                }
                else if (dataPosition == 2)
                {
                    this.gene2 ??= new(this);
                    this.gene2.SetRecv(dataControl, toBeShared);
                }

                if (this.totalGene == 0)
                {
                    completeFlag = true;
                }
                else if (this.totalGene == 1)
                {
                    completeFlag =
                        this.gene0?.IsReceived == true;
                }
                else if (this.totalGene == 2)
                {
                    completeFlag =
                        this.gene0?.IsReceived == true &&
                        this.gene1?.IsReceived == true;
                }
                else if (this.totalGene == 3)
                {
                    completeFlag =
                        this.gene0?.IsReceived == true &&
                        this.gene1?.IsReceived == true &&
                        this.gene2?.IsReceived == true;
                }
            }
            else if (this.genes is not null)
            {// Block, Stream
                // Console.WriteLine(dataPosition);
                var chain = this.genes.DataPositionListChain;
                if (this.Mode == NetTransmissionMode.Stream)
                {// Stream
                    if (dataPosition < chain.StartPosition ||
                    dataPosition >= chain.EndPosition)
                    {// Out of range (no ack)
                        return;
                    }
                }

                /*else if (this.Mode == NetTransmissionMode.Block)
                { //  Check anyway in chain.Get(dataPosition).
                    if (dataPosition < 0 ||
                    dataPosition >= this.totalGene)
                    {// Out of range
                        return;
                    }
                }*/

                if (chain.Get(dataPosition) is { } gene)
                {
                    gene.SetRecv(dataControl, toBeShared);

                    if (this.successiveReceivedPosition <= dataPosition)
                    {
                        if (this.successiveReceivedPosition == dataPosition)
                        {
                            this.successiveReceivedPosition++;
                        }

                        while (chain.Get(this.successiveReceivedPosition) is { } g && g.IsReceived)
                        {
                            this.successiveReceivedPosition++;
                        }
                    }

                    if (this.Mode == NetTransmissionMode.Block)
                    {
                        if (this.successiveReceivedPosition >= this.totalGene)
                        {
                            completeFlag = true;
                        }
                    }

                    /*else if (this.Mode == NetTransmissionMode.Stream)
                    {
                        if (dataControl == DataControl.Complete ||
                            dataControl == DataControl.Cancel)
                        {
                            this.totalGene = dataPosition;
                        }
                    }*/
                }
            }

            if (completeFlag)
            {// Complete (Burst, Stream)
                this.ProcessReceive_GeneComplete(out dataKind, out dataId, out rentMemory);
            }
        }

        this.Connection.UpdateLastEventMics();

        // Send Ack
        if (this.Mode == NetTransmissionMode.Burst)
        {// Fast Ack
            if (completeFlag)
            {
                if (this.Connection.Agreement.MaxTransmissions < 10)
                {// Instant
                    this.Connection.Logger.TryGet(LogLevel.Debug)?.Log($"{this.Connection.ConnectionIdText} Send Instant Ack {this.totalGene} to {this.Connection.DestinationEndpoint}");

                    Span<byte> ackFrame = stackalloc byte[2 + 4 + 4];
                    var span = ackFrame;
                    BitConverter.TryWriteBytes(span, (ushort)FrameType.Ack);
                    span = span.Slice(sizeof(ushort));
                    BitConverter.TryWriteBytes(span, (int)-1);
                    span = span.Slice(sizeof(int));
                    BitConverter.TryWriteBytes(span, this.TransmissionId);
                    span = span.Slice(sizeof(uint));

                    Debug.Assert(span.Length == 0);
                    this.Connection.SendPriorityFrame(ackFrame);
                }
                else
                {// Defer
                    // this.Connection.Logger.TryGet(LogLevel.Debug)?.Log($"{this.Connection.ConnectionIdText} Send Ack 0 - {this.totalGene}");

                    this.Connection.ConnectionTerminal.AckQueue.AckBurst(this.Connection, this);
                }
            }
        }
        else
        {// Ack (TransmissionId, GenePosition)
            this.Connection.ConnectionTerminal.AckQueue.AckBlock(this.Connection, this, dataPosition);
        }

        if (completeFlag)
        {// Receive complete
            TaskCompletionSource<NetResponse>? receivedTcs;

            using (this.lockObject.EnterScope())
            {
                receivedTcs = this.receivedTcs;
                this.receivedTcs = default;

                // this.Goshujin = null; // -> this.Connection.RemoveTransmission(this);
                this.DisposeInternal();
            }

            this.Connection.RemoveTransmission(this);

            if (rentMemory.IsRent)
            {
                if (this.Connection is ServerConnection serverConnection)
                {// InvokeServer: Connection, NetTransmission, Owner
                    var transmissionContext = new TransmissionContext(serverConnection, this.TransmissionId, dataKind, dataId, rentMemory.IncrementAndShare());
                    serverConnection.GetContext().InvokeSync(transmissionContext);
                }

                receivedTcs?.SetResult(new(NetResult.Success, dataId, 0, rentMemory.IncrementAndShare()));
                rentMemory.Return();
            }
        }
    }

    internal void StartStream(ulong dataId, long maxStreamLength)
    {
        TaskCompletionSource<NetResponse>? receivedTcs;
        using (this.lockObject.EnterScope())
        {
            receivedTcs = this.receivedTcs;
            this.receivedTcs = default;
        }

        if (receivedTcs is not null)
        {
            receivedTcs.SetResult(new(NetResult.Success, dataId, maxStreamLength, default));
        }
    }

    internal void ProcessReceive_GeneComplete(out uint dataKind, out ulong dataId, out BytePool.RentMemory toBeMoved)
    {// using (this.lockObject.EnterScope())
        if (this.genes is null)
        {// Single send/recv
            if (this.totalGene == 0)
            {
                dataKind = 0;
                dataId = 0;
                toBeMoved = default;
            }
            else
            {
                var span = this.gene0!.Packet.Span;
                dataKind = BitConverter.ToUInt32(span);
                span = span.Slice(sizeof(uint));
                dataId = BitConverter.ToUInt64(span);

                var firstPacket = this.gene0!.Packet.Slice(12);
                var length = firstPacket.Span.Length;
                if (this.totalGene == 1)
                {
                    toBeMoved = firstPacket.IncrementAndShare();
                }
                else if (this.totalGene == 2)
                {
                    length += this.gene1!.Packet.Span.Length;
                    toBeMoved = BytePool.Default.Rent(length).AsMemory(0, length);

                    span = toBeMoved.Span;
                    firstPacket.Span.CopyTo(span);
                    span = span.Slice(firstPacket.Span.Length);
                    this.gene1!.Packet.Span.CopyTo(span);
                }
                else if (this.totalGene == 3)
                {
                    length += this.gene1!.Packet.Span.Length;
                    length += this.gene2!.Packet.Span.Length;
                    toBeMoved = BytePool.Default.Rent(length).AsMemory(0, length);

                    span = toBeMoved.Span;
                    firstPacket.Span.CopyTo(span);
                    span = span.Slice(firstPacket.Span.Length);
                    this.gene1!.Packet.Span.CopyTo(span);
                    span = span.Slice(this.gene1!.Packet.Span.Length);
                    this.gene2!.Packet.Span.CopyTo(span);
                }
                else
                {
                    toBeMoved = default;
                }
            }

            return;
        }
        else
        {// Multiple send/recv
            // First
            var firstGene = this.genes.DataPositionListChain.Get(0);
            if (firstGene is null)
            {
                goto Abort;
            }

            var span = firstGene.Packet.Span;
            dataKind = BitConverter.ToUInt32(span);
            span = span.Slice(sizeof(uint));
            dataId = BitConverter.ToUInt64(span);

            var firstSpan = firstGene.Packet.Slice(12).Span;
            var length = firstSpan.Length;

            // Last
            var lastGene = this.genes.DataPositionListChain.Get(this.totalGene - 1);
            if (lastGene is null)
            {
                goto Abort;
            }

            length += (FollowingGeneFrame.MaxGeneLength * (this.totalGene - 2)) + lastGene.Packet.Span.Length;
            toBeMoved = BytePool.Default.Rent(length).AsMemory(0, length);
            span = toBeMoved.Span;

            firstSpan.CopyTo(span);
            span = span.Slice(firstSpan.Length);
            for (var i = 1; i < this.totalGene; i++)
            {
                var gene = this.genes.DataPositionListChain.Get(i);
                if (gene is null)
                {
                    toBeMoved.Return();
                    goto Abort;
                }

                var src = gene.Packet.Span;
                src.CopyTo(span);
                span = span.Slice(src.Length);
            }

            Debug.Assert(span.Length == 0);
            return;
        }

Abort:
        dataKind = 0;
        dataId = 0;
        toBeMoved = default;
    }

    internal void ProcessDispose()
    {
        if (!this.IsDisposed)
        {
            this.Dispose();
        }
    }

    internal async Task<(NetResult Result, int Written)> ProcessReceive(ReceiveStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int written = 0;
        if (this.Mode != NetTransmissionMode.Stream)
        {
            return (NetResult.Completed, written);
        }

        int remaining = buffer.Length;
        int lastMaxReceivedPosition;
        while (true)
        {
            using (this.lockObject.EnterScope())
            {
                if (!this.Connection.IsActive)
                {
                    return (NetResult.Closed, written);
                }
                else if (this.Mode != NetTransmissionMode.Stream)
                {
                    return (NetResult.Completed, written);
                }

                var chain = this.genes?.DataPositionListChain;
                if (chain is null)
                {
                    return (NetResult.Closed, written);
                }

                while (chain.Get(stream.CurrentGene) is { } gene)
                {
                    if (stream.ReceivedLength >= stream.MaxStreamLength)
                    {// Complete
                        this.DisposeInternal();
                        goto Complete;
                    }
                    else if (remaining == 0)
                    {
                        return (NetResult.Success, written);
                    }
                    else if (!gene.IsReceived)
                    {// Wait for data arrival.
                        break;
                    }

                    var originalLength = gene.Packet.Span.Length;
                    var length = originalLength;
                    if (gene.DataControl == DataControl.Complete)
                    {// Complete
                        gene.Dispose();
                        gene.Goshujin = default;
                        this.DisposeInternal();
                        goto Complete;
                    }
                    else if (gene.DataControl == DataControl.Cancel)
                    {// Cancel
                        gene.Dispose();
                        gene.Goshujin = default;
                        this.DisposeInternal();
                        goto Cancel;
                    }

                    if (stream.CurrentGene == 0 &&
                        stream.CurrentPosition < 12)
                    {// First gene
                        stream.CurrentPosition = 12;
                    }

                    length -= stream.CurrentPosition;
                    if (length > remaining)
                    {
                        length = remaining;
                    }

                    gene.Packet.Span.Slice(stream.CurrentPosition, length).CopyTo(buffer.Span);
                    buffer = buffer.Slice(length);
                    written += length;
                    remaining -= length;
                    stream.ReceivedLength += length;
                    stream.CurrentPosition += length;

                    if (stream.CurrentPosition >= originalLength)
                    {
                        Debug.Assert(stream.CurrentPosition == originalLength);
                        stream.CurrentGene++;
                        stream.CurrentPosition = 0;
                        gene.Dispose();

                        chain.Remove(gene);
                        chain.Add(gene);
                    }
                }

                lastMaxReceivedPosition = this.successiveReceivedPosition;
                if (stream.ReceivedLength >= stream.MaxStreamLength)
                {// Complete
                    this.DisposeInternal();
                    goto Complete;
                }
            }

            // Wait for data arrival.
            var delay = NetConstants.InitialReceiveStreamDelayMilliseconds;
            while (this.successiveReceivedPosition == lastMaxReceivedPosition)
            {
                if (!this.Connection.IsActive)
                {
                    return (NetResult.Closed, written);
                }
                else if (this.Mode != NetTransmissionMode.Stream)
                {
                    return (NetResult.Closed, written);
                }

                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    delay = Math.Min(delay << 1, NetConstants.MaxReceiveStreamDelayMilliseconds);
                }
                catch
                {
                    return (NetResult.Timeout, written);
                }
            }
        }

Complete:
        this.Connection.RemoveTransmission(this);
        return (NetResult.Completed, written);

Cancel:
        this.Connection.RemoveTransmission(this);
        return (NetResult.Canceled, written);
    }
}

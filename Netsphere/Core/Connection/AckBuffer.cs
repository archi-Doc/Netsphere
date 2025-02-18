// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Netsphere.Packet;
using Netsphere.Relay;

namespace Netsphere.Core;

internal partial class AckBuffer
{
    internal readonly struct ReceiveTransmissionAndAckGene
    {
        public ReceiveTransmissionAndAckGene(ReceiveTransmission transmission, Queue<int> ackGene)
        {
            this.ReceiveTransmission = transmission;
            this.AckGene = ackGene;
        }

        public readonly ReceiveTransmission ReceiveTransmission;
        public readonly Queue<int> AckGene;
    }

    public AckBuffer(ConnectionTerminal connectionTerminal)
    {
        this.connectionTerminal = connectionTerminal;
        this.logger = connectionTerminal.UnitLogger.GetLogger<AckBuffer>();
    }

    #region FieldAndProperty

    private readonly ConnectionTerminal connectionTerminal;
    private readonly ILogger logger;
    private readonly Queue<int> burst = new();

    private readonly Lock lockObject = new();
    private readonly Queue<Connection> connectionQueue = new();
    private readonly Queue<Queue<ReceiveTransmissionAndAckGene>> freeAckQueue = new();
    private readonly ConcurrentQueue<Queue<int>> freeAckGene = new();

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AckBurst(Connection connection, ReceiveTransmission receiveTransmission)
    {
        if (NetConstants.LogLowLevelNet)
        {
            this.logger.TryGet(LogLevel.Debug)?.Log($"AckBurst {this.connectionTerminal.NetTerminal.NetTerminalString} to {connection.DestinationEndpoint.ToString()} {receiveTransmission.TransmissionId}");
        }

        using (this.lockObject.EnterScope())
        {
            var ackQueue = connection.AckQueue;
            if (ackQueue is null)
            {
                this.freeAckQueue.TryDequeue(out ackQueue); // Reuse the queued queue.
                ackQueue ??= new();

                connection.AckMics = Mics.FastSystem + NetConstants.AckDelayMics;
                connection.AckQueue = ackQueue;
                this.connectionQueue.Enqueue(connection);
            }

            var ackGene = receiveTransmission.AckGene;
            if (ackGene is null)
            {
                receiveTransmission.AckGene = this.burst;
                ackQueue.Enqueue(new(receiveTransmission, this.burst));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AckBlock(Connection connection, ReceiveTransmission receiveTransmission, int geneSerial)
    {
        if (NetConstants.LogLowLevelNet)
        {
            this.logger.TryGet(LogLevel.Debug)?.Log($"{connection.ConnectionIdText} AckBlock to {connection.DestinationEndpoint.ToString()} {receiveTransmission.TransmissionId}-{geneSerial}");
        }

        using (this.lockObject.EnterScope())
        {
            var ackQueue = connection.AckQueue;
            if (ackQueue is null)
            {
                this.freeAckQueue.TryDequeue(out ackQueue); // Reuse the queued queue.
                ackQueue ??= new();

                connection.AckMics = Mics.FastSystem + NetConstants.AckDelayMics;
                connection.AckQueue = ackQueue;
                this.connectionQueue.Enqueue(connection);
            }

            var ackGene = receiveTransmission.AckGene;
            if (ackGene is null)
            {
                this.freeAckGene.TryDequeue(out ackGene); // Reuse the queued queue.
                ackGene ??= new();

                receiveTransmission.AckGene = ackGene;
                ackQueue.Enqueue(new(receiveTransmission, ackGene));
            }

            ackGene.Enqueue(geneSerial);
        }
    }

    public void ProcessSend(NetSender netSender)
    {
        Connection? connection = default;
        Queue<ReceiveTransmissionAndAckGene>? ackQueue = default;

        while (true)
        {
            using (this.lockObject.EnterScope())
            {
                if (ackQueue is not null)
                {
                    this.freeAckQueue.Enqueue(ackQueue);
                    ackQueue = default;
                }

                if (!netSender.CanSend)
                {
                    return;
                }

                this.connectionQueue.TryPeek(out connection);
                if (connection is not null && Mics.FastSystem > connection.AckMics)
                {
                    this.connectionQueue.Dequeue();
                    ackQueue = connection.AckQueue!;
                    foreach (var x in ackQueue)
                    {
                        x.ReceiveTransmission.AckGene = default;
                    }

                    connection.AckMics = 0;
                    connection.AckQueue = default;
                }
            }

            // To shorten the acquisition time of the exclusive lock, temporarily release the lock.

            if (connection is null || ackQueue is null)
            {
                break;
            }

            this.ProcessAck(netSender, connection, ackQueue);
        }
    }

    private void ProcessAck(NetSender netSender, Connection connection, Queue<ReceiveTransmissionAndAckGene> ackQueue)
    {
        const int maxLength = PacketHeader.MaxFrameLength - 2;

        BytePool.RentArray? rentArray = default;
        Span<byte> span = default;

        while (ackQueue.TryDequeue(out var item))
        {
NewPacket:
            if (rentArray is not null && span.Length < AckFrame.Margin)
            {// Send the packet when the remaining length falls below the margin.
                Send(maxLength - span.Length);
            }

            if (rentArray is null)
            {// Prepare
                // ProtectedPacketCode
                rentArray = PacketPool.Rent();
                span = rentArray.AsSpan(PacketHeader.Length + ProtectedPacket.Length + 2, maxLength); // PacketHeader, FrameType, NumberOfBurst, NumberOfBlock
            }

            var ackGene = item.AckGene;
            if (ackGene == this.burst)
            {// Burst
                BitConverter.TryWriteBytes(span, (int)-1); // 4 bytes
                span = span.Slice(sizeof(int));
                BitConverter.TryWriteBytes(span, item.ReceiveTransmission.TransmissionId); // 4 bytes
                span = span.Slice(sizeof(uint));
            }
            else
            {// Block/Stream
                int maxReceivePosition = item.ReceiveTransmission.MaxReceivePosition;
                int successiveReceivedPosition = item.ReceiveTransmission.SuccessiveReceivedPosition;

                BitConverter.TryWriteBytes(span, maxReceivePosition); // 4 bytes
                span = span.Slice(sizeof(int));
                BitConverter.TryWriteBytes(span, item.ReceiveTransmission.TransmissionId); // 4 bytes
                span = span.Slice(sizeof(uint));
                BitConverter.TryWriteBytes(span, successiveReceivedPosition); // 4 bytes
                span = span.Slice(sizeof(int));

                ushort numberOfPairs = 0;
                var numberOfPairsSpan = span;
                span = span.Slice(sizeof(ushort)); // 2 bytes

                int startGene = -1;
                int endGene = -1;
                while (ackGene.TryDequeue(out var geneSerial))
                {
                    if (startGene == -1)
                    {// Initial gene
                        startGene = geneSerial;
                        endGene = geneSerial + 1;
                    }
                    else if (endGene == geneSerial)
                    {// Serial genes
                        endGene = geneSerial + 1;
                    }
                    else
                    {// Not serial gene
                        // Console.WriteLine($"{startGene} - {endGene}");
                        BitConverter.TryWriteBytes(span, startGene);
                        span = span.Slice(sizeof(int));
                        BitConverter.TryWriteBytes(span, endGene);
                        span = span.Slice(sizeof(int));
                        numberOfPairs++;

                        if (rentArray is not null && span.Length < AckFrame.Margin)
                        {// Send the packet when the remaining length falls below the margin.
                            BitConverter.TryWriteBytes(numberOfPairsSpan, numberOfPairs);
                            goto NewPacket;
                        }

                        startGene = geneSerial;
                        endGene = geneSerial + 1;
                    }
                }

                if (startGene != -1)
                {
                    // Console.WriteLine($"{startGene} - {endGene}");
                    BitConverter.TryWriteBytes(span, startGene);
                    span = span.Slice(sizeof(int));
                    BitConverter.TryWriteBytes(span, endGene);
                    span = span.Slice(sizeof(int));
                    numberOfPairs++;
                    BitConverter.TryWriteBytes(numberOfPairsSpan, numberOfPairs);
                }

                this.freeAckGene.Enqueue(ackGene);
            }
        }

        if (rentArray is not null && span.Length > 0)
        {// Send the packet if not empty.
            Send(maxLength - span.Length);
        }

        rentArray?.Return(); // Return the rent buffer.
        // ackQueue will be returned later for reuse.

        void Send(int spanLength)
        {
            if (NetConstants.LogLowLevelNet)
            {
                this.logger.TryGet(LogLevel.Debug)?.Log($"{connection.ConnectionIdText} to {connection.DestinationEndpoint.ToString()}, SendAck");
            }

            connection.CreateAckPacket(rentArray, spanLength, out var packetLength);
            if (connection.MinimumNumberOfRelays == 0)
            {// No relay
                netSender.Send_NotThreadSafe(connection.DestinationEndpoint.EndPoint, rentArray.AsMemory(0, packetLength));
            }
            else
            {// Relay
                if (connection.CorrespondingRelayKey.TryEncrypt(connection.MinimumNumberOfRelays, connection.DestinationNode.Address, rentArray.AsMemory(0, packetLength).Span, out var encrypted, out var relayEndpoint))
                {
                    netSender.Send_NotThreadSafe(relayEndpoint.EndPoint, encrypted);
                }

                rentArray.Return();
            }

            // rentMemory = rentMemory.Return(); // Moved
            rentArray = default;
        }
    }
}

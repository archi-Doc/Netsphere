// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using Arc.Collections;

namespace Netsphere.Core;

internal class NoCongestionControl : ICongestionControl
{
    public NoCongestionControl()
    {
    }

    #region FieldAndProperty

    public int NumberInFlight
        => this.genesInFlight.Count;

    public bool IsCongested
        => false;

    private readonly Lock lockObject = new();
    private readonly OrderedMultiMap<long, SendGene> genesInFlight = new(); // Retransmission mics, gene

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ICongestionControl.AddInFlight(SendGene sendGene, int additional)
    {
        using (this.lockObject.EnterScope())
        {
            var rto = Mics.FastSystem + sendGene.SendTransmission.Connection.TaichiTimeout + additional;
            if (NetConstants.LogLowLevelNet)
            {
                // sendGene.SendTransmission.Connection.Logger.TryGet(LogLevel.Debug)?.Log($"RTT {sendGene.SendTransmission.Connection.SmoothedRtt}, var {sendGene.SendTransmission.Connection.RttVar}, Taichi timeout {sendGene.SendTransmission.Connection.TaichiTimeout}, RTO {sendGene.SendTransmission.Connection.RetransmissionTimeout}");
            }

            if (sendGene.Node is OrderedMultiMap<long, SendGene>.Node node)
            {
                this.genesInFlight.SetNodeKey(node, rto);
            }
            else
            {
                (sendGene.Node, _) = this.genesInFlight.Add(rto, sendGene);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ICongestionControl.RemoveInFlight(SendGene sendGene, bool ack)
    {
        using (this.lockObject.EnterScope())
        {
            sendGene.SendTransmission.Connection.ResetTaichi();
            if (sendGene.Node is OrderedMultiMap<long, SendGene>.Node node)
            {
                this.genesInFlight.RemoveNode(node);
                sendGene.Node = default;
            }
        }
    }

    void ICongestionControl.LossDetected(Netsphere.Core.SendGene sendGene)
    {
    }

    bool ICongestionControl.Process(NetSender netSender, long elapsedMics, double elapsedMilliseconds)
    {// lock (ConnectionTerminal.CongestionControlList)
        // Resend
        SendGene? gene;
        using (this.lockObject.EnterScope())
        {// To prevent deadlocks, the lock order for CongestionControl must be the lowest, and it must not acquire locks by calling functions of other classes.
            int addition = 0; // Increment rto (retransmission timeout) to create a small difference.
            while (netSender.CanSend)
            {// Retransmission
                var firstNode = this.genesInFlight.First;
                if (firstNode is null ||
                    firstNode.Key > Mics.FastSystem)
                {
                    break;
                }

                gene = firstNode.Value;
                gene.SendTransmission.Connection.DoubleTaichi();
                Console.WriteLine($"Resend(timeout): {gene.GeneSerial}/{gene.SendTransmission.GeneSerialMax} {gene.SendTransmission.Connection.DestinationEndpoint}");
                if (!gene.Resend_NotThreadSafe(netSender, addition++))
                {// Cannot send
                    this.genesInFlight.RemoveNode(firstNode);
                    gene.Node = default;
                }
            }
        }

        return true; // Do not dispose NoCongestionControl as it is shared across the connections.
    }

    void ICongestionControl.AddRtt(int rttMics)
    {
    }
}

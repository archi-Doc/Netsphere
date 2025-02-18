// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

#pragma warning disable SA1401 // Fields should be private

namespace Netsphere.Core;

[ValueLinkObject(Restricted = true)]
internal partial class SendGene
{// using (transmission.lockObject.EnterScope())
    internal enum State
    {
        Initial, // Waiting to be sent.
        Sent, // Sent once.
        Resent, // Sent more than once.
        LossDetected, // Requires resending due to presumed packet loss.
    }

    [Link(Primary = true, Type = ChainType.SlidingList, Name = "GeneSerialList")]
    public SendGene(SendTransmission sendTransmission)
    {
        this.SendTransmission = sendTransmission;
        this.CongestionControl = sendTransmission.Connection.GetCongestionControl(); // Keep a CongestionContro instance as a member variable, since it may be subject to change.
    }

    #region FieldAndProperty

    public SendTransmission SendTransmission { get; }

    public ICongestionControl CongestionControl { get; }

    public BytePool.RentMemory Packet { get; private set; }

    public long SentMics { get; private set; }

    public State CurrentState { get; private set; }

    public int GeneSerial
        => this.GeneSerialListLink.Position;

    internal object? Node; // using (this.CongestionControl.lockObject.EnterScope())

    public bool CanSend
        => this.SendTransmission.Mode != NetTransmissionMode.Disposed &&
        this.SendTransmission.Connection.CurrentState == Connection.State.Open;

    public bool CanResend
        => (Mics.FastSystem - this.SentMics) > this.SendTransmission.Connection.MinimumRtt;

    /*public bool CanResend
    {
        get
        {
            if (this.CurrentState == State.Sent ||
            this.CurrentState == State.Resent ||
            this.CurrentState == State.LossDetected)
            {// Sent
                var threshold = this.SendTransmission.Connection.MinimumRtt;
                if (Mics.FastSystem - this.SentMics < threshold)
                {// Suppress the resending.
                    return false;
                }
            }

            // Initial or LossDetected
            return true;
        }
    }*/

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetSend(BytePool.RentMemory toBeMoved)
    {
        this.Packet = toBeMoved;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetLossDetected()
    {
        this.CurrentState = State.LossDetected;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Resend_NotThreadSafe(NetSender netSender, int additional)
    {
        if (!this.CanResend)
        {// Suppress resending.
            return true;
        }

        this.CurrentState = State.Resent;
        return this.Send_NotThreadSafe(netSender, additional);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Send_NotThreadSafe(NetSender netSender, int additional)
    {
        if (!this.CanSend || !this.Packet.TryIncrement())
        {// MemoryOwner has been returned to the pool (Disposed).
            return false;
        }

        var connection = this.SendTransmission.Connection;
        var currentMics = Mics.FastSystem;

        if (NetConstants.LogLowLevelNet)
        {
            connection.Logger.TryGet(LogLevel.Debug)?.Log($"{connection.ConnectionIdText} {connection.ConnectionTerminal.NetTerminal.NetTerminalString} to {connection.DestinationEndpoint.ToString()}, Send gene {this.GeneSerialListLink.Position} {this.CurrentState.ToString()} {this.Packet.Memory.Length}");
        }

        if (connection.MinimumNumberOfRelays == 0)
        {// No relay
            netSender.Send_NotThreadSafe(connection.DestinationEndpoint.EndPoint, this.Packet);
        }
        else
        {// Relay
            if (!connection.CorrespondingRelayKey.TryEncrypt(connection.MinimumNumberOfRelays, connection.DestinationNode.Address, this.Packet.Span, out var encrypted, out var relayEndpoint))
            {
                this.Packet.Return();
                return false;
            }

            this.Packet.Return();
            netSender.Send_NotThreadSafe(relayEndpoint.EndPoint, encrypted);
        }

        this.SentMics = currentMics;
        if (this.CurrentState == State.Initial)
        {// First send
            this.CurrentState = State.Sent;
            connection.IncrementSendCount();
        }
        else
        {// Resend (Sent, Resent, LossDetected)
            this.CurrentState = State.Resent;
            connection.IncrementResendCount();
        }

        this.CongestionControl.AddInFlight(this, additional);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose(bool ack)
    {// using (SendTransmissions.lockObject.EnterScope())
        this.CongestionControl.RemoveInFlight(this, ack);
        this.Goshujin = null;
        this.Packet.Return();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DisposeMemory()
    {// using (SendTransmissions.lockObject.EnterScope())
        this.CongestionControl.RemoveInFlight(this, false);
        this.Packet.Return();
    }

    public override string ToString()
        => $"Send gene {this.GeneSerial}";
}

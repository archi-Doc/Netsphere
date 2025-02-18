// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using System.Runtime.InteropServices;
using Netsphere.Core;
using Netsphere.Crypto;
using Netsphere.Packet;
using Netsphere.Relay;
using Netsphere.Responder;
using Netsphere.Stats;

namespace Netsphere;

public class NetTerminal : UnitBase, IUnitPreparable, IUnitExecutable
{
    public enum State
    {
        Initial,
        Active,
        Shutdown,
    }

    public NetTerminal(UnitContext unitContext, UnitLogger unitLogger, NetBase netBase, NetStats netStats, IRelayControl relayControl)
        : base(unitContext)
    {
        this.UnitLogger = unitLogger;
        this.NetBase = netBase;
        this.NetStats = netStats;

        this.NetSender = new(this, this.NetBase, unitLogger.GetLogger<NetSender>());
        this.PacketTerminal = new(this.NetBase, this, unitLogger.GetLogger<PacketTerminal>());
        this.IncomingCircuit = new(this, true);
        this.OutgoingCircuit = new(this, false);
        this.RelayControl = relayControl;
        this.RelayAgent = new(relayControl, this);
        this.ConnectionTerminal = new(unitContext.ServiceProvider, this);

        this.PacketTransmissionTimeout = NetConstants.DefaultPacketTransmissionTimeout;
    }

    #region FieldAndProperty

    public State CurrentState { get; private set; }

    public bool IsActive => !ThreadCore.Root.IsTerminated && this.CurrentState == State.Active;

    public NetBase NetBase { get; }

    public string NetTerminalString => this.IsAlternative ? "Alt" : "Main";

    public EncryptionPublicKey NodePublicKey { get; private set; }

    public NetStats NetStats { get; }

    public ResponderControl Responders { get; private set; } = default!;

    public ServiceControl Services { get; private set; } = default!;

    public PacketTerminal PacketTerminal { get; }

    public RelayCircuit IncomingCircuit { get; private set; }

    public RelayCircuit OutgoingCircuit { get; private set; }

    public RelayAgent RelayAgent { get; private set; }

    public bool IsAlternative { get; private set; }

    public int Port { get; private set; }

    public int MinimumNumberOfRelays { get; private set; }

    public TimeSpan PacketTransmissionTimeout { get; private set; }

    public IRelayControl RelayControl { get; private set; }

    internal SeedKey NodeSeedKey { get; private set; } = default!;

    internal NetSender NetSender { get; }

    internal UnitLogger UnitLogger { get; private set; }

    internal ConnectionTerminal ConnectionTerminal { get; private set; }

    #endregion

    public bool TryCreateEndpoint(ref NetAddress address, EndpointResolution endpointResolution, out NetEndpoint endPoint)
        => this.NetStats.TryCreateEndpoint(ref address, endpointResolution, out endPoint);

    public void SetDeliveryFailureRatioForTest(double ratio)
    {
        this.NetSender.SetDeliveryFailureRatio(ratio);
    }

    public void SetReceiveTransmissionGapForTest(uint gap)
    {
        this.ConnectionTerminal.SetReceiveTransmissionGapForTest(gap);
    }

    public async Task<NetNode?> UnsafeGetNetNode(NetAddress address)
    {
        if (!this.NetBase.AllowUnsafeConnection)
        {
            return null;
        }

        var t = await this.PacketTerminal.SendAndReceive<GetInformationPacket, GetInformationPacketResponse>(address, new()).ConfigureAwait(false);
        if (t.Value is null)
        {
            return null;
        }

        if (t.Value.OwnNetNode is not null)
        {
            return t.Value.OwnNetNode;
        }

        return new(address, t.Value.PublicKey);
    }

    public Task<ClientConnection?> Connect(NetNode destination, Connection.ConnectMode mode = Connection.ConnectMode.ReuseIfAvailable, int minimumNumberOfRelays = 0, EndpointResolution endpointResolution = EndpointResolution.PreferIpv6)
        => this.ConnectionTerminal.Connect(destination, mode, minimumNumberOfRelays, endpointResolution);

    public Task<ClientConnection?> ConnectForRelay(NetNode destination, bool incomingRelay, int targetNumberOfRelays, EndpointResolution endpointResolution = EndpointResolution.PreferIpv6)
        => this.ConnectionTerminal.ConnectForRelay(destination, incomingRelay, targetNumberOfRelays, endpointResolution);

    public async Task<ClientConnection?> UnsafeConnect(NetAddress destination, Connection.ConnectMode mode = Connection.ConnectMode.ReuseIfAvailable)
    {
        var netNode = await this.UnsafeGetNetNode(destination).ConfigureAwait(false);
        if (netNode is null)
        {
            return default;
        }

        return await this.Connect(netNode, mode).ConfigureAwait(false);
    }

    public void SetNodeSeedKey(SeedKey nodePrivateKey)
    {
        this.NodeSeedKey = nodePrivateKey;
        this.NodePublicKey = nodePrivateKey.GetEncryptionPublicKey();
    }

    void IUnitPreparable.Prepare(UnitMessage.Prepare message)
    {
        if (this.Port == 0)
        {
            this.Port = this.NetBase.NetOptions.Port;
        }

        if (!this.IsAlternative)
        {
            this.NodeSeedKey = this.NetBase.NodeSeedKey;
        }
        else
        {
            this.NodeSeedKey = Alternative.SeedKey;
            this.Port = Alternative.Port;
        }

        this.NodePublicKey = this.NodeSeedKey.GetEncryptionPublicKey();
    }

    async Task IUnitExecutable.StartAsync(UnitMessage.StartAsync message, CancellationToken cancellationToken)
    {
        this.CurrentState = State.Active;

        await this.NetSender.StartAsync(message.ParentCore);
    }

    void IUnitExecutable.Stop(UnitMessage.Stop message)
    {
    }

    async Task IUnitExecutable.TerminateAsync(UnitMessage.TerminateAsync message, CancellationToken cancellationToken)
    {
        // Close all connections
        this.CurrentState = State.Shutdown;

        await this.ConnectionTerminal.Terminate(cancellationToken).ConfigureAwait(false);

        this.NetSender.Stop();
    }

    internal void Initialize(ResponderControl responders, ServiceControl services, bool isAlternative)
    {
        this.Responders = responders;
        this.Services = services;
        this.IsAlternative = isAlternative;

        this.RelayControl.RegisterResponder(this.Responders);
    }

    internal async Task<NetResponse> Wait(Task<NetResponse> task, TimeSpan timeout, CancellationToken cancellationToken)
    {// I don't think this is a smart approach, but...
        var remaining = timeout;
        while (true)
        {
            if (!this.IsActive)
            {// NetTerminal
                return new(NetResult.Closed);
            }

            try
            {
                var result = await task.WaitAsync(NetConstants.WaitIntervalTimeSpan, cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (TimeoutException)
            {
                if (remaining < TimeSpan.Zero)
                {// Wait indefinitely.
                }
                else if (remaining > NetConstants.WaitIntervalTimeSpan)
                {// Reduce the time and continue waiting.
                    remaining -= NetConstants.WaitIntervalTimeSpan;
                }
                else
                {// Timeout
                    return new(NetResult.Timeout);
                }
            }
        }
    }

    internal void ProcessSend(NetSender netSender)
    {
        // 1st: PacketTerminal (Packets: Connect, Ack, Loss, ...)
        this.PacketTerminal.ProcessSend(netSender);
        if (!netSender.CanSend)
        {
            return;
        }

        // 2nd: AckBuffer (Ack)
        this.ConnectionTerminal.AckQueue.ProcessSend(netSender);
        if (!netSender.CanSend)
        {
            return;
        }

        // 3rd: ConnectionTerminal (SendTransmission/SendGene)
        this.ConnectionTerminal.ProcessSend(netSender);

        // 4th : Relay
        this.RelayAgent.ProcessSend(netSender);
    }

    internal unsafe void ProcessReceive(IPEndPoint endPoint, BytePool.RentArray toBeShared, int packetSize)
    {// Checked: packetSize
        var currentSystemMics = Mics.FastSystem;
        var rentMemory = toBeShared.AsMemory(0, packetSize);
        var span = rentMemory.Span;

        // PacketHeaderCode
        var netEndpoint = new NetEndpoint(MemoryMarshal.Read<RelayId>(span), endPoint); // SourceRelayId
        var relayNumber = 0;
        var incomingRelay = false;
        var destinationRelayId = MemoryMarshal.Read<RelayId>(span.Slice(sizeof(RelayId))); // DestinationRelayId
        if (destinationRelayId != 0)
        {// Relay
            if (!this.RelayAgent.ProcessRelay(netEndpoint, destinationRelayId, rentMemory, out var decrypted))
            {
                return;
            }

            rentMemory = decrypted;
            span = decrypted.Span;
        }
        else if (netEndpoint.RelayId != 0)
        {// Receive data from relays.
            NetAddress originalAddress;
            if (this.OutgoingCircuit.RelayKey.NumberOfRelays > 0 &&
                this.OutgoingCircuit.RelayKey.TryDecrypt(netEndpoint, ref rentMemory, out originalAddress, out relayNumber))
            {// Outgoing relay
                span = rentMemory.Span;
                var ep2 = this.RelayAgent.GetEndPoint_NotThreadSafe(originalAddress, RelayAgent.EndpointOperation.None);
                netEndpoint = new(originalAddress.RelayId, ep2.EndPoint);
            }
            else if (this.IncomingCircuit.RelayKey.NumberOfRelays > 0 &&
                this.IncomingCircuit.RelayKey.TryDecrypt(netEndpoint, ref rentMemory, out originalAddress, out relayNumber))
            {// Incoming relay
                span = rentMemory.Span;
                var ep2 = this.RelayAgent.GetEndPoint_NotThreadSafe(originalAddress, RelayAgent.EndpointOperation.None);
                netEndpoint = new(originalAddress.RelayId, ep2.EndPoint);
                incomingRelay = true;
            }
        }

        // relayNumber: 0 No relay, >0 Outgoing, <0 Incoming

        // Packet type
        span = span.Slice(RelayHeader.RelayIdLength + sizeof(uint));
        var packetType = BitConverter.ToUInt16(span);

        if (packetType < 256)
        {// Packet
            this.PacketTerminal.ProcessReceive(netEndpoint, relayNumber, incomingRelay, destinationRelayId, packetType, rentMemory, currentSystemMics);
        }
        else if (packetType < 511)
        {// Gene
            var outgoingRelay = relayNumber > 0 && !incomingRelay;
            this.ConnectionTerminal.ProcessReceive(netEndpoint, outgoingRelay, packetType, rentMemory, currentSystemMics);
        }
    }

    internal async Task IntervalTask(CancellationToken cancellationToken)
    {
        this.ConnectionTerminal.Clean();
        this.RelayAgent.Clean();
        this.IncomingCircuit.Clean();
        this.OutgoingCircuit.Clean();

        await this.IncomingCircuit.Maintain(cancellationToken);
        await this.OutgoingCircuit.Maintain(cancellationToken);
    }
}

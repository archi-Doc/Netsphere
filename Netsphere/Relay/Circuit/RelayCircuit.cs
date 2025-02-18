// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;

namespace Netsphere.Relay;

/// <summary>
/// <see cref="RelayCircuit"/> is a primitive class for managing relay circuits.
/// </summary>
public class RelayCircuit
{
    private const int MaxOutgoingSerialRelays = 5;
    private const int MaxIncomingSerialRelays = 1;
    private static readonly long PingIntervalMics = Mics.FromSeconds(60);

    public RelayCircuit(NetTerminal netTerminal, bool incoming)
    {
        this.netTerminal = netTerminal;
        this.IsIncoming = incoming;

        this.logger = this.netTerminal.UnitLogger.GetLogger<RelayCircuit>();
    }

    #region FieldAndProperty

    public bool AllowOpenSesami { get; set; }

    public bool AllowUnknownIncoming { get; set; }

    public int NumberOfRelays
        => this.relayNodes.Count;

    public bool IsIncoming { get; }

    public string KindText => this.IsIncoming switch
    {
        true => "Incoming",
        false => "Outgoing",
    };

    internal RelayKey RelayKey
        => this.relayKey;

    private readonly NetTerminal netTerminal;
    private readonly ILogger logger;
    private readonly RelayNode.GoshujinClass relayNodes = new();

    private RelayKey relayKey = new();
    private long lastPingMics;

    #endregion

    public AssignRelayBlock NewAssignRelayBlock()
        => new(this.AllowOpenSesami, this.AllowUnknownIncoming);

    public bool TryGetOutermostAddress([MaybeNullWhen(false)] out NetAddress netAddress)
    {
        using (this.relayNodes.LockObject.EnterScope())
        {
            if (this.relayNodes.LinkedListChain.Last is { } last)
            {
                netAddress = new(last.OuterRelayId, last.Address);
                return true;
            }
            else
            {
                netAddress = default;
                return false;
            }
        }
    }

    public async Task<RelayResult> AddRelay(AssignRelayBlock assignRelayBlock, AssignRelayResponse assignRelayResponse, ClientConnection clientConnection)
    {
        if (clientConnection.DestinationEndpoint.RelayId != 0)
        {
            return RelayResult.InvalidEndpoint;
        }

        if (assignRelayResponse.RelayNetAddress.IsValid &&
            !assignRelayResponse.RelayNetAddress.Equals(clientConnection.DestinationEndpoint.EndPoint))
        {
            return RelayResult.InvalidEndpoint;
        }

        var relayId = assignRelayResponse.InnerRelayId;
        ClientConnection? lastConnection = default;
        using (this.relayNodes.LockObject.EnterScope())
        {
            var result = this.CanAddRelayInternal(relayId, clientConnection.DestinationEndpoint);
            if (result != RelayResult.Success)
            {
                return result;
            }

            lastConnection = this.relayNodes.LinkedListChain.Last?.ClientConnection;

            this.relayNodes.Add(new(assignRelayBlock, assignRelayResponse, clientConnection));
            this.ResetRelayKeyInternal();
        }

        clientConnection.MinimumNumberOfRelays = -clientConnection.MinimumNumberOfRelays - 1; // Configure it as a relay connection and specify the relay circuit number to send data through the relay (use NetAddress.Relay).
        clientConnection.Agreement.MinimumConnectionRetentionMics = assignRelayResponse.RetensionMics;

        if (lastConnection is null)
        {
            return RelayResult.Success;
        }

        var outerEndpoint = new NetEndpoint(assignRelayResponse.InnerRelayId, clientConnection.DestinationEndpoint.EndPoint);
        var block = new SetupRelayBlock(outerEndpoint, assignRelayBlock.InnerKeyAndNonce);
        var r = await lastConnection.SendAndReceive<SetupRelayBlock, SetupRelayResponse>(block, SetupRelayBlock.DataId);
        if (r.Result != NetResult.Success ||
            r.Value is null)
        {
            return RelayResult.ConnectionFailure;
        }

        return r.Value.Result;
    }

    public void Clean()
    {
        using (this.relayNodes.LockObject.EnterScope())
        {
            TemporaryList<RelayNode> deleteList = default;
            foreach (var x in this.relayNodes)
            {
                if (!x.ClientConnection.IsOpen)
                {// Connection is closed
                    deleteList.Add(x);
                }
            }

            foreach (var x in deleteList)
            {
                if (NetConstants.LogRelay)
                {
                    this.logger.TryGet(LogLevel.Information)?.Log($"Removed (Clean) {x.ToString()}");
                }

                x.Remove();
            }
        }
    }

    public async Task Maintain(CancellationToken cancellationToken)
    {
        if (this.NumberOfRelays > 0 && this.IsIncoming)
        {// Ping
            if (this.lastPingMics + PingIntervalMics < Mics.FastSystem)
            {
                this.lastPingMics = Mics.FastSystem;
                var r = await this.netTerminal.PacketTerminal.SendAndReceive<PingRelayPacket, PingRelayResponse>(NetAddress.Relay, new(), this.NumberOfRelays, cancellationToken, EndpointResolution.PreferIpv6, this.IsIncoming);
                // Console.WriteLine(r.Result);
                if (r.Result != NetResult.Success)
                {
                }
            }
        }
    }

    public async Task Close()
    {
        while (true)
        {
            using (this.relayNodes.LockObject.EnterScope())
            {// Close sequentially starting from the outermost node.
                RelayNode? node = this.relayNodes.LinkedListChain.Last;
                if (node is null)
                {
                    break;
                }

                node.Remove();
                if (this.relayNodes.Count == 0)
                {
                    break;
                }
            }

            // Send packets with a time delay.
            await Task.Delay(10);
        }

        using (this.relayNodes.LockObject.EnterScope())
        {
            this.relayNodes.Clear();
            this.ResetRelayKeyInternal();
        }

        this.netTerminal.ConnectionTerminal.CloseRelayedConnections();
    }

    public RelayResult CanAddRelay(RelayId relayId, NetEndpoint endpoint)
    {
        using (this.relayNodes.LockObject.EnterScope())
        {
            return this.CanAddRelayInternal(relayId, endpoint);
        }
    }

    public string UnsafeToString()
    {
        var sb = new StringBuilder();
        using (this.relayNodes.LockObject.EnterScope())
        {
            var i = 0;
            foreach (var x in this.relayNodes)
            {
                sb.Append($"{i++}: {x.ToString()}");
            }
        }

        return sb.ToString();
    }

    public async Task<string> UnsafeDetailedToString()
    {
        NetEndpoint[] endpointArray;
        using (this.relayNodes.LockObject.EnterScope())
        {
            endpointArray = this.relayNodes.Select(x => x.Endpoint).ToArray();
        }

        var dictionary = new ConcurrentDictionary<int, PingRelayResponse>();
        var cts = new CancellationTokenSource();
        cts.CancelAfter(NetConstants.DefaultPacketTransmissionTimeout);
        var task = Parallel.ForAsync(0, endpointArray.Length, cts.Token, async (i, cancellationToken) =>
        {
            var relayNumber = -(1 + i); // this.incoming ? 0 : -(1 + i);
            var rr = await this.netTerminal.PacketTerminal.SendAndReceive<PingRelayPacket, PingRelayResponse>(NetAddress.Relay, new(), relayNumber, cancellationToken, EndpointResolution.PreferIpv6, this.IsIncoming);
            if (rr.Result == NetResult.Success &&
            rr.Value is { } response)
            {
                dictionary.TryAdd(i, response);
            }
        });

        try
        {
            await task;
        }
        catch
        {
        }

        var sb = new StringBuilder();
        for (var i = 0; i < endpointArray.Length; i++)
        {
            if (dictionary.TryGetValue(i, out var response))
            {
                sb.AppendLine($"{i}: {endpointArray[i].ToString()} {response.ToString()}");
            }
            else
            {
            }
        }

        return sb.ToString();
    }

    /*internal bool TryEncrypt(int relayNumber, NetAddress destination, ReadOnlySpan<byte> content, out BytePool.RentMemory encrypted, out NetEndpoint relayEndpoint)
        => this.relayKey.TryEncrypt(relayNumber, destination, content, out encrypted, out relayEndpoint);*/

    internal async Task Terminate(CancellationToken cancellationToken)
    {
    }

    private void ResetRelayKeyInternal()
    {// using (this.relayNodes.LockObject.EnterScope())
        this.relayKey = new(this.relayNodes);
    }

    private RelayResult CanAddRelayInternal(RelayId relayId, NetEndpoint endpoint)
    {// using (this.relayNodes.LockObject.EnterScope())
        if (endpoint.RelayId != 0)
        {
            return RelayResult.InvalidEndpoint;
        }

        if (this.IsIncoming)
        {// Incoming circuit
            if (this.relayNodes.Count >= MaxIncomingSerialRelays)
            {
                return RelayResult.SerialRelayLimit;
            }
        }
        else
        {// Outgoing circuit
            if (this.relayNodes.Count >= MaxOutgoingSerialRelays)
            {
                return RelayResult.SerialRelayLimit;
            }
        }

        if (this.relayNodes.RelayIdChain.ContainsKey(relayId))
        {
            return RelayResult.DuplicateRelayId;
        }

        if (this.relayNodes.EndpointChain.ContainsKey(endpoint))
        {
            return RelayResult.DuplicateEndpoint;
        }

        return RelayResult.Success;
    }
}

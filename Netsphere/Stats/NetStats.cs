// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using Netsphere.Crypto;
using Netsphere.Relay;

namespace Netsphere.Stats;

[TinyhandObject(UseServiceProvider = true, LockObject = "lockObject")]
public sealed partial class NetStats
{
    public const string Filename = "NetStat.tinyhand";

    private const int EndpointTrustCapacity = 32;
    private const int EndpointTrustMinimum = 4;

    public NetStats(ILogger<NetStats> logger, NetBase netBase, NodeControl nodeControl)
    {
        this.logger = logger;
        this.netBase = netBase;
        this.NodeControl = nodeControl;
    }

    #region FieldAndProperty

    // [Key(0)]
    // public long LastMics { get; private set; }

    [Key(1)]
    public NodeControl NodeControl { get; private set; }

    [Key(2)]
    private IPEndPoint? lastIpv4Endpoint;

    [Key(3)]
    private IPEndPoint? lastIpv6Endpoint;

    [Key(4)]
    private int lastOutboundPort;

    [Key(5)]
    public int LastPort { get; private set; }

    [IgnoreMember]
    public bool IsIpv6Supported { get; private set; } = true;

    [IgnoreMember]
    public NetNode? OwnNetNode { get; private set; }

    [IgnoreMember]
    public NodeType OwnNodeType { get; private set; }

    [IgnoreMember]
    public NetNode? PeerNetNode { get; private set; }

    public bool IsOpenNode => this.OwnNodeType == NodeType.Direct;

    [IgnoreMember]
    public bool DirectConfirmed { get; private set; }

    [IgnoreMember]
    public TrustSource<IPEndPoint?> Ipv4Endpoint { get; private set; } = new(EndpointTrustCapacity, EndpointTrustMinimum);

    [IgnoreMember]
    public TrustSource<IPEndPoint?> Ipv6Endpoint { get; private set; } = new(EndpointTrustCapacity, EndpointTrustMinimum);

    [IgnoreMember]
    public TrustSource<int> OutboundPort { get; private set; } = new(EndpointTrustCapacity, EndpointTrustMinimum);

    private readonly Lock lockObject = new();
    private readonly ILogger logger;
    private readonly NetBase netBase;

    #endregion

    public bool TryCreateEndpoint(ref NetAddress address, EndpointResolution endpointResolution, out NetEndpoint endPoint)
    {
        endPoint = default;
        if (endpointResolution == EndpointResolution.PreferIpv6)
        {
            if (this.IsIpv6Supported || !address.IsValidIpv4)
            {// Ipv6 supported or Ipv6 only
                address.TryCreateIpv6(ref endPoint);
                if (endPoint.IsValid)
                {
                    return true;
                }
            }

            // Ipv4
            return address.TryCreateIpv4(ref endPoint);
        }
        else if (endpointResolution == EndpointResolution.NetAddress)
        {
            if (this.IsIpv6Supported && address.IsValidIpv6)
            {
                address.TryCreateIpv6(ref endPoint);
                if (endPoint.IsValid)
                {
                    return true;
                }
            }

            return address.TryCreateIpv4(ref endPoint);
        }
        else if (endpointResolution == EndpointResolution.Ipv4)
        {
            return address.TryCreateIpv4(ref endPoint);
        }
        else if (endpointResolution == EndpointResolution.Ipv6)
        {
            return address.TryCreateIpv6(ref endPoint);
        }
        else
        {
            return false;
        }
    }

    public NodeType GetOwnNodeType()
    {
        if (this.OutboundPort.TryGet(out var port, out _))
        {
            return (port == this.netBase.NetOptions.Port && this.netBase.IsPortNumberSpecified) ?
                NodeType.Direct :
                NodeType.Cone;
        }
        else if (this.OutboundPort.IsInconsistent)
        {
            return NodeType.Symmetric;
        }

        return NodeType.Unknown;
    }

    public NetNode GetOwnNetNode()
    {
        this.Ipv4Endpoint.TryGet(out var ipv4, out _);
        this.Ipv6Endpoint.TryGet(out var ipv6, out _);
        var address = new NetAddress(ipv4?.Address, ipv6?.Address, (ushort)this.netBase.NetOptions.Port);
        return new(address, this.netBase.NodePublicKey);
    }

    public bool TryGetOwnNetNode([MaybeNullWhen(false)] out NetNode netNode)
    {
        var validIpv4 = this.Ipv4Endpoint.TryGet(out var ipv4, out _);
        var validIpv6 = this.Ipv6Endpoint.TryGet(out var ipv6, out _);
        if (validIpv4 || validIpv6)
        {
            netNode = new(new NetAddress(ipv4?.Address, ipv6?.Address, (ushort)this.netBase.NetOptions.Port), this.netBase.NodePublicKey);
            return true;
        }

        netNode = default;
        return false;
    }

    public void Reset()
    {
        this.Ipv4Endpoint.Clear();
        this.Ipv6Endpoint.Clear();
        this.OutboundPort.Clear();
    }

    public void Update(RelayCircuit incomingCircuit)
    {
        this.IsIpv6Supported = this.Ipv6Endpoint.TryGet(out _, out _);

        if (this.OwnNetNode is null)
        {
            if (this.TryGetOwnNetNode(out var netNode))
            {
                this.OwnNetNode = netNode;
            }
        }

        this.OwnNodeType = this.GetOwnNodeType();

        if (this.IsOpenNode)
        {
            this.PeerNetNode = this.OwnNetNode;
        }

        if (incomingCircuit.IsIncoming &&
            incomingCircuit.TryGetOutermostAddress(out var address))
        {
            this.PeerNetNode = new(address, this.netBase.NodePublicKey);
        }
    }

    public void ReportEndpoint(bool isIpv6, IPEndPoint? endpoint)
    {
        if (endpoint is not null &&
            !NetAddress.Validate(endpoint.Address))
        {
            return;
        }

        if (isIpv6)
        {
            this.Ipv6Endpoint.Add(endpoint);
        }
        else
        {
            this.Ipv4Endpoint.Add(endpoint);
        }
    }

    public void ReportEndpoint(IPEndPoint endpoint)
    {
        if (!NetAddress.Validate(endpoint.Address))
        {
            return;
        }

        if (endpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            this.Ipv6Endpoint.Add(endpoint);
        }
        else if (endpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            this.Ipv4Endpoint.Add(endpoint);
        }
    }

    public void SetOwnNetNodeForTest(NetAddress address, EncryptionPublicKey publicKey)
    {
        this.OwnNetNode = new(address, publicKey);
    }

    [TinyhandOnSerializing]
    private void OnSerializing()
    {
        // this.LastMics = Mics.GetUtcNow();
        this.LastPort = this.netBase.NetOptions.Port;

        this.Ipv4Endpoint.TryGet(out this.lastIpv4Endpoint, out _);
        this.Ipv6Endpoint.TryGet(out this.lastIpv6Endpoint, out _);
        this.OutboundPort.TryGet(out this.lastOutboundPort, out _);
    }

    [TinyhandOnDeserialized]
    private void OnDeserialized()
    {
        if (this.LastPort == this.netBase.NetOptions.Port &&
            this.lastOutboundPort != 0)
        {
            this.OutboundPort.Add(this.lastOutboundPort);
        }

        if (this.lastIpv4Endpoint is not null)
        {
            this.Ipv4Endpoint.Add(this.lastIpv4Endpoint);
        }

        if (this.lastIpv6Endpoint is not null)
        {
            this.Ipv6Endpoint.Add(this.lastIpv6Endpoint);
        }

        /*var utcNow = Mics.GetUtcNow();
        var range = new MicsRange(utcNow - Mics.FromMinutes(1), utcNow);
        if (!range.IsWithin(this.LastMics))
        {
            this.Reset();
        }*/
    }
}

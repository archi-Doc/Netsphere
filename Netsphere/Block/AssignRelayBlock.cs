// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Relay;

[TinyhandObject(ReservedKeyCount = 2)]
public partial class AssignRelayBlock
{
    public const int KeyAndNonceSize = 32;

    internal AssignRelayBlock(bool allowOpenSesami, bool allowUnknownIncoming)
    {
        this.AllowOpenSesami = allowOpenSesami;
        this.AllowUnknownIncoming = allowUnknownIncoming;
        this.InnerKeyAndNonce = new byte[KeyAndNonceSize];
        RandomVault.Default.NextBytes(this.InnerKeyAndNonce);
    }

    protected AssignRelayBlock()
    {
    }

    [Key(0)]
    public bool AllowOpenSesami { get; protected set; }

    /// <summary>
    /// Gets or sets a value indicating whether or not to allow communication from unknown nodes.<br/>
    /// This feature is designed with Engagement in mind.
    /// </summary>
    [Key(1)]
    public bool AllowUnknownIncoming { get; protected set; }

    [Key(2)]
    public byte[] InnerKeyAndNonce { get; protected set; } = [];

    // [Key(3)]
    // public Linkage? Linkage { get; private set; }
}

[TinyhandObject]
public partial class AssignRelayResponse
{
    public AssignRelayResponse(RelayResult result, RelayId innerRelayId, RelayId outerRelayId, long relayPoint, long retensionMics, NetNode? relayNetNode)
    {
        this.Result = result;
        this.InnerRelayId = innerRelayId;
        this.OuterRelayId = outerRelayId;
        this.RelayPoint = relayPoint;
        this.RetensionMics = retensionMics;
        this.RelayNetAddress = relayNetNode is null ? default : relayNetNode.Address;
    }

    protected AssignRelayResponse()
    {
    }

    [Key(0)]
    public RelayResult Result { get; protected set; }

    [Key(1)]
    public RelayId InnerRelayId { get; protected set; }

    [Key(2)]
    public RelayId OuterRelayId { get; protected set; }

    [Key(3)]
    public long RelayPoint { get; protected set; }

    [Key(4)]
    public long RetensionMics { get; protected set; }

    [Key(5)]
    public NetAddress RelayNetAddress { get; protected set; }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Relay;

[ValueLinkObject(Isolation = IsolationLevel.Serializable)]
public sealed partial class RelayNode
{
    [Link(Primary = true, Name = "LinkedList", Type = ChainType.LinkedList)]
    [Link(Name = "RelayId", TargetMember = "RelayId", Type = ChainType.Unordered)]
    public RelayNode(AssignRelayBlock assignRelayBlock, AssignRelayResponse assignRelayResponse, ClientConnection clientConnection)
    {
        this.Endpoint = new(assignRelayResponse.InnerRelayId, clientConnection.DestinationEndpoint.EndPoint);
        if (assignRelayResponse.RelayNetAddress.IsValid)
        {
            this.Address = new(assignRelayResponse.OuterRelayId, assignRelayResponse.RelayNetAddress);
        }
        else
        {
            this.Address = new(this.Endpoint);
        }

        this.ClientConnection = clientConnection;
        this.InnerKeyAndNonce = assignRelayBlock.InnerKeyAndNonce;
        this.OuterRelayId = assignRelayResponse.OuterRelayId;
    }

    #region FieldAndProperty

    public RelayId RelayId => this.Endpoint.RelayId;

    public RelayId OuterRelayId { get; private set; }

    public NetAddress Address { get; private set; }

    [Link(Type = ChainType.Unordered)]
    public NetEndpoint Endpoint { get; private set; }

    public ClientConnection ClientConnection { get; }

    internal byte[] InnerKeyAndNonce { get; private set; } = [];

    #endregion

    public override string ToString()
        => this.Endpoint.IsValid ? $"In:{this.RelayId} Out:{this.OuterRelayId}{NetAddress.RelayIdSeparator}{this.Endpoint.EndPoint?.ToString()}" : string.Empty;

    internal void Remove()
    {// using (RelayCircuit.relayNodes.LockObject.EnterScope())
        if (this.Goshujin is not null)
        {
            this.Goshujin = null;

            this.Endpoint = default;
            this.ClientConnection.CloseInternal();
        }
    }
}

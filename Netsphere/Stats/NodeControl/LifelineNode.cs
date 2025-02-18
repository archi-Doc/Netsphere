// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere.Stats;

[TinyhandObject]
[ValueLinkObject(Isolation = IsolationLevel.Serializable)]
public sealed partial class LifelineNode : NetNode
{
    public const int ConnectionFailureLimit = 3;

    #region FieldAndProperty

    [Key(2)]
    public long LastConnectedMics { get; set; }

    [Key(3)]
    public int ConnectionFailureCount { get; set; }

    #endregion

    [Link(Primary = true, Unique = true, Type = ChainType.Unordered, TargetMember = "Address", AddValue = false)]
    [Link(Type = ChainType.LinkedList, Name = "UncheckedList", AutoLink = false)]
    [Link(Type = ChainType.LinkedList, Name = "OnlineLink", AutoLink = false)]
    [Link(Type = ChainType.LinkedList, Name = "OfflineLink", AutoLink = false)]
    public LifelineNode(NetNode netNode)
    {
        this.Address = netNode.Address;
        this.PublicKey = netNode.PublicKey;
    }

    public LifelineNode(NetAddress netAddress, EncryptionPublicKey publicKey)
    {
        this.Address = netAddress;
        this.PublicKey = publicKey;
    }

    private LifelineNode()
    {
    }

    public void ConnectionSucceeded()
    {
        this.LastConnectedMics = Mics.FastCorrected;
        this.ConnectionFailureCount = 0;
    }

    public bool ConnectionFailed()
    {
        return this.ConnectionFailureCount++ >= ConnectionFailureLimit;
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;
using ValueLink.Integrality;

namespace Netsphere.Stats;

[TinyhandObject]
[ValueLinkObject(Integrality = true, Isolation = IsolationLevel.Serializable)]
public sealed partial class ActiveNode : NetNode
{
    /*internal class Integrality : Integrality<ActiveNode.GoshujinClass, ActiveNode>
    {
        public static readonly Integrality Default = new()
        {
            MaxItems = 100,
            RemoveIfItemNotFound = false,
        };

        public override bool Validate(GoshujinClass goshujin, ActiveNode newItem, ActiveNode? oldItem)
        {
            if (!newItem.Address.Validate())
            {
                return false;
            }
            else if (!newItem.Address.IsValidIpv4AndIpv6)
            {
                return false;
            }

            if (oldItem is not null)
            {
                // Console.WriteLine(oldItem.LastConnectedMicsValue);
                if (oldItem.LastConnectedMics >= newItem.LastConnectedMics)
                {
                    return false;
                }
            }

            return true;
        }
    }*/

    public ActiveNode(NetNode netNode)
    {
        this.Address = netNode.Address;
        this.PublicKey = netNode.PublicKey;
    }

    public ActiveNode(LifelineNode node)
    {
        this.Address = node.Address;
        this.PublicKey = node.PublicKey;
        this.LastConnectedMics = node.LastConnectedMics;
    }

    [Link(Primary = true, Type = ChainType.QueueList, Name = "Get")]
    [Link(Unique = true, Type = ChainType.Unordered, TargetMember = "Address", AddValue = false)]
    private ActiveNode()
    {
    }

    public void ConnectionSucceeded()
    {
        this.LastConnectedMicsValue = Mics.FastCorrected;
    }

    #region FieldAndProperty

    [Key(2)]
    [Link(Type = ChainType.ReverseOrdered, Accessibility = ValueLinkAccessibility.Public)]
    public long LastConnectedMics { get; private set; }

    #endregion
}

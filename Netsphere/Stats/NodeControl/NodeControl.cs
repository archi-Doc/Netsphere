// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Arc.Collections;
using Tinyhand.IO;
using ValueLink.Integrality;

namespace Netsphere.Stats;

[TinyhandObject(UseServiceProvider = true)]
public sealed partial class NodeControl
{
    public static readonly int MaxLifelineNodes = 32;
    public static readonly int SufficientLifelineNodes = 24;
    public static readonly int MaxActiveNodes = 256;
    public static readonly int SufficientActiveNodes = 32;
    public static readonly int GetActiveNodesMax = 16;
    public static readonly long LifelineCheckIntervalMics = Mics.FromDays(1);
    public static readonly long OnlineValidMics = Mics.FromMinutes(5);

    public NodeControl(NetBase netBase)
    {
        this.netBase = netBase;
    }

    private readonly NetBase netBase;

    #region FieldAndProperty

    [Key(0)]
    private LifelineNode.GoshujinClass lifelineNodes = new(); // this.lifelineNodes.SyncObject

    [Key(1)]
    private ActiveNode.GoshujinClass activeNodes = new(); // this.activeNodes.SyncObject

    // private ActiveNode.GoshujinClass unknownNodes = new(); // this.unknownNodes.SyncObject

    [IgnoreMember]
    public NetNode? RestorationNode { get; set; }

    public int CountLinfelineOnline => this.lifelineNodes.OnlineLinkChain.Count;

    public int CountLinfelineOffline => this.lifelineNodes.OfflineLinkChain.Count;

    public int CountActive => this.activeNodes.Count;

    // public int CountUnknown => this.unknownNodes.Count;

    public bool CanAddLifelineNode => this.lifelineNodes.Count < MaxLifelineNodes;

    public bool CanAddActiveNode => this.activeNodes.Count < MaxActiveNodes;

    public bool HasSufficientActiveNodes => this.CountActive >= SufficientActiveNodes;

    public bool NoOnlineNode => this.lifelineNodes.OnlineLinkChain.Count == 0 &&
        this.activeNodes.Count == 0;

    #endregion

    public void ShowNodes()
    {
        var sb = new StringBuilder();

        using (this.lifelineNodes.LockObject.EnterScope())
        {
            sb.AppendLine("Lifeline Online:");
            foreach (var x in this.lifelineNodes.OnlineLinkChain)
            {
                sb.AppendLine(x.ToString());
            }

            sb.AppendLine("Lifeline Offline:");
            foreach (var x in this.lifelineNodes.OfflineLinkChain)
            {
                sb.AppendLine(x.ToString());
            }
        }

        using (this.activeNodes.LockObject.EnterScope())
        {
            sb.AppendLine("Active:");
            foreach (var x in this.activeNodes.LastConnectedMicsChain)
            {
                sb.AppendLine($"{x.LastConnectedMics.MicsToDateTimeString()} {x.ToString()}");
            }
        }

        Console.WriteLine(sb.ToString());
    }

    public void FromLifelineNodeToActiveNode()
    {
        if (this.CountActive >= SufficientActiveNodes ||
            this.CountActive >= this.CountLinfelineOnline)
        {
            return;
        }

        using (this.lifelineNodes.LockObject.EnterScope())
        {
            using (this.activeNodes.LockObject.EnterScope())
            {
                foreach (var x in this.lifelineNodes.OnlineLinkChain)
                {// Lifeline Online -> Active
                    if (this.activeNodes.AddressChain.ContainsKey(x.Address))
                    {// Duplicate
                        continue;
                    }

                    if (this.CountActive >= SufficientActiveNodes)
                    {
                        return;
                    }

                    var item = new ActiveNode(x);
                    item.Goshujin = this.activeNodes;
                }
            }
        }
    }

    /// <summary>
    /// Maintains the lifeline nodes by adding online nodes to the lifeline and removing offline lifeline nodes if there are sufficient lifeline nodes.
    /// </summary>
    /// <param name="ownNode">The own net node.</param>
    public void MaintainLifelineNode(NetNode? ownNode)
    {
        using (this.lifelineNodes.LockObject.EnterScope())
        {
            using (this.activeNodes.LockObject.EnterScope())
            {
                // Own -> Active
                if (ownNode is not null &&
                    ownNode.Address.IsValidIpv4AndIpv6 &&
                    ownNode.PublicKey.IsValid &&
                    !this.activeNodes.AddressChain.ContainsKey(ownNode.Address) &&
                    this.CanAddActiveNode)
                {
                    var item = new ActiveNode(ownNode);
                    item.ConnectionSucceeded();
                    item.Goshujin = this.activeNodes;
                }

                // Active -> Lifeline
                foreach (var x in this.activeNodes)
                {
                    if (this.lifelineNodes.AddressChain.ContainsKey(x.Address))
                    {
                        continue;
                    }

                    if (!this.CanAddLifelineNode)
                    {
                        return;
                    }

                    if (!x.Address.IsValidIpv4AndIpv6 ||
                        ownNode?.Address.Equals(x.Address) == true)
                    {// Non-dual address or own address
                        continue;
                    }

                    var item = new LifelineNode(x.Address, x.PublicKey);
                    item.ConnectionSucceeded();
                    this.lifelineNodes.Add(item);
                    this.lifelineNodes.OnlineLinkChain.AddLast(item);
                }

                // Lifeline offline -> Remove
                TemporaryList<LifelineNode> deleteList = default;
                foreach (var x in this.lifelineNodes.OfflineLinkChain)
                {
                    if ((this.lifelineNodes.Count - deleteList.Count) > SufficientLifelineNodes)
                    {
                        deleteList.Add(x);
                    }
                }

                foreach (var x in deleteList)
                {
                    x.Goshujin = null;
                }
            }
        }
    }

    public bool TryAddActiveNode(NetNode node)
    {
        if (this.HasSufficientActiveNodes)
        {
            return false;
        }
        else if (!node.Validate() ||
            !node.Address.IsValidIpv4AndIpv6)
        {
            return false;
        }

        using (this.activeNodes.LockObject.EnterScope())
        {
            if (this.activeNodes.AddressChain.ContainsKey(node.Address))
            {
                return false;
            }

            var item = new ActiveNode(node);
            item.ConnectionSucceeded();
            item.Goshujin = this.activeNodes;
        }

        return true;
    }

    /*
    /// <summary>
    /// Tries to add a NetNode from the incoming connection and check the node later.
    /// </summary>
    /// <param name="node">The NetNode to add.</param>
    /// <returns>True if the node was added successfully, false otherwise.</returns>
    public bool TryAddUnknownNode(NetNode node)
    {
        if (this.HasSufficientActiveNodes)
        {
            return false;
        }

        if (this.unknownNodes.Count >= MaxUnknownNodes)
        {
            return false;
        }

        using (this.activeNodes.LockObject.EnterScope())
        {
            if (this.activeNodes.AddressChain.ContainsKey(node.Address))
            {
                return false;
            }
        }

        using (this.unknownNodes.LockObject.EnterScope())
        {
            var item = new ActiveNode(node);
            item.Goshujin = this.unknownNodes;
        }

        return true;
    }*/

    public bool TryGetUncheckedLifelineNode([MaybeNullWhen(false)] out NetNode node)
    {
        node = default;
        using (this.lifelineNodes.LockObject.EnterScope())
        {
            var obj = this.lifelineNodes.UncheckedListChain.First;
            if (obj is null)
            {
                return false;
            }

            node = obj;
            this.lifelineNodes.UncheckedListChain.Remove(obj);
        }

        return true;
    }

    public bool TryGetLifelineOnlineNode([MaybeNullWhen(false)] out NetNode node)
    {
        node = default;
        using (this.lifelineNodes.LockObject.EnterScope())
        {
            var obj = this.lifelineNodes.OnlineLinkChain.First;
            if (obj is null)
            {
                return false;
            }

            node = obj;
            this.lifelineNodes.OnlineLinkChain.AddLast(obj);
        }

        return true;
    }

    public bool TryGetActiveNode([MaybeNullWhen(false)] out NetNode node)
    {
        node = default;
        using (this.activeNodes.LockObject.EnterScope())
        {
            if (!this.activeNodes.GetChain.TryPeek(out var obj))
            {
                return false;
            }

            var n = RandomVault.Xoshiro.NextInt32(0, this.activeNodes.GetChain.Count >> 2);
            while (n-- > 0)
            {
                obj = obj.GetLink.Next;
                if (obj is null)
                {
                    return false;
                }
            }

            this.activeNodes.GetChain.Remove(obj);
            this.activeNodes.GetChain.Enqueue(obj);
            node = obj;
        }

        return true;
    }

    /*public bool TryGetActiveNode([MaybeNullWhen(false)] out NetNode node)
    {
        node = default;
        using (this.activeNodes.LockObject.EnterScope())
        {
            if (!this.activeNodes.GetChain.TryDequeue(out var obj))
            {
                return false;
            }

            this.activeNodes.GetChain.Enqueue(obj);
            node = obj;
        }

        return true;
    }*/

    /*public bool TryGetUnknownNode([MaybeNullWhen(false)] out NetNode node)
    {
        node = default;
        using (this.unknownNodes.LockObject.EnterScope())
        {
            if (!this.unknownNodes.GetChain.TryPeek(out var obj))
            {
                return false;
            }

            obj.Goshujin = default;
            node = obj;
        }

        return true;
    }*/

    /*public BytePool.RentMemory DifferentiateActiveNode(ReadOnlyMemory<byte> memory)
    {
        return ActiveNode.Integrality.Default.Differentiate(this.activeNodes, memory);
    }

    public Task<IntegralityResult> IntegrateActiveNode(IntegralityBrokerDelegate brokerDelegate, CancellationToken cancellationToken)
    {
        return ActiveNode.Integrality.Default.Integrate(this.activeNodes, brokerDelegate, cancellationToken);
    }*/

    public BytePool.RentMemory GetActiveNodes()
    {
        var writer = TinyhandWriter.CreateFromBytePool();
        try
        {
            using (this.activeNodes.LockObject.EnterScope())
            {
                var count = 0;
                ActiveNode? node = this.activeNodes.LastConnectedMicsChain.First;
                while (count++ < GetActiveNodesMax && node is not null)
                {
                    TinyhandSerializer.SerializeObject(ref writer, node);
                    node = node.LastConnectedMicsLink.Next;
                }
            }

            return writer.FlushAndGetRentMemory();
        }
        finally
        {
            writer.Dispose();
        }
    }

    public void ProcessGetActiveNodes(ReadOnlySpan<byte> span)
    {
        if (span.Length == 0)
        {
            return;
        }

        var reader = new TinyhandReader(span);
        using (this.activeNodes.LockObject.EnterScope())
        {
            try
            {
                while (!reader.End)
                {
                    var node = TinyhandSerializer.DeserializeObject<ActiveNode>(ref reader);
                    if (node is null || !node.Validate())
                    {
                        continue;
                    }

                    if (this.activeNodes.AddressChain.TryGetValue(node.Address, out var item))
                    {// Exists
                        if (item.LastConnectedMics < node.LastConnectedMics)
                        {// Replace
                            item.Goshujin = default;
                            node.Goshujin = this.activeNodes;
                        }
                    }
                    else
                    {// New
                        node.Goshujin = this.activeNodes;
                        if (this.activeNodes.Count >= MaxActiveNodes)
                        {
                            if (this.activeNodes.LastConnectedMicsChain.Last is { } last)
                            {
                                last.Goshujin = default;
                            }
                        }
                    }
                }
            }
            catch
            {
                return;
            }
        }
    }

    public void ReportLifelineNodeConnection(NetNode node, ConnectionResult result)
    {
        using (this.lifelineNodes.LockObject.EnterScope())
        {
            using (this.activeNodes.LockObject.EnterScope())
            {
                var item = this.lifelineNodes.AddressChain.FindFirst(node.Address);
                if (item is not null)
                {// Lifeline nodes
                    if (item.Goshujin is { } g)
                    {
                        g.UncheckedListChain.Remove(item);
                        if (result == ConnectionResult.Success)
                        {// -> Online
                            item.ConnectionSucceeded();
                            g.OnlineLinkChain.AddLast(item);
                            g.OfflineLinkChain.Remove(item);
                        }
                        else
                        {
                            if (item.ConnectionFailed())
                            { // Remove
                                item.Goshujin = default;
                            }
                            else
                            {// -> Offline
                                g.OnlineLinkChain.Remove(item);
                                g.OfflineLinkChain.AddLast(item);
                            }
                        }
                    }
                }

                if (result == ConnectionResult.Success)
                {
                    var item2 = this.activeNodes.AddressChain.FindFirst(node.Address);
                    if (item2 is not null)
                    {
                        item2.ConnectionSucceeded();
                    }
                    else if (this.CanAddActiveNode)
                    {
                        item2 = new(node);
                        item2.ConnectionSucceeded();
                        item2.Goshujin = this.activeNodes;
                    }
                }
                else if (result == ConnectionResult.Failure)
                {
                }
            }
        }
    }

    public void ReportActiveNodeConnection(NetNode node, ConnectionResult result)
    {
        using (this.activeNodes.LockObject.EnterScope())
        {
            var item = this.activeNodes.AddressChain.FindFirst(node.Address);
            if (item is not null)
            {
                if (result == ConnectionResult.Success)
                {
                    item.ConnectionSucceeded();
                }
                else if (result == ConnectionResult.Failure)
                {
                    item.Goshujin = default;
                }
            }
            else
            {
                if (result == ConnectionResult.Success &&
                    this.CanAddActiveNode)
                {
                    item = new(node);
                    item.ConnectionSucceeded();
                    item.Goshujin = this.activeNodes;
                }
            }
        }
    }

    public void Validate()
    {
        using (this.lifelineNodes.LockObject.EnterScope())
        {
            using (this.activeNodes.LockObject.EnterScope())
            {
                this.ValidateInternal();
            }
        }
    }

    public void Trim(bool trimLifeline, bool trimActive)
    {
        using (this.lifelineNodes.LockObject.EnterScope())
        {
            using (this.activeNodes.LockObject.EnterScope())
            {
                this.TrimInternal(trimLifeline, trimActive);
            }
        }
    }

    [TinyhandOnDeserialized]
    private void OnAfterDeserialize()
    {
        using (this.lifelineNodes.LockObject.EnterScope())
        {
            TemporaryList<LifelineNode> deleteList = default;
            foreach (var x in this.lifelineNodes)
            {
                if (!x.Address.IsValidIpv4AndIpv6)
                {
                    deleteList.Add(x);
                }
            }

            foreach (var x in deleteList)
            {
                x.Goshujin = default;
            }
        }

        using (this.activeNodes.LockObject.EnterScope())
        {
            TemporaryList<ActiveNode> deleteList = default;
            foreach (var x in this.activeNodes)
            {
                if (!x.Address.IsValidIpv4AndIpv6)
                {
                    deleteList.Add(x);
                }
            }

            foreach (var x in deleteList)
            {
                x.Goshujin = default;
            }
        }

        this.LoadNodeList();
        this.Prepare();
    }

    [TinyhandOnReconstructed]
    private void OnAfterReconstruct()
    {
        this.LoadNodeList();
    }

    private void Prepare()
    {
        TemporaryList<LifelineNode> offlineToUnchecked = default;
        foreach (var x in this.lifelineNodes.OfflineLinkChain)
        {// Offline -> Unchecked
            offlineToUnchecked.Add(x);
        }

        foreach (var x in offlineToUnchecked)
        {
            this.lifelineNodes.OfflineLinkChain.Remove(x);
            this.lifelineNodes.UncheckedListChain.AddFirst(x);
        }

        this.ValidateInternal();
        this.TrimInternal(true, true);
    }

    private void LoadNodeList()
    {// Load NetOptions.NodeList
        var nodes = this.netBase.NetOptions.NodeList;
        foreach (var x in nodes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!NetNode.TryParse(x, out var node, out _))
            {
                continue;
            }

            if (!this.lifelineNodes.AddressChain.TryGetValue(node.Address, out var item))
            {// New
                item = new LifelineNode(node);
                this.lifelineNodes.Add(item);
                this.lifelineNodes.UncheckedListChain.AddFirst(item);
                continue;
            }
        }
    }

    private void TrimInternal(bool trimLifeline, bool trimActive)
    {
        if (trimLifeline)
        {
            var range = MicsRange.FromPastToFastCorrected(LifelineCheckIntervalMics);
            foreach (var x in this.lifelineNodes)
            {
                if (!range.IsWithin(x.LastConnectedMics) &&
                    x.Goshujin is { } g)
                {// Online/Offline -> Unchecked
                    g.UncheckedListChain.AddFirst(x);
                    g.OnlineLinkChain.Remove(x);
                    g.OfflineLinkChain.Remove(x);
                }
            }
        }

        if (trimActive)
        {
            var range = MicsRange.FromPastToFastCorrected(OnlineValidMics);
            TemporaryList<ActiveNode> deleteList = default;
            foreach (var x in this.activeNodes)
            {
                if (!range.IsWithin(x.LastConnectedMics) &&
                    x.Goshujin is { } g)
                {
                    deleteList.Add(x);
                }
            }

            foreach (var x in deleteList)
            {
                x.Goshujin = default;
            }
        }
    }

    private void ValidateInternal()
    {
        // Validate nodes.
        List<LifelineNode>? lifelineList = default;
        foreach (var x in this.lifelineNodes)
        {
            if (!x.Validate())
            {
                lifelineList ??= new();
                lifelineList.Add(x);
            }
        }

        if (lifelineList is not null)
        {
            foreach (var x in lifelineList)
            {
                x.Goshujin = null;
            }
        }

        List<ActiveNode>? onlineList = default;
        foreach (var x in this.activeNodes)
        {
            if (!x.Validate())
            {
                onlineList ??= new();
                onlineList.Add(x);
            }
        }

        if (onlineList is not null)
        {
            foreach (var x in onlineList)
            {
                x.Goshujin = null;
            }
        }
    }
}

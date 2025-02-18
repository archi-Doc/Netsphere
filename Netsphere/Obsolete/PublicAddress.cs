// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Stats;

/*[TinyhandObject]
public partial class PublicAddress
{
    private const int MaxItems = 30;
    private const int PriorityWeight = 10;

    public enum State
    {
        Unknown,
        Unavailable,
        Fixed,
        Changed,
    }

    public PublicAddress()
    {
    }

    [TinyhandObject]
    [ValueLinkObject]
    private partial class Item
    {
        [Link(Primary = true, Type = ChainType.QueueList, Name = "Queue")]
        public Item()
        {
        }

        public Item(IPAddress? address, int weight)
        {
            this.Address = address;
            this.Weight = weight;
        }

        [Key(0)]
        public IPAddress? Address { get; private set; }

        [Key(1)]
        public int Weight { get; private set; }
    }

    [TinyhandObject]
    [ValueLinkObject]
    private partial class Counter
    {
        public Counter()
        {
        }

        public Counter(IPAddress? address)
        {
            this.Address = address;
        }

        public void Add(int weight)
        {
            this.WeightValue += weight;
            if (this.WeightValue == 0)
            {
                this.Goshujin = null;
            }
        }

        [Key(0)]
        [Link(Primary = true, Type = ChainType.Unordered, AddValue = false)]
        public IPAddress? Address { get; private set; }

        [Key(1)]
        [Link(Type = ChainType.Ordered)]
        public int Weight { get; private set; }
    }

    #region FieldAndProperty

    [IgnoreMember]
    private Lock lockObject = new();

    [Key(0)]
    public State AddressState { get; private set; }

    [Key(1)]
    public IPAddress? Address { get; private set; }

    [Key(2)]
    private Item.GoshujinClass items = new();

    [Key(3)]
    private Counter.GoshujinClass counters = new();

    #endregion

    public void ReportAddress(bool priority, IPAddress? address)
    {
        var weight = priority ? PriorityWeight : 1;
        using (this.lockObject.EnterScope())
        {
            Item? item;
            Counter? counter;

            while (this.items.Count >= MaxItems)
            {// Remove
                item = this.items.QueueChain.Peek();

                if (this.counters.AddressChain.TryGetValue(item.Address, out counter))
                {
                    counter.Add(-item.Weight);
                }

                item.Goshujin = null;
            }

            item = new(address, weight);
            item.Goshujin = this.items;
            if (!this.counters.AddressChain.TryGetValue(item.Address, out counter))
            {
                counter = new(address);
                counter.Goshujin = this.counters;
            }

            counter.Add(weight);

            this.InternalUpdate();
        }
    }

    public void Reset()
    {
        using (this.lockObject.EnterScope())
        {
            this.AddressState = State.Unknown;
            this.Address = null;
            this.items.Clear();
            this.counters.Clear();
        }
    }

    public string Dump()
    {
        string st;
        using (this.lockObject.EnterScope())
        {
            if (this.AddressState == State.Unknown ||
                this.AddressState == State.Unavailable)
            {
                st = $"{this.AddressState}";
            }
            else
            {
                st = $"{this.AddressState} Address {this.Address?.ToString()}";
            }
        }

        return st;
    }

    private void InternalUpdate()
    {
        using (this.lockObject.EnterScope())
        {
            if (this.AddressState == State.Unknown)
            {// Unknown -> Unavailable, Fixed
                if (this.counters.WeightChain.Last is { } last &&
                    last.Weight >= PriorityWeight)
                {
                    this.Address = last.Address;
                    this.AddressState = this.Address is null ? State.Unavailable : State.Fixed;
                }
            }
            else if (this.AddressState == State.Unavailable)
            {// Unavailable -> Fixed
                if (this.counters.WeightChain.Last is { } last &&
                    last.Weight >= PriorityWeight &&
                    last.Address is not null)
                {
                    this.Address = last.Address;
                    this.AddressState = State.Fixed;
                }
            }
            else if (this.AddressState == State.Fixed)
            {// Fixed -> Changed
                if (this.counters.WeightChain.Last?.Address is { } address &&
                    !address.Equals(this.Address))
                {
                    this.AddressState = State.Changed;
                }
            }
            else if (this.AddressState == State.Changed)
            {
            }
        }
    }
}*/

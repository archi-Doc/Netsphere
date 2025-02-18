// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Netsphere;

[TinyhandObject]
public sealed partial class TrustSource<T>
{
    public TrustSource(int capacity, int trustMinimum)
    {
        Debug.Assert(capacity > 0);
        Debug.Assert(trustMinimum > 0);
        Debug.Assert(capacity >= trustMinimum);

        this.Capacity = capacity;
        this.TrustMinimum = trustMinimum;
        this.counterPool = new(() => new(), this.Capacity);
    }

    public TrustSource()
    {
        this.counterPool = default!;
    }

    [TinyhandObject]
    [ValueLinkObject]
    private partial class Item
    {
        [Link(Primary = true, Type = ChainType.QueueList, Name = "Queue")]
        public Item()
        {
            this.Value = default!;
        }

        public Item(T value)
        {
            this.Value = value;
        }

        [Key(0)]
        public T Value { get; set; }

        // [Key(1)]
        // public long AddedMics { get; set; }

        public override string ToString()
            => $"Item {this.Value?.ToString()}";
    }

    [TinyhandObject]
    [ValueLinkObject]
    private partial class Counter
    {
        public Counter()
        {
            this.Value = default!;
        }

        public Counter(T value)
        {
            this.Value = value;
        }

        [Key(0)]
        [Link(Primary = true, Type = ChainType.Unordered, AddValue = false)]
        public T Value { get; set; }

        [Key(1)]
        [Link(Type = ChainType.Ordered)]
        public long Count { get; set; }

        public override string ToString()
            => $"{this.Count} x {this.Value?.ToString()}";
    }

    #region FieldAndProperty

    [IgnoreMember]
    public int Capacity { get; private set; }

    [IgnoreMember]
    public int TrustMinimum { get; private set; }

    public int Count => this.items.Count;

    public bool IsFixed => this.isFixed;

    public bool IsInconsistent => !this.isFixed && this.IsAboveMinimum;

    /// <summary>
    /// Gets a value indicating whether the count of items is above the minimum trust value.
    /// </summary>
    public bool IsAboveMinimum => this.items.Count >= this.TrustMinimum;

    // public T? FixedOrDefault => this.fixedValue;

    private readonly Lock lockObject = new();
    // private readonly ObjectPool<Item> itemPool;
    private readonly ObjectPool<Counter> counterPool;

    [Key(0)]
    private Item.GoshujinClass items = new(); // using (this.lockObject.EnterScope())

    [Key(1)]
    private Counter.GoshujinClass counters = new(); // using (this.lockObject.EnterScope())

    [Key(2)]
    private bool isFixed;

    [Key(3)]
    private T? fixedValue;

    #endregion

    public void Add(T value)
    {
        using (this.lockObject.EnterScope())
        {
            Counter? counter;
            if (this.counters.ValueChain.TryGetValue(value, out counter))
            {// Increment counter
                counter.CountValue++;
            }
            else
            {// New counter
                counter = this.counterPool.Rent();
                counter.Value = value;
                counter.Count = 1;
                counter.Goshujin = this.counters;
            }

            Item item;
            if (this.items.Count >= this.Capacity)
            {
                item = this.items.QueueChain.Dequeue();
                if (this.counters.ValueChain.TryGetValue(item.Value, out var counter2))
                {
                    counter2.CountValue--;
                    if (counter2.CountValue == 0)
                    {// Return counter
                        counter2.Goshujin = null;
                        this.counterPool.Return(counter2);
                    }
                }
            }
            else
            {
                item = new();
            }

            item.Value = value;
            item.Goshujin = this.items;

            if (this.isFixed)
            {// Fixed
                if (this.fixedValue is null)
                {
                    if (value is null)
                    {// Identical
                        return;
                    }
                }
                else if (this.fixedValue.Equals(value))
                {// Identical
                    return;
                }

                if (this.counters.CountChain.Last is { } last)
                {
                    if (last.Value is null)
                    {
                        if (this.fixedValue is not null && this.CanFix(last))
                        {// Fix -> Unfix
                            this.ClearInternal();
                        }
                    }
                    else if (!last.Value.Equals(this.fixedValue) && this.CanFix(last))
                    {// Fix -> Unfix
                        this.ClearInternal();
                    }
                }
            }
            else
            {// Not fixed
                var last = this.counters.CountChain.Last;
                if (last is not null && this.CanFix(last))
                {// Fix
                 // this.ClearInternal(false);
                    this.isFixed = true;
                    this.fixedValue = last.Value;
                }
            }
        }
    }

    public bool TryGetFixed([MaybeNullWhen(false)] out T value)
    {
        value = this.fixedValue;
        return this.isFixed && value is not null;
    }

    public bool TryGet([MaybeNullWhen(false)] out T value, out bool isFixed)
    {
        value = this.fixedValue;
        if (this.isFixed)
        {// Fixed
            isFixed = true;
            return value is not null;
        }
        else
        {// Not fixed
            using (this.lockObject.EnterScope())
            {
                isFixed = false;
                var last = this.counters.CountChain.Last;
                if (last is not null)
                {// Use the value with the highest count as the provisional value.
                    value = last.Value;
                    return value is not null;
                }
                else
                {// No value
                    value = default;
                    return false;
                }
            }
        }
    }

    public void Clear()
    {
        using (this.lockObject.EnterScope())
        {
            this.ClearInternal();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearInternal()
    {
        this.items.Clear();
        this.counters.Clear();
        this.isFixed = false;
        this.fixedValue = default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanFix(Counter counter)
    {
        return counter.Count >= this.TrustMinimum &&
            counter.Count >= (this.items.Count >> 1);
    }
}

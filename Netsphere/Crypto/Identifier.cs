// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Crypto;

/// <summary>
/// Immutable identifier of objects in Lp.
/// </summary>
[TinyhandObject]
public readonly partial struct Identifier : IEquatable<Identifier>, IComparable<Identifier>
{
    public const string Name = "Identifier";
    public const int Length = 32;

    public static readonly Identifier Zero = default;

    public static readonly Identifier One = new(1);

    public static readonly Identifier Two = new(2);

    public static readonly Identifier Three = new(3);

    public static Identifier FromReadOnlySpan(ReadOnlySpan<byte> input)
        => new(Sha3Helper.Get256_UInt64(input));

    public Identifier(int id0)
    {
        this.Id0 = (ulong)id0;
        this.Id1 = 0;
        this.Id2 = 0;
        this.Id3 = 0;
    }

    public Identifier(ulong id0)
    {
        this.Id0 = id0;
        this.Id1 = 0;
        this.Id2 = 0;
        this.Id3 = 0;
    }

    public Identifier(ulong id0, ulong id1, ulong id2, ulong id3)
    {
        this.Id0 = id0;
        this.Id1 = id1;
        this.Id2 = id2;
        this.Id3 = id3;
    }

    public Identifier((ulong Id0, ulong Id1, ulong Id2, ulong Id3) id)
    {
        this.Id0 = id.Id0;
        this.Id1 = id.Id1;
        this.Id2 = id.Id2;
        this.Id3 = id.Id3;
    }

    public Identifier(Identifier identifier)
    {
        this.Id0 = identifier.Id0;
        this.Id1 = identifier.Id1;
        this.Id2 = identifier.Id2;
        this.Id3 = identifier.Id3;
    }

    public Identifier(byte[] byteArray)
    {
        if (byteArray.Length < Length)
        {
            throw new ArgumentException($"Length of a byte array must be at least {Length}");
        }

        var s = byteArray.AsSpan();
        this.Id0 = BitConverter.ToUInt64(s);
        s = s.Slice(8);
        this.Id1 = BitConverter.ToUInt64(s);
        s = s.Slice(8);
        this.Id2 = BitConverter.ToUInt64(s);
        s = s.Slice(8);
        this.Id3 = BitConverter.ToUInt64(s);
    }

    [Key(0)]
    public readonly ulong Id0;

    [Key(1)]
    public readonly ulong Id1;

    [Key(2)]
    public readonly ulong Id2;

    [Key(3)]
    public readonly ulong Id3;

    public bool TryWriteBytes(Span<byte> destination)
    {
        if (destination.Length < Length)
        {
            throw new ArgumentException($"Length of a byte array must be at least {Length}");
        }

        var d = destination;
        BitConverter.TryWriteBytes(d, this.Id0);
        d = d.Slice(8);
        BitConverter.TryWriteBytes(d, this.Id1);
        d = d.Slice(8);
        BitConverter.TryWriteBytes(d, this.Id2);
        d = d.Slice(8);
        BitConverter.TryWriteBytes(d, this.Id3);
        return true;
    }

    public bool IsDefault()
        => this.Id0 == 0 && this.Id1 == 0 && this.Id2 == 0 && this.Id3 == 0;

    public bool Equals(Identifier other)
        => this.Id0 == other.Id0 && this.Id1 == other.Id1 && this.Id2 == other.Id2 && this.Id3 == other.Id3;

    public override int GetHashCode() => (int)this.Id0; // HashCode.Combine(this.Id0, this.Id1, this.Id2, this.Id3);

    public override string ToString() => this.Id0 switch
    {
        0 => $"{Name} Zero",
        1 => $"{Name} One",
        2 => $"{Name} Two",
        3 => $"{Name} Three",
        _ => $"{Name} {this.Id0:D4}",
    };

    public int CompareTo(Identifier other)
    {
        if (this.Id0 > other.Id0)
        {
            return 1;
        }
        else if (this.Id0 < other.Id0)
        {
            return -1;
        }

        if (this.Id1 > other.Id1)
        {
            return 1;
        }
        else if (this.Id1 < other.Id1)
        {
            return -1;
        }

        if (this.Id2 > other.Id2)
        {
            return 1;
        }
        else if (this.Id2 < other.Id2)
        {
            return -1;
        }

        if (this.Id3 > other.Id3)
        {
            return 1;
        }
        else if (this.Id3 < other.Id3)
        {
            return -1;
        }

        return 0;
    }
}

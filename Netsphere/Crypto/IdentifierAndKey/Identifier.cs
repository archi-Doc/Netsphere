// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Netsphere.Crypto;

/// <summary>
/// Immutable identifier of objects in Lp.
/// </summary>
[TinyhandObject]
public readonly partial struct Identifier : IEquatable<Identifier>, IComparable<Identifier>, IStringConvertible<Identifier>
{
    public const string Name = "Identifier";
    public const int Length = 32;

    public static readonly Identifier Zero = default;

    public static readonly Identifier One = new(1);

    public static readonly Identifier Two = new(2);

    public static readonly Identifier Three = new(3);

    public static Identifier FromReadOnlySpan(ReadOnlySpan<byte> input)
        => new(Sha3Helper.Get256_UInt64(input));

    #region IStringConvertible

    public static bool TryParse(ReadOnlySpan<char> source, [MaybeNullWhen(false)] out Identifier identifier, out int read)
    {// Base64(Length) or Alias(*)
        read = SeedKeyHelper.CalculateStringLength(source);
        if (read == SeedKeyHelper.RawPublicKeyLengthInBase64)
        {// key
            Span<byte> keyAndChecksum = stackalloc byte[SeedKeyHelper.PublicKeyAndChecksumSize];
            if (Base64.Url.FromStringToSpan(source.Slice(0, SeedKeyHelper.RawPublicKeyLengthInBase64), keyAndChecksum, out _) &&
                    SeedKeyHelper.ValidateChecksum(keyAndChecksum))
            {
                identifier = new(keyAndChecksum);
                return true;
            }
        }
        else if (read > 0 && Alias.TryGetIdentifierFromAlias(source.Slice(0, read), out identifier))
        {
            return true;
        }

        identifier = default;
        return false;
    }

    public static int MaxStringLength => SeedKeyHelper.RawPublicKeyLengthInBase64;

    public int GetStringLength()
    {
        if (Alias.TryGetAliasFromIdentifier(this, out var alias))
        {
            return alias.Length;
        }
        else
        {
            return SeedKeyHelper.RawPublicKeyLengthInBase64;
        }
    }

    public bool TryFormat(Span<char> destination, out int written)
    {
        if (Alias.TryGetAliasFromIdentifier(this, out var alias))
        {
            if (destination.Length < alias.Length)
            {
                written = 0;
                return false;
            }

            alias.CopyTo(destination);
            written = alias.Length;
            return true;
        }
        else
        {
            return SeedKeyHelper.TryFormatPublicKeyWithoutBracket(this.AsSpan(), destination, out written);
        }
    }

    #endregion

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

    public Identifier(ReadOnlySpan<byte> b)
    {
        if (b.Length < Length)
        {
            throw new ArgumentException($"Length of a byte array must be at least {Length}");
        }

        this.Id0 = BitConverter.ToUInt64(b);
        b = b.Slice(sizeof(ulong));
        this.Id1 = BitConverter.ToUInt64(b);
        b = b.Slice(sizeof(ulong));
        this.Id2 = BitConverter.ToUInt64(b);
        b = b.Slice(sizeof(ulong));
        this.Id3 = BitConverter.ToUInt64(b);
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

    public bool IsDefault
        => this.Id0 == 0 && this.Id1 == 0 && this.Id2 == 0 && this.Id3 == 0;

    public bool Equals(Identifier other)
        => this.Id0 == other.Id0 && this.Id1 == other.Id1 && this.Id2 == other.Id2 && this.Id3 == other.Id3;

    public override int GetHashCode() => (int)this.Id0; // HashCode.Combine(this.Id0, this.Id1, this.Id2, this.Id3);

    public override string ToString()
    {
        Span<char> s = stackalloc char[SeedKeyHelper.RawPublicKeyLengthInBase64];
        this.TryFormat(s, out var written);
        return s.Slice(0, written).ToString();
    }

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

    [UnscopedRef]
    public ReadOnlySpan<byte> AsSpan()
        => MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in this), 1));

    internal Span<byte> UnsafeAsSpan()
        => MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref Unsafe.AsRef(in this), 1));
}

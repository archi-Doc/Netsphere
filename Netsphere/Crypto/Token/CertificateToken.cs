// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Netsphere.Misc;
using Tinyhand.IO;

#pragma warning disable SA1202 // Elements should be ordered by access

namespace Netsphere.Crypto;

/// <summary>
/// Represents a certificate token.
/// </summary>
/// <typeparam name="T">The type of the certificate object.</typeparam>
[TinyhandObject]
public partial class CertificateToken<T> : ISignAndVerify, IEquatable<CertificateToken<T>>, IStringConvertible<CertificateToken<T>>
    where T : ITinyhandSerializable<T>
{
    private const char Identifier = 'C';

    public static CertificateToken<T> CreateAndSign(T target, SeedKey seedKey, Connection connection)
    {
        var token = new CertificateToken<T>(target);
        NetHelper.Sign(seedKey, token, connection);
        return token;
    }

    public CertificateToken()
    {
        this.Target = default!;
    }

    public CertificateToken(T target)
    {
        this.Target = target;
    }

    public static int MaxStringLength => 256;

    #region FieldAndProperty

    [Key(0)]
    public char TokenIdentifier { get; private set; } = Identifier;

    [Key(1)]
    public SignaturePublicKey PublicKey { get; set; }

    [Key(2, Level = TinyhandWriter.DefaultSignatureLevel + 1)]
    public byte[] Signature { get; set; } = Array.Empty<byte>();

    [Key(3)]
    public long SignedMics { get; set; }

    [Key(4)]
    public ulong Salt { get; set; }

    [Key(5)]
    public T Target { get; set; }

    #endregion

    public static bool TryParse(ReadOnlySpan<char> source, [MaybeNullWhen(false)] out CertificateToken<T> token, out int read)
        => TokenHelper.TryParse(Identifier, source, out token, out read);

    public bool Validate()
    {
        if (this.TokenIdentifier != Identifier)
        {
            return false;
        }
        else if (this.SignedMics == 0)
        {
            return false;
        }

        return true;
    }

    public bool Equals(CertificateToken<T>? other)
    {
        if (other == null)
        {
            return false;
        }

        return this.PublicKey.Equals(other.PublicKey) &&
            this.Signature.SequenceEqual(other.Signature) &&
            this.SignedMics == other.SignedMics;
    }

    public override string ToString()
        => TokenHelper.ToBase64(this, Identifier);

    public int GetStringLength()
        => -1;

    public bool TryFormat(Span<char> destination, out int written)
        => TokenHelper.TryFormat(this, Identifier, destination, out written);
}

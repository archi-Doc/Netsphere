// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Netsphere.Misc;
using Tinyhand.IO;

#pragma warning disable SA1202 // Elements should be ordered by access

namespace Netsphere.Crypto;

/// <summary>
/// Represents an authentication token.
/// </summary>
[TinyhandObject]
public sealed partial class AuthenticationToken : ISignAndVerify, IEquatable<AuthenticationToken>, IStringConvertible<AuthenticationToken>
{
    private const char Identifier = 'A';

    /*static AuthenticationToken()
    {
        var maxLength = SignaturePublicKey.MaxStringLength + Base64.Url.GetEncodedLength(KeyHelper.SignatureLength + 8 + 4); // 146
    }*/

    public static AuthenticationToken CreateAndSign(SeedKey seedKey, Connection connection)
    {
        var token = new AuthenticationToken();
        NetHelper.Sign(seedKey, token, connection);
        return token;
    }

    public AuthenticationToken()
    {
    }

    public static int MaxStringLength => 256;

    #region FieldAndProperty

    [Key(0)]
    public SignaturePublicKey PublicKey { get; set; }

    [Key(1, Level = TinyhandWriter.DefaultSignatureLevel + 1)]
    public byte[] Signature { get; set; } = Array.Empty<byte>();

    [Key(2)]
    public long SignedMics { get; set; }

    [Key(3)]
    public ulong Salt { get; set; }

    #endregion

    public static bool TryParse(ReadOnlySpan<char> source, [MaybeNullWhen(false)] out AuthenticationToken instance, out int read)
        => TokenHelper.TryParse(Identifier, source, out instance, out read);

    public bool Validate()
    {
        if (this.SignedMics == 0)
        {
            return false;
        }

        return true;
    }

    public bool Equals(AuthenticationToken? other)
    {
        if (other == null)
        {
            return false;
        }

        return this.PublicKey.Equals(other.PublicKey) &&
            this.Signature.SequenceEqual(other.Signature) &&
            this.SignedMics == other.SignedMics &&
            this.Salt == other.Salt;
    }

    public override string ToString()
        => TokenHelper.ToBase64(this, Identifier);

    public int GetStringLength()
        => -1;

    public bool TryFormat(Span<char> destination, out int written)
        => TokenHelper.TryFormat(this, Identifier, destination, out written);
}

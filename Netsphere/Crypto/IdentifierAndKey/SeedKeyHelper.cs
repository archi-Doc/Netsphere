// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Netsphere.Crypto;

#pragma warning disable SA1204
#pragma warning disable SA1401

public static class SeedKeyHelper
{
    public const int SeedSize = 32;
    public const int PublicKeySize = 32;
    public const int SignatureSize = CryptoSign.SignatureSize;
    public const int ChecksumSize = 4;
    public const int SeedAndChecksumSize = SeedSize + ChecksumSize;
    public const int PublicKeyAndChecksumSize = PublicKeySize + ChecksumSize;

    public const char PublicKeyOpenBracket = '(';
    public const char PublicKeySeparator = ':';
    public const char PublicKeySeparator2 = '!';
    public const char PublicKeyCloseBracket = ')';
    public const char Separator1 = '#';
    public const char Separator2 = '/';
    public const char Separator3 = '+';
    public const char InvalidKey = '*'; // '0'

    public static ReadOnlySpan<char> PrivateKeyBracket => "!!!";

    public static ReadOnlySpan<char> InvalidPublicKeySpan => "(*)"; // (0)

    public static readonly int SeedLengthInBase64; // !!!seed and checksum!!!
    public static readonly int RawPublicKeyLengthInBase64; // key and checksum
    public static readonly int PublicKeyLengthInBase64; // (s:key and checksum)
    public static readonly int PublicKeyLengthInBase64B; // (key and checksum)
    public static readonly int MaxPrivateKeyLengthInBase64; // !!!seed and checksum!!!(s:key and checksum)

    static SeedKeyHelper()
    {
        SeedLengthInBase64 = Base64.Url.GetEncodedLength(SeedSize + ChecksumSize) + 6; // "!!!!!!"
        RawPublicKeyLengthInBase64 = Base64.Url.GetEncodedLength(PublicKeySize + ChecksumSize); // "key"
        PublicKeyLengthInBase64 = RawPublicKeyLengthInBase64 + 4; // "(s:key)"
        PublicKeyLengthInBase64B = RawPublicKeyLengthInBase64 + 2; // "(key)"
        MaxPrivateKeyLengthInBase64 = SeedLengthInBase64 + PublicKeyLengthInBase64; // !!!seed!!!(s:key)

        Debug.Assert(RawPublicKeyLengthInBase64 > Alias.MaxAliasLength);
    }

    public static int CalculateStringLength(ReadOnlySpan<char> source)
    {// identifier, key, (s:key), (key)
        if (source.Length >= 3)
        {
            if (source[0] == PublicKeyOpenBracket &&
                (source[2] == PublicKeySeparator || source[2] == PublicKeySeparator2))
            {// (s:key) (s!key)
                if (source.Length >= PublicKeyLengthInBase64 &&
                    source[PublicKeyLengthInBase64 - 1] == PublicKeyCloseBracket)
                {
                    return PublicKeyLengthInBase64;
                }
            }
            else if (source[0] == PublicKeyOpenBracket &&
                source[2] != PublicKeySeparator &&
                source[2] != PublicKeySeparator2)
            {// (key)
                if (source.Length >= PublicKeyLengthInBase64B &&
                    source[PublicKeyLengthInBase64B - 1] == PublicKeyCloseBracket)
                {
                    return PublicKeyLengthInBase64B;
                }
            }
        }

        var n = source.IndexOfAny(Separator1, Separator2, Separator3);
        return n == -1 ? source.Length : n;
    }

    public static bool TryParsePublicKey(KeyOrientation orientation, ReadOnlySpan<char> source, Span<byte> keyAndChecksum, out int read)
    {// key, (s:key), (key)
        read = 0;
        if (keyAndChecksum.Length != PublicKeyAndChecksumSize)
        {
            BaseHelper.ThrowSizeMismatchException(nameof(keyAndChecksum), PublicKeySize);
        }

        read = CalculateStringLength(source);
        if (read == PublicKeyLengthInBase64)
        {// (s:key)
            if (IdentifierToOrientation(source[1]) != orientation)
            {
                return false;
            }

            if (Base64.Url.FromStringToSpan(source.Slice(3, RawPublicKeyLengthInBase64), keyAndChecksum, out _) &&
                ValidateChecksum(keyAndChecksum))
            {
                return true;
            }
        }
        else if (read == PublicKeyLengthInBase64B)
        {// (key)
            if (Base64.Url.FromStringToSpan(source.Slice(1, RawPublicKeyLengthInBase64), keyAndChecksum, out _) &&
                ValidateChecksum(keyAndChecksum))
            {
                return true;
            }
        }
        else if (read == RawPublicKeyLengthInBase64)
        {// key
            if (Base64.Url.FromStringToSpan(source.Slice(0, RawPublicKeyLengthInBase64), keyAndChecksum, out _) &&
                ValidateChecksum(keyAndChecksum))
            {
                return true;
            }
        }
        else if ((read == 3 && source.Slice(0, 3).SequenceEqual(InvalidPublicKeySpan)) ||
            (read == 1 && source[0] == InvalidKey))
        {
            keyAndChecksum.Clear();
            return true;
        }

        return false;
    }

    public static KeyOrientation IdentifierToOrientation(char identifier)
        => identifier switch
        {
            EncryptionPublicKey.Identifier => KeyOrientation.Encryption,
            SignaturePublicKey.Identifier => KeyOrientation.Signature,
            _ => KeyOrientation.NotSpecified,
        };

    public static char OrientationToIdentifier(KeyOrientation keyOrientation)
        => keyOrientation switch
        {
            KeyOrientation.Encryption => EncryptionPublicKey.Identifier,
            KeyOrientation.Signature => SignaturePublicKey.Identifier,
            _ => (char)0,
        };

    internal static void SetChecksum(Span<byte> span)
    {
        if (span.Length < ChecksumSize)
        {
            throw new ArgumentOutOfRangeException();
        }

        var checksum = (uint)XxHash3.Hash64(span.Slice(0, span.Length - ChecksumSize));
        MemoryMarshal.Write(span.Slice(span.Length - ChecksumSize), checksum);
    }

    internal static bool ValidateChecksum(Span<byte> span)
    {
        if (span.Length < ChecksumSize)
        {
            throw new ArgumentOutOfRangeException();
        }

        var checksum = MemoryMarshal.Read<uint>(span.Slice(span.Length - ChecksumSize));
        return checksum == (uint)XxHash3.Hash64(span.Slice(0, span.Length - ChecksumSize));
    }

    [SkipLocalsInit]
    internal static bool TryFormatPublicKeyWithoutBracket(ReadOnlySpan<byte> publicKey, Span<char> destination, out int written)
    {// key
        if (destination.Length < RawPublicKeyLengthInBase64)
        {
            written = 0;
            return false;
        }

        if (MemoryMarshal.Read<ulong>(publicKey) == 0)
        {
            destination[0] = InvalidKey;
            written = 1;
            return true;
        }

        Span<byte> span = stackalloc byte[PublicKeySize + ChecksumSize];
        publicKey.CopyTo(span);
        SetChecksum(span);
        return Base64.Url.FromByteArrayToSpan(span, destination, out written);
    }

    [SkipLocalsInit]
    internal static bool TryFormatPublicKey(char identifier, ReadOnlySpan<byte> publicKey, Span<char> destination, out int written)
    {// (s:key)
        if (destination.Length < PublicKeyLengthInBase64)
        {
            written = 0;
            return false;
        }

        if (MemoryMarshal.Read<ulong>(publicKey) == 0)
        {
            InvalidPublicKeySpan.CopyTo(destination);
            written = InvalidPublicKeySpan.Length;
            return true;
        }

        var b = destination;
        b[0] = PublicKeyOpenBracket;
        b[1] = identifier;
        b[2] = PublicKeySeparator2; // PublicKeySeparator
        b = b.Slice(3);

        Span<byte> span = stackalloc byte[SeedKeyHelper.PublicKeySize + SeedKeyHelper.ChecksumSize];
        publicKey.CopyTo(span);
        SetChecksum(span);
        Base64.Url.FromByteArrayToSpan(span, b, out written);
        b = b.Slice(written);

        b[0] = SeedKeyHelper.PublicKeyCloseBracket;
        written += 4;

        return true;
    }
}

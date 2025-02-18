﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

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
    public const char PublicKeyCloseBracket = ')';

    public static ReadOnlySpan<char> PrivateKeyBracket => "!!!";

    public static readonly int SeedLengthInBase64; // !!!seed and checksum!!!
    public static readonly int RawPublicKeyLengthInBase64; // key and checksum
    public static readonly int PublicKeyLengthInBase64; // (s:key and checksum)
    public static readonly int MaxPrivateKeyLengthInBase64; // !!!seed and checksum!!!(s:key and checksum)

    static SeedKeyHelper()
    {
        SeedLengthInBase64 = Base64.Url.GetEncodedLength(SeedSize + ChecksumSize) + 6; // "!!!!!!"
        RawPublicKeyLengthInBase64 = Base64.Url.GetEncodedLength(PublicKeySize + ChecksumSize); // "key"
        PublicKeyLengthInBase64 = RawPublicKeyLengthInBase64 + 4; // "(s:key)"
        MaxPrivateKeyLengthInBase64 = SeedLengthInBase64 + PublicKeyLengthInBase64; // !!!seed!!!(s:key)
    }

    public static bool TryParsePublicKey(KeyOrientation orientation, ReadOnlySpan<char> source, Span<byte> keyAndChecksum, out int read)
    {// key, (s:key), (key)
        read = 0;
        if (keyAndChecksum.Length != PublicKeyAndChecksumSize)
        {
            BaseHelper.ThrowSizeMismatchException(nameof(keyAndChecksum), PublicKeySize);
        }

        source = source.Trim();
        if (source.Length < RawPublicKeyLengthInBase64)
        {
            return false;
        }

        if (source[0] != PublicKeyOpenBracket)
        {// key
            if (source.Length < RawPublicKeyLengthInBase64)
            {
                return false;
            }

            if (Base64.Url.FromStringToSpan(source.Slice(0, RawPublicKeyLengthInBase64), keyAndChecksum, out _) &&
                ValidateChecksum(keyAndChecksum))
            {
                read = RawPublicKeyLengthInBase64;
                return true;
            }
            else
            {
                return false;
            }
        }

        if (source[2] == PublicKeySeparator)
        {// (s:key)
            if (IdentifierToOrientation(source[1]) != orientation)
            {
                return false;
            }

            if (source.Length < PublicKeyLengthInBase64 ||
                source[PublicKeyLengthInBase64 - 1] != PublicKeyCloseBracket)
            {
                return false;
            }

            if (Base64.Url.FromStringToSpan(source.Slice(3, RawPublicKeyLengthInBase64), keyAndChecksum, out _) &&
                ValidateChecksum(keyAndChecksum))
            {
                read = PublicKeyLengthInBase64;
                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {// (key)
            if (source.Length < (RawPublicKeyLengthInBase64 + 2) ||
                source[RawPublicKeyLengthInBase64 + 1] != PublicKeyCloseBracket)
            {
                return false;
            }

            if (Base64.Url.FromStringToSpan(source.Slice(1, RawPublicKeyLengthInBase64), keyAndChecksum, out _) &&
                ValidateChecksum(keyAndChecksum))
            {
                read = RawPublicKeyLengthInBase64 + 2;
                return true;
            }
            else
            {
                return false;
            }
        }
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
    internal static bool TryFormatPublicKey(ReadOnlySpan<byte> publicKey, Span<char> destination, out int written)
    {
        if (destination.Length < RawPublicKeyLengthInBase64)
        {
            written = 0;
            return false;
        }

        Span<byte> span = stackalloc byte[PublicKeySize + ChecksumSize];
        publicKey.CopyTo(span);
        SetChecksum(span);
        return Base64.Url.FromByteArrayToSpan(span, destination, out written);
    }

    [SkipLocalsInit]
    internal static bool TryFormatPublicKeyWithBracket(char identifier, ReadOnlySpan<byte> publicKey, Span<char> destination, out int written)
    {// (s:key)
        if (destination.Length < PublicKeyLengthInBase64)
        {
            written = 0;
            return false;
        }

        var b = destination;
        b[0] = PublicKeyOpenBracket;
        b[1] = identifier;
        b[2] = PublicKeySeparator;
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

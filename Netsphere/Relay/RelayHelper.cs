// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Netsphere.Relay;

public static class RelayHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CreateNonce(uint salt4, ulong salt8, ulong secret, Span<byte> nonce)
    {
        Debug.Assert(nonce.Length == 32);

        MemoryMarshal.Write(nonce, salt4);
        nonce = nonce.Slice(sizeof(uint));
        MemoryMarshal.Write(nonce, salt8);
        nonce = nonce.Slice(sizeof(ulong));
        MemoryMarshal.Write(nonce, secret);
        nonce = nonce.Slice(sizeof(ulong));
        MemoryMarshal.Write(nonce, salt4);
        nonce = nonce.Slice(sizeof(uint));
        MemoryMarshal.Write(nonce, salt8);
    }

    public static bool TryDecrypt(byte[] keyAndNonce, scoped ref BytePool.RentMemory source, out Span<byte> span)
    {// source=Relay source id(2), destination id(2), salt(4), Data, Tag(16)
        if (keyAndNonce.Length != (Aegis128L.KeySize + Aegis128L.NonceSize) ||
            source.Length < RelayHeader.RelayIdLength + sizeof(uint) + Aegis128L.MinTagSize)
        {
            span = default;
            return false;
        }

        var sourceSpan = source.Span;
        var key16 = keyAndNonce.AsSpan(0, Aegis128L.KeySize);
        Span<byte> nonce16 = stackalloc byte[Aegis128L.NonceSize];
        keyAndNonce.AsSpan(Aegis128L.KeySize, Aegis128L.NonceSize).CopyTo(nonce16);
        MemoryMarshal.AsRef<uint>(nonce16) ^= MemoryMarshal.Read<uint>(sourceSpan.Slice(RelayHeader.RelayIdLength));

        sourceSpan = sourceSpan.Slice(RelayHeader.RelayIdLength + sizeof(uint));
        if (Aegis128L.TryDecrypt(sourceSpan.Slice(0, sourceSpan.Length - Aegis128L.MinTagSize), sourceSpan, nonce16, key16))
        {
            // Console.WriteLine($"Aegis128L Decrypt {sourceSpan.Length}, Nonce:{Hex.FromByteArrayToString(nonce16)}, Key:{Hex.FromByteArrayToString(key16)} : Success");
            source = source.Slice(0, source.Length - Aegis128L.MinTagSize);
            span = source.Span.Slice(RelayHeader.RelayIdLength);
            return true;
        }
        else
        {
            // Console.WriteLine($"Aegis128L Decrypt {sourceSpan.Length}, Nonce:{Hex.FromByteArrayToString(nonce16)}, Key:{Hex.FromByteArrayToString(key16)} : Failure");
            span = default;
            return false;
        }
    }

    public static void Encrypt(byte[] keyAndNonce, scoped ref BytePool.RentMemory source)
    {// source=Relay source id(2), destination id(2), salt(4), Data
        if (keyAndNonce.Length != (Aegis128L.KeySize + Aegis128L.NonceSize) ||
            source.Length < RelayHeader.RelayIdLength + sizeof(uint))
        {
            // span = default;
            return;
        }

        var sourceSpan = source.Span;
        var key16 = keyAndNonce.AsSpan(0, Aegis128L.KeySize);
        Span<byte> nonce16 = stackalloc byte[Aegis128L.NonceSize];
        keyAndNonce.AsSpan(Aegis128L.KeySize, Aegis128L.NonceSize).CopyTo(nonce16);
        MemoryMarshal.AsRef<uint>(nonce16) ^= MemoryMarshal.Read<uint>(sourceSpan.Slice(RelayHeader.RelayIdLength));

        if (source.RentArray is not { } rentArray)
        {
            // span = default;
            return;
        }

        source = rentArray.AsMemory(0, source.Length + Aegis128L.MinTagSize);
        sourceSpan = source.Span.Slice(RelayHeader.RelayIdLength + sizeof(uint));
        Aegis128L.Encrypt(sourceSpan, sourceSpan.Slice(0, sourceSpan.Length - Aegis128L.MinTagSize), nonce16, key16);
        // Console.WriteLine($"Aegis128L Encrypt {sourceSpan.Length}, Nonce:{Hex.FromByteArrayToString(nonce16)}, Key:{Hex.FromByteArrayToString(key16)}");
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace Netsphere.Crypto;

public static class Alias
{// Identifier/PublicKey <-> Alias
    public const int MaxAliasLength = 32; //  <= RawPublicKeyLengthInBase64

    private static readonly Lock LockPublicKey = new();
    private static readonly UnorderedMapSlim<SignaturePublicKey, string> PublicKeyToAliasTable = new();
    private static readonly Utf16UnorderedMap<SignaturePublicKey> AliasToPublicKeyTable = new();

    private static readonly Lock LockIdentifier = new();
    private static readonly UnorderedMapSlim<Identifier, string> IdentifierToAliasTable = new();
    private static readonly Utf16UnorderedMap<Identifier> AliasToIdentifierTable = new();

    public static bool IsValid(ReadOnlySpan<char> alias)
    {
        if (alias.Length == 0 ||
            alias.Length > MaxAliasLength)
        {
            return false;
        }

        if (!IsAlphabet(alias[0]))
        {
            return false;
        }

        for (var i = 1; i < alias.Length; i++)
        {
            if (!IsAlphabetOrDigit(alias[i]) && alias[i] != '_')
            {
                return false;
            }
        }

        return true;

        static bool IsAlphabet(char c)
            => (uint)(c - 'A') <= ('Z' - 'A') || (uint)(c - 'a') <= ('z' - 'a');

        static bool IsAlphabetOrDigit(char c)
            => (uint)(c - 'A') <= ('Z' - 'A') || (uint)(c - 'a') <= ('z' - 'a') || (uint)(c - '0') <= ('9' - '0');
    }

    public static void Add(string alias, Identifier identifier)
    {
        if (alias.Length > MaxAliasLength)
        {
            throw new ArgumentOutOfRangeException(nameof(alias), $"Alias length must be less than {MaxAliasLength}.");
        }

        using (LockIdentifier.EnterScope())
        {
            IdentifierToAliasTable.Add(identifier, alias);
            AliasToIdentifierTable.Add(alias, identifier);
        }
    }

    public static void TryAdd(string alias, Identifier identifier)
    {
        if (alias.Length > MaxAliasLength)
        {
            throw new ArgumentOutOfRangeException(nameof(alias), $"Alias length must be less than {MaxAliasLength}.");
        }

        using (LockIdentifier.EnterScope())
        {
            IdentifierToAliasTable.TryAdd(identifier, alias);
            AliasToIdentifierTable.TryAdd(alias, identifier);
        }
    }

    public static void Add(string alias, SignaturePublicKey publicKey)
    {
        if (alias.Length > MaxAliasLength)
        {
            throw new ArgumentOutOfRangeException(nameof(alias), $"Alias length must be less than {MaxAliasLength}.");
        }

        using (LockPublicKey.EnterScope())
        {
            PublicKeyToAliasTable.Add(publicKey, alias);
            AliasToPublicKeyTable.Add(alias, publicKey);
        }
    }

    public static void TryAdd(string alias, SignaturePublicKey publicKey)
    {
        if (alias.Length > MaxAliasLength)
        {
            throw new ArgumentOutOfRangeException(nameof(alias), $"Alias length must be less than {MaxAliasLength}.");
        }

        using (LockPublicKey.EnterScope())
        {
            PublicKeyToAliasTable.TryAdd(publicKey, alias);
            AliasToPublicKeyTable.TryAdd(alias, publicKey);
        }
    }

    public static bool Remove(SignaturePublicKey publicKey)
    {
        using (LockPublicKey.EnterScope())
        {
            if (PublicKeyToAliasTable.TryGetValue(publicKey, out var alias))
            {
                PublicKeyToAliasTable.Remove(publicKey);
                AliasToPublicKeyTable.Remove(alias);
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    public static bool Remove(Identifier identifier)
    {
        using (LockIdentifier.EnterScope())
        {
            if (IdentifierToAliasTable.TryGetValue(identifier, out var alias))
            {
                IdentifierToAliasTable.Remove(identifier);
                AliasToIdentifierTable.Remove(alias);
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    public static void ClearPublicKeyAlias()
    {
        using (LockPublicKey.EnterScope())
        {
            PublicKeyToAliasTable.Clear();
            AliasToPublicKeyTable.Clear();
        }
    }

    public static void ClearIdentifierAlias()
    {
        using (LockIdentifier.EnterScope())
        {
            IdentifierToAliasTable.Clear();
            AliasToIdentifierTable.Clear();
        }
    }

    public static bool TryGetAliasFromPublicKey(SignaturePublicKey publicKey, [MaybeNullWhen(false)] out string alias)
    {
        using (LockPublicKey.EnterScope())
        {
            return PublicKeyToAliasTable.TryGetValue(publicKey, out alias);
        }
    }

    public static bool TryGetPublicKeyFromAlias(ReadOnlySpan<char> alias, out SignaturePublicKey publicKey)
    {
        using (LockPublicKey.EnterScope())
        {
            return AliasToPublicKeyTable.TryGetValue(alias, out publicKey);
        }
    }

    public static bool TryGetAliasFromIdentifier(Identifier identifier, [MaybeNullWhen(false)] out string alias)
    {
        using (LockIdentifier.EnterScope())
        {
            return IdentifierToAliasTable.TryGetValue(identifier, out alias);
        }
    }

    public static bool TryGetIdentifierFromAlias(ReadOnlySpan<char> alias, out Identifier identifier)
    {
        using (LockIdentifier.EnterScope())
        {
            return AliasToIdentifierTable.TryGetValue(alias, out identifier);
        }
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace Netsphere.Crypto;

public static class Alias
{// Identifier/PublicKey <-> Alias
    public const int MaxAliasLength = 32; //  <= RawPublicKeyLengthInBase64
    // private static readonly Lock LockPublicKey = new();
    private static readonly Dictionary<SignaturePublicKey, string> PublicKeyToAliasTable;
    private static readonly Dictionary<string, SignaturePublicKey> AliasToPublicKeyTable;
    private static readonly Dictionary<string, SignaturePublicKey>.AlternateLookup<Utf16StringEqualityComparer> AliasToPublicKeyLookup;

    // private static readonly Lock LockIdentifier = new();
    private static readonly Dictionary<Identifier, string> IdentifierToAliasTable = new();
    private static readonly Dictionary<string, Identifier> AliasToIdentifierTable = new();

    static Alias()
    {
        PublicKeyToAliasTable = new();
        AliasToPublicKeyTable = new();
        AliasToPublicKeyLookup = AliasToPublicKeyTable.GetAlternateLookup<Utf16StringEqualityComparer>();
    }

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

    public static void Set(SignaturePublicKey publicKey, string alias)
    {
        if (alias.Length > MaxAliasLength)
        {
            throw new ArgumentOutOfRangeException(nameof(alias), $"Alias length must be less than {MaxAliasLength}.");
        }

        // using (LockPublicKey.EnterScope())
        PublicKeyToAliasTable[publicKey] = alias; // PublicKeyToAliasTable.Add(publicKey, alias);
        AliasToPublicKeyTable[alias] = publicKey; // AliasToPublicKeyTable.Add(alias, publicKey);
    }

    public static bool Remove(SignaturePublicKey publicKey)
    {
        // using (LockPublicKey.EnterScope())
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

    public static void ClearPublicKeyAlias()
    {
        // using (LockPublicKey.EnterScope())
        PublicKeyToAliasTable.Clear();
        AliasToPublicKeyTable.Clear();
    }

    public static void ClearIdentifierAlias()
    {
        // using (LockIdentifier.EnterScope())
        IdentifierToAliasTable.Clear();
        AliasToIdentifierTable.Clear();
    }

    public static void Add(Identifier identifier, string alias)
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

    public static bool TryGetAliasFromPublicKey(SignaturePublicKey publicKey, [MaybeNullWhen(false)] out string alias)
        => PublicKeyToAliasTable.TryGetValue(publicKey, out alias);

    public static bool TryGetPublicKeyFromAlias(ReadOnlySpan<char> alias, out SignaturePublicKey publicKey)
        => AliasToPublicKeyTable.TryGetValue(alias, out publicKey);

    public static bool TryGetAliasFromIdentifier(Identifier identifier, [MaybeNullWhen(false)] out string alias)
        => IdentifierToAliasTable.TryGetValue(identifier, out alias);

    public static bool TryGetIdentifierFromAlias(ReadOnlySpan<char> alias, out Identifier identifier)
        => AliasToIdentifierTable.TryGetValue(alias, out identifier);
}

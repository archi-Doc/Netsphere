// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace Netsphere.Crypto;

public class Alias
{// Identifier/PublicKey <-> Alias
    public const int MaxAliasLength = 32; //  <= RawPublicKeyLengthInBase64

    private readonly Lock lockPublicKey = new();
    private readonly UnorderedMapSlim<SignaturePublicKey, string> publicKeyToAliasMap = new();
    private readonly Utf16UnorderedMap<SignaturePublicKey> aliasToPublicKeyMap = new();

    private readonly Lock lockIdentifier = new();
    private readonly UnorderedMapSlim<Identifier, string> identifierToAliasMap = new();
    private readonly Utf16UnorderedMap<Identifier> aliasToIdentifierMap = new();

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

    public void Add(string alias, Identifier identifier)
    {
        if (alias.Length > MaxAliasLength)
        {
            throw new ArgumentOutOfRangeException(nameof(alias), $"Alias length must be less than {MaxAliasLength}.");
        }

        using (this.lockIdentifier.EnterScope())
        {
            this.identifierToAliasMap.Add(identifier, alias);
            this.aliasToIdentifierMap.Add(alias, identifier);
        }
    }

    public void TryAdd(string alias, Identifier identifier)
    {
        if (alias.Length > MaxAliasLength)
        {
            throw new ArgumentOutOfRangeException(nameof(alias), $"Alias length must be less than {MaxAliasLength}.");
        }

        using (this.lockIdentifier.EnterScope())
        {
            this.identifierToAliasMap.TryAdd(identifier, alias);
            this.aliasToIdentifierMap.TryAdd(alias, identifier);
        }
    }

    public void Add(string alias, SignaturePublicKey publicKey)
    {
        if (alias.Length > MaxAliasLength)
        {
            throw new ArgumentOutOfRangeException(nameof(alias), $"Alias length must be less than {MaxAliasLength}.");
        }

        using (this.lockPublicKey.EnterScope())
        {
            this.publicKeyToAliasMap.Add(publicKey, alias);
            this.aliasToPublicKeyMap.Add(alias, publicKey);
        }
    }

    public void TryAdd(string alias, SignaturePublicKey publicKey)
    {
        if (alias.Length > MaxAliasLength)
        {
            throw new ArgumentOutOfRangeException(nameof(alias), $"Alias length must be less than {MaxAliasLength}.");
        }

        using (this.lockPublicKey.EnterScope())
        {
            this.publicKeyToAliasMap.TryAdd(publicKey, alias);
            this.aliasToPublicKeyMap.TryAdd(alias, publicKey);
        }
    }

    public bool Remove(SignaturePublicKey publicKey)
    {
        using (this.lockPublicKey.EnterScope())
        {
            if (this.publicKeyToAliasMap.TryGetValue(publicKey, out var alias))
            {
                this.publicKeyToAliasMap.Remove(publicKey);
                this.aliasToPublicKeyMap.Remove(alias);
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    public bool Remove(Identifier identifier)
    {
        using (this.lockIdentifier.EnterScope())
        {
            if (this.identifierToAliasMap.TryGetValue(identifier, out var alias))
            {
                this.identifierToAliasMap.Remove(identifier);
                this.aliasToIdentifierMap.Remove(alias);
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    public void ClearPublicKeyAlias()
    {
        using (this.lockPublicKey.EnterScope())
        {
            this.publicKeyToAliasMap.Clear();
            this.aliasToPublicKeyMap.Clear();
        }
    }

    public void ClearIdentifierAlias()
    {
        using (this.lockIdentifier.EnterScope())
        {
            this.identifierToAliasMap.Clear();
            this.aliasToIdentifierMap.Clear();
        }
    }

    public bool TryGetAliasFromPublicKey(SignaturePublicKey publicKey, [MaybeNullWhen(false)] out string alias)
    {
        using (this.lockPublicKey.EnterScope())
        {
            return this.publicKeyToAliasMap.TryGetValue(publicKey, out alias);
        }
    }

    public bool TryGetPublicKeyFromAlias(ReadOnlySpan<char> alias, out SignaturePublicKey publicKey)
    {
        using (this.lockPublicKey.EnterScope())
        {
            return this.aliasToPublicKeyMap.TryGetValue(alias, out publicKey);
        }
    }

    public bool TryGetAliasFromIdentifier(Identifier identifier, [MaybeNullWhen(false)] out string alias)
    {
        using (this.lockIdentifier.EnterScope())
        {
            return this.identifierToAliasMap.TryGetValue(identifier, out alias);
        }
    }

    public bool TryGetIdentifierFromAlias(ReadOnlySpan<char> alias, out Identifier identifier)
    {
        using (this.lockIdentifier.EnterScope())
        {
            return this.aliasToIdentifierMap.TryGetValue(alias, out identifier);
        }
    }
}

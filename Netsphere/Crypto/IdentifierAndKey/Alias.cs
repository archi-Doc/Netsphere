// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace Netsphere.Crypto;

/// <summary>
/// Maps identifiers and signature public keys to short text aliases.
/// </summary>
public class Alias : IConversionOptions
{// Identifier/PublicKey <-> Alias
    public const int MaxAliasLength = 16; // Must be set to RawPublicKeyLengthInBase64 or less.

    public static Alias Instance { get; } = new();

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
        {// Remove stale entries in both directions so that the maps remain one-to-one.
            if (this.identifierToAliasMap.Remove(identifier, out var previousAlias))
            {
                this.aliasToIdentifierMap.Remove(previousAlias);
            }

            if (this.aliasToIdentifierMap.TryGetValue(alias, out var previousIdentifier))
            {
                this.identifierToAliasMap.Remove(previousIdentifier);
            }

            this.identifierToAliasMap.AddOrUpdate(identifier, alias);
            this.aliasToIdentifierMap.AddOrUpdate(alias, identifier);
        }
    }

    public void TryAdd(string alias, Identifier identifier)
    {
        if (alias.Length > MaxAliasLength)
        {
            throw new ArgumentOutOfRangeException(nameof(alias), $"Alias length must be less than {MaxAliasLength}.");
        }

        using (this.lockIdentifier.EnterScope())
        {// Add only when neither side is mapped; adding one direction alone would break the correspondence.
            if (!this.identifierToAliasMap.ContainsKey(identifier) &&
                !this.aliasToIdentifierMap.ContainsKey(alias))
            {
                this.identifierToAliasMap.TryAdd(identifier, alias);
                this.aliasToIdentifierMap.TryAdd(alias, identifier);
            }
        }
    }

    public void Add(string alias, SignaturePublicKey publicKey)
    {
        if (alias.Length > MaxAliasLength)
        {
            throw new ArgumentOutOfRangeException(nameof(alias), $"Alias length must be less than {MaxAliasLength}.");
        }

        using (this.lockPublicKey.EnterScope())
        {// Remove stale entries in both directions so that the maps remain one-to-one.
            if (this.publicKeyToAliasMap.Remove(publicKey, out var previousAlias))
            {
                this.aliasToPublicKeyMap.Remove(previousAlias);
            }

            if (this.aliasToPublicKeyMap.TryGetValue(alias, out var previousPublicKey))
            {
                this.publicKeyToAliasMap.Remove(previousPublicKey);
            }

            this.publicKeyToAliasMap.AddOrUpdate(publicKey, alias);
            this.aliasToPublicKeyMap.AddOrUpdate(alias, publicKey);
        }
    }

    public void TryAdd(string alias, SignaturePublicKey publicKey)
    {
        if (alias.Length > MaxAliasLength)
        {
            throw new ArgumentOutOfRangeException(nameof(alias), $"Alias length must be less than {MaxAliasLength}.");
        }

        using (this.lockPublicKey.EnterScope())
        {// Add only when neither side is mapped; adding one direction alone would break the correspondence.
            if (!this.publicKeyToAliasMap.ContainsKey(publicKey) &&
                !this.aliasToPublicKeyMap.ContainsKey(alias))
            {
                this.publicKeyToAliasMap.TryAdd(publicKey, alias);
                this.aliasToPublicKeyMap.TryAdd(alias, publicKey);
            }
        }
    }

    public bool Remove(SignaturePublicKey publicKey)
    {
        using (this.lockPublicKey.EnterScope())
        {
            if (this.publicKeyToAliasMap.Remove(publicKey, out var alias))
            {
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
            if (this.identifierToAliasMap.Remove(identifier, out var alias))
            {
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

    T? IConversionOptions.GetOption<T>()
        where T : class
    {
        if (typeof(T) == typeof(Alias))
        {
            return this as T;
        }

        return default;
    }
}

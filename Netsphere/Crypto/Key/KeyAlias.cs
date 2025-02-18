// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Netsphere.Crypto;

public static class KeyAlias
{// PublicKey <-> Alias
    private static readonly Lock LockSignaturePublicKey = new();
    private static readonly NotThreadsafeHashtable<SignaturePublicKey, string> SignaturePublicKeyToAlias = new();
    private static readonly NotThreadsafeHashtable<string, SignaturePublicKey> AliasToSignaturePublicKey = new();

    public static void AddAlias(SignaturePublicKey publicKey, string alias)
    {
        using (LockSignaturePublicKey.EnterScope())
        {
            SignaturePublicKeyToAlias.Add(publicKey, alias);
            AliasToSignaturePublicKey.Add(alias, publicKey);
        }
    }

    public static bool TryGetAlias(SignaturePublicKey publicKey, [MaybeNullWhen(false)] out string alias)
        => SignaturePublicKeyToAlias.TryGetValue(publicKey, out alias);

    public static bool TryGetAlias(string alias, out SignaturePublicKey publicKey)
        => AliasToSignaturePublicKey.TryGetValue(alias, out publicKey);
}

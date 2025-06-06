﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Security.Cryptography.X509Certificates;
using Netsphere;
using Netsphere.Crypto;
using Xunit;

namespace xUnitTest;

public class KeyTest
{
    public const string AliasName = "test";
    public const string AliasName2 = "test2";

    [Fact]
    public void Test1()
    {
        var seedKey = SeedKey.New(KeyOrientation.Signature);
        var st = seedKey.UnsafeToString();
        SeedKey.TryParse(st, out var seedKey2);
        seedKey.Equals(seedKey2).IsTrue();

        seedKey = SeedKey.New(KeyOrientation.Encryption);
        st = seedKey.UnsafeToString();
        SeedKey.TryParse(st, out seedKey2);
        seedKey.Equals(seedKey2).IsTrue();

        SeedKey.TryParse("!!!FoZqwj1Bvy5dRNMLbtgLQDzdc3wOd2Sw75qm7ifev8vsY4JL!!!(s:cDlMibfEAW29DgjeRzxx7eqOw5KayiVVQEXlcryiTrI28xnW)", out seedKey).IsTrue();
        SeedKey.TryParse("!!!FoZqwj1Bvy5dRNMLbtgLQDzdc3wOd2Sw75qm7ifev8vsY4JL!!!(cDlMibfEAW29DgjeRzxx7eqOw5KayiVVQEXlcryiTrI28xnW)", out seedKey).IsFalse();
        SeedKey.TryParse("!!!FoZqwj1Bvy5dRNMLbtgLQDzdc3wOd2Sw75qm7ifev8vsY4JL!!!(e:cDlMibfEAW29DgjeRzxx7eqOw5KayiVVQEXlcryiTrI28xnW)", out seedKey).IsFalse();
        SeedKey.TryParse("!!!FoZqwj1Bvy5dRNMLbtgLQDzdc3wOd2Sw75qm7ifev8vsY4JL!!!", out seedKey).IsTrue();
    }

    [Fact]
    public void TestSignaturePublicKey()
    {
        var seedKey = SeedKey.NewSignature();
        var publicKey = seedKey.GetSignaturePublicKey();

        var st = publicKey.ToString(); // (s:key)
        SignaturePublicKey.TryParse(st, out var publicKey2, out int read).IsTrue();
        read.Is(st.Length);
        publicKey.Equals(publicKey2).IsTrue();

        st = publicKey.ToString().Substring(3, read - 4); // key
        SignaturePublicKey.TryParse(st, out publicKey2, out read).IsTrue();
        read.Is(st.Length);
        publicKey.Equals(publicKey2).IsTrue();

        st = $"({st})"; // (key)
        SignaturePublicKey.TryParse(st, out publicKey2, out read).IsTrue();
        read.Is(st.Length);
        publicKey.Equals(publicKey2).IsTrue();

        Alias.Instance.Add(AliasName, publicKey);
        Alias.Instance.TryGetPublicKeyFromAlias(AliasName, out publicKey2).IsTrue();
        publicKey.Equals(publicKey2).IsTrue();

        SignaturePublicKey.TryParse(AliasName2, out publicKey2, out read, Alias.Instance).IsFalse();
        SignaturePublicKey.TryParse(AliasName, out publicKey2, out read, Alias.Instance).IsTrue();
        publicKey2.ToString(Alias.Instance).Is(AliasName);
        publicKey.Equals(publicKey2).IsTrue();

        st = SeedKeyHelper.InvalidPublicKeySpan.ToString(); // (0)
        SignaturePublicKey.TryParse(st, out publicKey2, out read).IsTrue();
        read.Is(st.Length);
        publicKey2.IsValid.IsFalse();
        publicKey2.ToString().SequenceEqual(st).IsTrue();

        st = $"{SeedKeyHelper.InvalidKey}"; // 0
        SignaturePublicKey.TryParse(st, out publicKey2, out read).IsTrue();
        read.Is(st.Length);
        publicKey2.IsValid.IsFalse();
        // publicKey2.ToString().SequenceEqual(st).IsTrue();
    }

    [Fact]
    public void TestIdentifier()
    {
        var publicKey = SeedKey.NewSignature().GetSignaturePublicKey();
        var identifier = publicKey.GetIdentifier();

        var st = identifier.ToString(); // key
        Identifier.TryParse(st, out var identifier2, out int read).IsTrue();
        read.Is(st.Length);
        identifier.Equals(identifier2).IsTrue();

        st = $"{st}/{st}";
        Identifier.TryParse(st, out identifier2, out read).IsTrue();
        identifier.Equals(identifier2).IsTrue();

        Alias.Instance.Add(AliasName, identifier);
        Alias.Instance.TryGetIdentifierFromAlias(AliasName, out identifier2).IsTrue();
        identifier.Equals(identifier2).IsTrue();

        Identifier.TryParse(AliasName2, out identifier2, out read, Alias.Instance).IsFalse();
        Identifier.TryParse(AliasName, out identifier2, out read, Alias.Instance).IsTrue();
        identifier2.ToString(Alias.Instance).Is(AliasName);
        identifier.Equals(identifier2).IsTrue();

        st = $"{SeedKeyHelper.InvalidKey}"; // 0
        Identifier.TryParse(st, out identifier2, out read).IsTrue();
        read.Is(st.Length);
        identifier2.IsValid.IsFalse();
        identifier2.ToString().SequenceEqual(st).IsTrue();
    }
}

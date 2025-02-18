// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Tinyhand.IO;

namespace Netsphere.Crypto;

/*[TinyhandObject]
public readonly partial record struct Signature
{
    public enum Type
    {
        Invalid,
        Attest,
    }

    public Signature(SignaturePublicKey2 publicKey, Type signatureType, long signedMics)
    {
        this.PublicKey = publicKey;
        this.SignatureType = signatureType;
        this.SignedMics = signedMics;
        this.Sign = null;
    }

    public Signature(SignaturePublicKey2 publicKey, Type signatureType, long signedMics, byte[] sign)
    {
        this.PublicKey = publicKey;
        this.SignatureType = signatureType;
        this.SignedMics = signedMics;

        if (sign.Length == KeyHelper.PublicKeyLength)
        {
            this.Sign = sign;
        }
        else
        {
            this.Sign = null;
        }
    }

    #region FieldAndProperty

    [Key(0)]
    public readonly SignaturePublicKey2 PublicKey;

    [Key(1)]
    public readonly Type SignatureType;

    [Key(2)]
    public readonly long SignedMics;

    [Key(3)]
    public readonly long ExpirationMics;

    [Key(4, Level = TinyhandWriter.DefaultSignatureLevel + 1)]
    [DefaultValue(null)]
    public readonly byte[]? Sign;

    #endregion
}*/

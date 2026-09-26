// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Netsphere.Crypto;

#pragma warning disable SA1202 // Elements should be ordered by access
#pragma warning disable SA1204
#pragma warning disable SA1401

/// <summary>
/// Stores secret seed material and derives encryption or signing keys.
/// </summary>
[TinyhandObject]
public sealed partial class SeedKey : IEquatable<SeedKey>, IStringConvertible<SeedKey>, IDisposable
{// !!!Base64Url(Seed+Checksum)!!!(s:Base64Url(PublicKey+Checksum))
    public static int MaxStringLength => SeedKeyHelper.MaxPrivateKeyLengthInBase64;

    public static SeedKey Invalid { get; } = new();

    public int GetStringLength() => this.KeyOrientation switch
    {
        KeyOrientation.Encryption => SeedKeyHelper.MaxPrivateKeyLengthInBase64,
        KeyOrientation.Signature => SeedKeyHelper.MaxPrivateKeyLengthInBase64,
        _ => SeedKeyHelper.SeedLengthInBase64,
    };

    public bool TryFormat(Span<char> destination, out int written, IConversionOptions? conversionOptions = default)
        => this.UnsafeTryFormat(destination, out written);

    public static bool TryParse(ReadOnlySpan<char> base64url, [MaybeNullWhen(false)] out SeedKey secretKey)
        => TryParse(base64url, out secretKey, out _);

    public static bool TryParse(ReadOnlySpan<char> base64url, [MaybeNullWhen(false)] out SeedKey secretKey, out int read, IConversionOptions? conversionOptions = default)
    {// !!!seed!!!, !!!seed!!!(s:key)
        Span<byte> seed = stackalloc byte[SeedKeyHelper.SeedSize];
        if (TryParseSeed(base64url, seed, out var keyOrientation, out read))
        {
            secretKey = new(seed, keyOrientation);
            seed.Clear();
            return true;
        }
        else
        {
            secretKey = default;
            read = 0;
            return false;
        }
    }

    public static SeedKey NewEncryption()
        => New(KeyOrientation.Encryption);

    public static SeedKey NewEncryption(ReadOnlySpan<byte> seed)
        => New(seed, KeyOrientation.Encryption);

    public static SeedKey NewSignature()
        => New(KeyOrientation.Signature);

    public static SeedKey NewSignature(ReadOnlySpan<byte> seed)
        => New(seed, KeyOrientation.Signature);

    public static SeedKey New(KeyOrientation keyOrientation)
    {
        Span<byte> seed = stackalloc byte[SeedKeyHelper.SeedSize];
        CryptoRandom.NextBytes(seed);
        return new(seed, keyOrientation);
    }

    public static SeedKey New(ReadOnlySpan<byte> seed, KeyOrientation keyOrientation)
    {
        if (seed.Length != SeedKeyHelper.SeedSize)
        {
            BaseHelper.ThrowSizeMismatchException(nameof(seed), SeedKeyHelper.SeedSize);
        }

        return new(seed, keyOrientation);
    }

    public static SeedKey New(SeedKey baseSeedKey, ReadOnlySpan<byte> additional)
    {
        if (!baseSeedKey.IsValid)
        {// An empty or cleared seed would derive a key that anyone can compute.
            throw new ArgumentException("The base seed key is not valid.", nameof(baseSeedKey));
        }

        Span<byte> hash = stackalloc byte[SeedKeyHelper.SeedSize];
        using var hasher = Blake3Hasher.New();
        hasher.Update(baseSeedKey.seed);
        hasher.Update(additional);
        hasher.FinalizeHash(hash);

        return new(hash, baseSeedKey.KeyOrientation);
    }

    private SeedKey()
    {
    }

    private SeedKey(ReadOnlySpan<byte> seed, KeyOrientation keyOrientation)
    {
        this.seed = seed.ToArray();
        this.KeyOrientation = keyOrientation;
    }

    private static bool TryParseSeed(ReadOnlySpan<char> base64url, Span<byte> seed, out KeyOrientation keyOrientation, out int read)
    {// !!!seed!!!, !!!seed!!!(s:key)
        keyOrientation = KeyOrientation.NotSpecified;
        read = 0;
        var span = base64url.Trim();
        if (!span.StartsWith(SeedKeyHelper.PrivateKeyBracket))
        {// Invalid
            return false;
        }

        var initialLength = span.Length;
        span = span.Slice(SeedKeyHelper.PrivateKeyBracket.Length);
        var bracketPosition = span.IndexOf(SeedKeyHelper.PrivateKeyBracket);
        if (bracketPosition <= 0)
        {// Invalid
            return false;
        }

        // Only the exact encoded length can hold a seed and checksum. Checking it first also avoids
        // GetDecodedLength, which throws for invalid lengths.
        var span2 = span.Slice(0, bracketPosition);
        if (span2.Length != SeedKeyHelper.SeedLengthInBase64 - (SeedKeyHelper.PrivateKeyBracket.Length * 2))
        {
            return false;
        }

        Span<byte> seedSpan = stackalloc byte[SeedKeyHelper.SeedAndChecksumSize];
        if (!FastBase64Url.TryDecode(span2, seedSpan, out var decodedLength) ||
            decodedLength != SeedKeyHelper.SeedAndChecksumSize ||
            !SeedKeyHelper.ValidateChecksum(seedSpan))
        {
            CryptographicOperations.ZeroMemory(seedSpan);
            return false;
        }

        seedSpan.Slice(0, SeedKeyHelper.SeedSize).CopyTo(seed);
        CryptographicOperations.ZeroMemory(seedSpan);

        span = span.Slice(bracketPosition + SeedKeyHelper.PrivateKeyBracket.Length);
        if (span.Length == 0 || span[0] != SeedKeyHelper.PublicKeyOpenBracket)
        {
            read = initialLength - span.Length;
            return true;
        }

        // (i:key)
        if (span.Length < 4)
        {
            seed.Clear();
            return false;
        }

        keyOrientation = SeedKeyHelper.IdentifierToOrientation(span[1]);
        if (keyOrientation == KeyOrientation.NotSpecified)
        {// (key)
            seed.Clear();
            return false;
        }

        Span<byte> keyAndChecksum = stackalloc byte[SeedKeyHelper.PublicKeyAndChecksumSize];
        if (!SeedKeyHelper.TryParsePublicKey(keyOrientation, span, keyAndChecksum, out var parsedLength))
        {
            seed.Clear();
            return false;
        }

        var key = keyAndChecksum.Slice(0, SeedKeyHelper.PublicKeySize);
        if (keyOrientation == KeyOrientation.Encryption)
        {
            Span<byte> encryptionSecretKey = stackalloc byte[CryptoBox.SecretKeySize];
            Span<byte> encryptionPublicKey = stackalloc byte[CryptoBox.PublicKeySize];
            CryptoBox.CreateKeyPair(seed, encryptionSecretKey, encryptionPublicKey);
            CryptographicOperations.ZeroMemory(encryptionSecretKey);
            if (CryptoDual.BoxPublicKeyEquals(key, encryptionPublicKey))
            {
                read = initialLength - span.Length + parsedLength;
                return true;
            }
        }
        else if (keyOrientation == KeyOrientation.Signature)
        {
            Span<byte> signatureSecretKey = stackalloc byte[CryptoSign.SecretKeySize];
            Span<byte> signaturePublicKey = stackalloc byte[CryptoSign.PublicKeySize];
            CryptoSign.CreateKeyPair(seed, signatureSecretKey, signaturePublicKey);
            CryptographicOperations.ZeroMemory(signatureSecretKey);
            if (key.SequenceEqual(signaturePublicKey))
            {
                read = initialLength - span.Length + parsedLength;
                return true;
            }
        }

        seed.Clear();
        return false;
    }

    #region FieldAndProperty

    [Key(0)]
    private byte[] seed = Array.Empty<byte>();

    [Key(1)]
    public KeyOrientation KeyOrientation { get; private set; } = KeyOrientation.NotSpecified;

    /// <summary>
    /// Gets a value indicating whether this instance holds a seed; it becomes <see langword="false"/> after <see cref="Clear"/>.
    /// </summary>
    public bool IsValid => this.seed.Length == SeedKeyHelper.SeedSize;

    private Lock lockObject = new();
    private byte[]? encryptionSecretKey; // X25519 32bytes
    private byte[]? encryptionPublicKey; // X25519 32bytes
    private byte[]? signatureSecretKey; // Ed235519 64bytes
    private byte[]? signaturePublicKey; // Ed235519 32bytes

    #endregion

    [MemberNotNull(nameof(encryptionSecretKey), nameof(encryptionPublicKey), nameof(signatureSecretKey), nameof(signaturePublicKey))]
    private void PrepareKey()
    {
        if (this.encryptionSecretKey is not null &&
            this.encryptionPublicKey is not null &&
            this.signatureSecretKey is not null &&
            this.signaturePublicKey is not null)
        {
            return;
        }

        using (this.lockObject.EnterScope())
        {
            if (this.encryptionSecretKey is not null &&
            this.encryptionPublicKey is not null &&
            this.signatureSecretKey is not null &&
            this.signaturePublicKey is not null)
            {
                return;
            }

            if (!this.IsValid)
            {// Deriving from an empty or cleared seed would produce a publicly computable key pair.
                throw new InvalidOperationException("The seed key is not valid or has been cleared.");
            }

            var signSecret = new byte[CryptoSign.SecretKeySize];
            var signPublic = new byte[CryptoSign.PublicKeySize];
            var boxSecret = new byte[CryptoBox.SecretKeySize];
            var boxPublic = new byte[CryptoBox.PublicKeySize];
            CryptoDual.CreateKeyPair(this.seed, signSecret, signPublic, boxSecret, boxPublic);

            this.signatureSecretKey = signSecret;
            this.signaturePublicKey = signPublic;
            this.encryptionSecretKey = boxSecret;
            this.encryptionPublicKey = boxPublic;
        }
    }

    public EncryptionPublicKey GetEncryptionPublicKey()
    {
        this.PrepareKey();
        return new(this.encryptionPublicKey);
    }

    public SignaturePublicKey GetSignaturePublicKey()
    {
        this.PrepareKey();
        return new(this.signaturePublicKey);
    }

    public ReadOnlySpan<byte> GetEncryptionPublicKeySpan()
    {
        this.PrepareKey();
        return this.encryptionPublicKey.AsSpan();
    }

    public ReadOnlySpan<byte> GetSignaturePublicKeySpan()
    {
        this.PrepareKey();
        return this.signaturePublicKey.AsSpan();
    }

    public bool TryEncrypt(ReadOnlySpan<byte> message, ReadOnlySpan<byte> nonce24, ReadOnlySpan<byte> publicKey32, Span<byte> cipher)
    {
        if (nonce24.Length != CryptoBox.NonceSize)
        {
            return false;
        }

        if (publicKey32.Length != CryptoBox.PublicKeySize)
        {
            return false;
        }

        if (cipher.Length != message.Length + CryptoBox.MacSize)
        {
            return false;
        }

        this.PrepareKey();
        CryptoBox.Encrypt(message, nonce24, this.encryptionSecretKey, publicKey32, cipher);
        return true;
    }

    public bool TryDecrypt(ReadOnlySpan<byte> cipher, ReadOnlySpan<byte> nonce24, ReadOnlySpan<byte> publicKey32, Span<byte> data)
    {
        if (nonce24.Length != CryptoBox.NonceSize)
        {
            return false;
        }

        if (publicKey32.Length != CryptoBox.PublicKeySize)
        {
            return false;
        }

        if (data.Length != cipher.Length - CryptoBox.MacSize)
        {
            return false;
        }

        this.PrepareKey();
        return CryptoBox.TryDecrypt(cipher, nonce24, this.encryptionSecretKey, publicKey32, data);
    }

    public void Sign(ReadOnlySpan<byte> message, Span<byte> signature)
    {
        if (signature.Length != CryptoSign.SignatureSize)
        {
            BaseHelper.ThrowSizeMismatchException(nameof(signature), CryptoSign.SignatureSize);
        }

        this.PrepareKey();
        CryptoSign.Sign(message, this.signatureSecretKey, signature);
    }

    public void DeriveKeyMaterial(EncryptionPublicKey publicKey, Span<byte> keyMaterial)
    {
        if (keyMaterial.Length != CryptoBox.SharedSecretSize)
        {
            BaseHelper.ThrowSizeMismatchException(nameof(keyMaterial), CryptoBox.SharedSecretSize);
        }

        this.PrepareKey();
        CryptoBox.DeriveSharedSecret(this.encryptionSecretKey, publicKey.AsSpan(), keyMaterial);
    }

    /// <summary>
    /// Derives shared key material, returning <see langword="false"/> instead of throwing when the peer's public key is not usable for key agreement (for example, a low-order point).
    /// </summary>
    /// <param name="publicKey">The peer's encryption public key.</param>
    /// <param name="keyMaterial">The destination; its length must be <see cref="CryptoBox.SharedSecretSize"/>. It is cleared on failure.</param>
    /// <returns><see langword="true"/> if the key material was derived.</returns>
    public bool TryDeriveKeyMaterial(EncryptionPublicKey publicKey, Span<byte> keyMaterial)
    {
        if (keyMaterial.Length != CryptoBox.SharedSecretSize)
        {
            return false;
        }

        this.PrepareKey();
        try
        {
            CryptoBox.DeriveSharedSecret(this.encryptionSecretKey, publicKey.AsSpan(), keyMaterial);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public bool Equals(SeedKey? other)
        => other is not null && CryptographicOperations.FixedTimeEquals(this.seed, other.seed);

    public override int GetHashCode()
        => (int)XxHash3.Hash64(this.seed); // Does not expose seed bytes, and accepts an empty seed.

    public override string ToString()
        => $"SeedKey";

    public string UnsafeToString()
    {
        Span<char> span = stackalloc char[this.GetStringLength()];
        return this.UnsafeTryFormat(span, out var written) ? span.Slice(0, written).ToString() : string.Empty;
    }

    private bool UnsafeTryFormat(Span<char> destination, out int written)
    {// !!!seed!!!, !!!seed!!!(s:key)
        if (!this.IsValid ||
            destination.Length < SeedKeyHelper.SeedLengthInBase64)
        {
            written = 0;
            return false;
        }

        Span<byte> seedSpan = stackalloc byte[SeedKeyHelper.SeedSize + SeedKeyHelper.ChecksumSize];
        this.seed.CopyTo(seedSpan);
        SeedKeyHelper.SetChecksum(seedSpan);

        Span<char> span = destination;
        SeedKeyHelper.PrivateKeyBracket.CopyTo(span);
        span = span.Slice(SeedKeyHelper.PrivateKeyBracket.Length);

        // Base64.Url.FromByteArrayToSpan(seedSpan, span, out var w);
        var w = FastBase64Url.Encode(seedSpan, span);
        CryptographicOperations.ZeroMemory(seedSpan);
        span = span.Slice(w);

        SeedKeyHelper.PrivateKeyBracket.CopyTo(span);
        span = span.Slice(SeedKeyHelper.PrivateKeyBracket.Length);

        written = SeedKeyHelper.SeedLengthInBase64;
        if (span.Length >= SeedKeyHelper.PublicKeyLengthInBase64)
        {
            if (this.KeyOrientation == KeyOrientation.Encryption)
            {
                var publicKey = this.GetEncryptionPublicKey();
                if (publicKey.TryFormat(span, out w))
                {
                    written += w;
                }
            }
            else if (this.KeyOrientation == KeyOrientation.Signature)
            {
                var publicKey = this.GetSignaturePublicKey();
                if (publicKey.TryFormat(span, out w))
                {
                    written += w;
                }
            }
        }

        return true;
    }

    public void Clear()
    {// Detach the arrays so that later use fails instead of deriving or reusing all-zero keys.
        using (this.lockObject.EnterScope())
        {
            CryptographicOperations.ZeroMemory(this.seed);
            this.seed = Array.Empty<byte>();
            this.KeyOrientation = KeyOrientation.NotSpecified;
            ClearKey(ref this.encryptionSecretKey);
            ClearKey(ref this.encryptionPublicKey);
            ClearKey(ref this.signatureSecretKey);
            ClearKey(ref this.signaturePublicKey);
        }

        static void ClearKey(ref byte[]? key)
        {
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
                key = null;
            }
        }
    }

    /// <summary>
    /// Clears the secret seed and cached key material.
    /// </summary>
    public void Dispose()
    {
        this.Clear();
    }
}

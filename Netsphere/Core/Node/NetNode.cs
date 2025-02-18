// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Netsphere.Crypto;

namespace Netsphere;

/// <summary>
/// Represents ipv4/ipv6 node information.<br/>
/// <see cref="NetNode"/> = <see cref="NetAddress"/> + <see cref="EncryptionPublicKey"/>.
/// </summary>
[TinyhandObject(ReservedKeyCount = 2)]
public partial class NetNode : IStringConvertible<NetNode>, IValidatable, IEquatable<NetNode>
{
    public NetNode()
    {
    }

    public NetNode(NetAddress address, EncryptionPublicKey publicKey)
    {
        this.Address = address;
        this.PublicKey = publicKey;
    }

    public NetNode(in NetEndpoint endPoint, EncryptionPublicKey publicKey)
    {
        this.Address = new(endPoint);
        this.PublicKey = publicKey;
    }

    public NetNode(NetNode netNode)
    {
        this.Address = netNode.Address;
        this.PublicKey = netNode.PublicKey;
    }

    [Key(0)]
    public NetAddress Address { get; protected set; }

    [Key(1)]
    public EncryptionPublicKey PublicKey { get; protected set; }

    public bool IsValid
        => this.Address.IsValid && this.PublicKey.IsValid;

    public static bool TryParseNetNode(ILogger? logger, string source, [MaybeNullWhen(false)] out NetNode node)
    {
        node = default;
        if (string.Compare(source, "alt", true) == 0)
        {
            node = Alternative.NetNode;
            return true;
        }
        else
        {
            if (!NetNode.TryParse(source, out var address, out _))
            {
                logger?.TryGet(LogLevel.Error)?.Log($"Could not parse: {source.ToString()}");
                return false;
            }

            if (!address.Address.Validate())
            {
                logger?.TryGet(LogLevel.Error)?.Log($"Invalid port: {source.ToString()}");
                return false;
            }

            node = address;
            return true;
        }
    }

    public static bool TryParse(ReadOnlySpan<char> source, [MaybeNullWhen(false)] out NetNode instance, out int read)
    {// Ip address (public key)
        source = source.Trim();
        instance = default;
        read = 0;

        var index = source.IndexOf('(');
        if (index < 0)
        {
            return false;
        }

        var index2 = source.IndexOf(')');
        if (index2 < 0)
        {
            return false;
        }

        var sourceAddress = source.Slice(0, index);
        var sourcePublicKey = source.Slice(index, index2 - index + 1);

        if (!NetAddress.TryParse(sourceAddress, out var address, out _))
        {
            return false;
        }

        if (!EncryptionPublicKey.TryParse(sourcePublicKey, out var publicKey, out _))
        {
            return false;
        }

        instance = new(address, publicKey);
        read = index2 + 1;
        return true;
    }

    public static int MaxStringLength
        => NetAddress.MaxStringLength + SeedKey.MaxStringLength + 2;

    public int GetStringLength()
        => -1;

    public bool TryFormat(Span<char> destination, out int written)
    {
        var span = destination;
        written = 0;
        if (span.Length < MaxStringLength)
        {
            return false;
        }

        if (!this.Address.TryFormat(span, out written))
        {
            return false;
        }
        else
        {
            span = span.Slice(written);
        }

        if (!this.PublicKey.TryFormatWithBracket(span, out written))
        {
            return false;
        }
        else
        {
            span = span.Slice(written);
        }

        written = destination.Length - span.Length;
        return true;
    }

    public bool Validate()
        => this.Address.Validate() && this.PublicKey.Validate();

    public override string ToString()
    {
        Span<char> span = stackalloc char[MaxStringLength];
        return this.TryFormat(span, out var written) ? span.Slice(0, written).ToString() : string.Empty;
    }

    public bool Equals(NetNode? other)
    {
        if (other is null)
        {
            return false;
        }

        return this.Address.Equals(other.Address) &&
            this.PublicKey.Equals(other.PublicKey);
    }

    public override int GetHashCode()
        => HashCode.Combine(this.Address, this.PublicKey);
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;

namespace Netsphere;

/// <summary>
/// Represents ipv4/ipv6 address information.
/// </summary>
[TinyhandObject]
public readonly partial record struct NetAddress : IStringConvertible<NetAddress>, IValidatable // , IEquatable<NetAddress>
{// 24 bytes. IEquatable<NetAddress> -> record struct
    public const char RelayIdSeparator = '&';
    public static readonly NetAddress Relay = new(0, 0, 0, 1);

    public static bool SkipValidation { get; set; }

    public static bool Validate(IPAddress ipAddress)
    {
        Span<byte> span = stackalloc byte[16];

        if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ipAddress.TryWriteBytes(span, out _))
            {
                var address6A = BitConverter.ToUInt64(span);
                span = span.Slice(sizeof(ulong));
                var address6B = BitConverter.ToUInt64(span);
                return Validate6(address6A, address6B);
            }
        }
        else
        {
            if (ipAddress.TryWriteBytes(span, out _))
            {
                var address4 = BitConverter.ToUInt32(span);
                return Validate4(address4);
            }
        }

        return false;
    }

    [Key(0)]
    public readonly RelayId RelayId; // 2 bytes

    [Key(1)]
    public readonly ushort Port; // 2 bytes

    [Key(2)]
    public readonly uint Address4; // 4 bytes

    [Key(3)]
    public readonly ulong Address6A; // 8 bytes

    [Key(4)]
    public readonly ulong Address6B; // 8 bytes

    public NetAddress(RelayId relayId, uint address4, ulong address6a, ulong address6b, ushort port)
    {
        this.RelayId = relayId;
        this.Port = port;
        this.Address4 = address4;
        this.Address6A = address6a;
        this.Address6B = address6b;
    }

    public NetAddress(uint address4, ulong address6a, ulong address6b, ushort port)
    {
        this.Port = port;
        this.Address4 = address4;
        this.Address6A = address6a;
        this.Address6B = address6b;
    }

    public NetAddress(NetAddress original, ushort port)
    {
        this.RelayId = original.RelayId;
        this.Port = port;
        this.Address4 = original.Address4;
        this.Address6A = original.Address6A;
        this.Address6B = original.Address6B;
    }

    public NetAddress(RelayId relayId, NetAddress original)
    {
        this.RelayId = relayId;
        this.Port = original.Port;
        this.Address4 = original.Address4;
        this.Address6A = original.Address6A;
        this.Address6B = original.Address6B;
    }

    public NetAddress(NetEndpoint endpoint)
    {
        if (endpoint.EndPoint is not null)
        {
            Span<byte> span = stackalloc byte[16];

            this.RelayId = endpoint.RelayId;
            this.Port = (ushort)endpoint.EndPoint.Port;
            if (endpoint.EndPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (endpoint.EndPoint.Address.TryWriteBytes(span, out _))
                {
                    this.Address6A = BitConverter.ToUInt64(span);
                    span = span.Slice(sizeof(ulong));
                    this.Address6B = BitConverter.ToUInt64(span);
                }
            }
            else
            {
                if (endpoint.EndPoint.Address.TryWriteBytes(span, out _))
                {
                    this.Address4 = BitConverter.ToUInt32(span);
                }
            }
        }
    }

    public NetAddress(IPAddress ipAddress, ushort port)
    {
        Span<byte> span = stackalloc byte[16];

        this.Port = port;
        if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ipAddress.TryWriteBytes(span, out _))
            {
                this.Address6A = BitConverter.ToUInt64(span);
                span = span.Slice(sizeof(ulong));
                this.Address6B = BitConverter.ToUInt64(span);
            }
        }
        else
        {
            if (ipAddress.TryWriteBytes(span, out _))
            {
                this.Address4 = BitConverter.ToUInt32(span);
            }
        }
    }

    public NetAddress(IPAddress? addressIpv4, IPAddress? addressIpv6, ushort port)
    {
        Span<byte> span = stackalloc byte[16];

        if (addressIpv4 is not null &&
            addressIpv4.TryWriteBytes(span, out _))
        {
            this.Address4 = BitConverter.ToUInt32(span);
            this.Port = port;
        }

        if (addressIpv6 is not null &&
            addressIpv6.TryWriteBytes(span, out _))
        {
            this.Address6A = BitConverter.ToUInt64(span);
            span = span.Slice(sizeof(ulong));
            this.Address6B = BitConverter.ToUInt64(span);
            this.Port = port;
        }
    }

    public static bool TryParse(ReadOnlySpan<char> source, [MaybeNullWhen(false)] out NetAddress instance)
        => TryParse(source, out instance, out _);

    public static bool TryParse(ReadOnlySpan<char> source, [MaybeNullWhen(false)] out NetAddress instance, out int read)
    {// 1.2.3.4:55, []:55, 1.2.3.4:55[]:55
        ushort port = 0;
        uint address4 = 0;
        ulong address6a;
        ulong address6b;

        instance = default;
        read = 0;

        source = source.Trim();
        if (source.Length == 0)
        {
            return false;
        }

        var initialLength = source.Length;
        TryParseRelayId(ref source, out var relayId);

        if (IsIpv4Address(source))
        {// TryParse IPv4
            TryParseIPv4(ref source, ref port, out address4);
        }

        // TryParse IPv6
        TryParseIPv6(ref source, ref port, out address6a, out address6b);

        instance = new(relayId, address4, address6a, address6b, port);
        read = initialLength - source.Length;
        return true;
    }

    public static bool TryParse(ILogger? logger, string source, [MaybeNullWhen(false)] out NetAddress address)
    {
        address = default;
        // read = 0;
        if (string.Compare(source, Alternative.Name, true) == 0)
        {
            address = Alternative.NetAddress;
            // read = Alternative.Name.Length;
            return true;
        }
        else
        {
            if (!NetAddress.TryParse(source, out address, out _))
            {
                logger?.TryGet(LogLevel.Error)?.Log($"Could not parse: {source.ToString()}");
                return false;
            }

            if (!address.Validate())
            {
                logger?.TryGet(LogLevel.Error)?.Log($"Invalid node address: {source.ToString()}");
                return false;
            }

            return true;
        }
    }

    public bool IsValidIpv4 => this.Port != 0 && this.Address4 != 0;

    public bool IsValidIpv6 => this.Port != 0 && (this.Address6A != 0 || this.Address6B != 0);

    public bool IsValidIpv4AndIpv6 => this.Port != 0 && this.Address4 != 0 && (this.Address6A != 0 || this.Address6B != 0);

    public bool IsValid => this.Port != 0 && (this.Address4 != 0 || this.Address6A != 0 || this.Address6B != 0);

    public static int MaxStringLength
        => (5 + 1) + (15 + 1 + 5) + (2 + 54 + 1 + 5); // RelayId:12345& IPv4:12345, [IPv6]:12345

    public int GetStringLength()
        => -1;

    public bool TryFormat(Span<char> destination, out int written)
    {// 15 + 1 + 5, 54 + 1 + 5 + 2
        if (destination.Length < MaxStringLength)
        {
            written = 0;
            return false;
        }

        if (!this.IsValid)
        {
            written = 0;
            return true;
        }

        var span = destination;
        if (this.RelayId != 0)
        {
            this.RelayId.TryFormat(span, out written);
            span = span.Slice(written);
            span[0] = RelayIdSeparator;
            span = span.Slice(1);
        }

        if (this.IsValidIpv4)
        {
            Span<byte> ipv4byte = stackalloc byte[4];
            BitConverter.TryWriteBytes(ipv4byte, this.Address4);
            var ipv4 = new IPAddress(ipv4byte);
            if (!ipv4.TryFormat(span, out written))
            {
                return false;
            }

            span = span.Slice(written);

            span[0] = ':';
            span = span.Slice(1);
            this.Port.TryFormat(span, out written);
            span = span.Slice(written);
        }

        if (this.IsValidIpv6)
        {
            span[0] = '[';
            span = span.Slice(1);

            Span<byte> ipv6byte = stackalloc byte[16];
            BitConverter.TryWriteBytes(ipv6byte, this.Address6A);
            BitConverter.TryWriteBytes(ipv6byte.Slice(sizeof(ulong)), this.Address6B);
            var ipv6 = new IPAddress(ipv6byte);
            if (!ipv6.TryFormat(span, out written))
            {
                return false;
            }

            span = span.Slice(written);

            span[0] = ']';
            span[1] = ':';
            span = span.Slice(2);

            this.Port.TryFormat(span, out written);
            span = span.Slice(written);
        }

        written = destination.Length - span.Length;
        return true;
    }

    public bool Equals(IPEndPoint? endpoint)
    {
        if (endpoint is null)
        {
            return false;
        }

        Span<byte> span = stackalloc byte[16];
        if (endpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            if (this.IsValidIpv4)
            {
                endpoint.Address.TryWriteBytes(span, out _);
                return this.Port == endpoint.Port && this.Address4 == BitConverter.ToUInt32(span);
            }
        }
        else if (endpoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (this.IsValidIpv6)
            {
                endpoint.Address.TryWriteBytes(span, out _);
                return this.Port == endpoint.Port && this.Address6A == BitConverter.ToUInt64(span) && this.Address6B == BitConverter.ToUInt64(span.Slice(sizeof(ulong)));
            }
        }

        return false;
    }

    public bool IsPrivateOrLocalLoopbackAddress()
    {
        return (this.IsValidIpv4 && this.IsPrivateOrLocalLoopbackAddressIPv4()) ||
            (this.IsValidIpv6 && this.IsPrivateOrLocalLoopbackAddressIPv6());
    }

    /*[MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IPEndPoint? TryCreateIpv4()
    {
        if (this.IsValidIpv4)
        {
            return new(this.Address4, this.Port);
        }
        else
        {
            return default;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IPEndPoint? TryCreateIpv6()
    {
        if (this.IsValidIpv6)
        {
            Span<byte> ipv6byte = stackalloc byte[16];
            BitConverter.TryWriteBytes(ipv6byte, this.Address6A);
            BitConverter.TryWriteBytes(ipv6byte.Slice(sizeof(ulong)), this.Address6B);
            var ipv6 = new IPAddress(ipv6byte);
            return new(ipv6, this.Port);
        }
        else
        {
            return default;
        }
    }*/

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryCreateIpv4(ref NetEndpoint endPoint)
    {
        if (!this.IsValidIpv4)
        {
            return false;
        }

        endPoint = new(this.RelayId, new(this.Address4, this.Port));
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryCreateIpv6(ref NetEndpoint endPoint)
    {
        if (!this.IsValidIpv6)
        {
            return false;
        }

        Span<byte> ipv6byte = stackalloc byte[16];
        BitConverter.TryWriteBytes(ipv6byte, this.Address6A);
        BitConverter.TryWriteBytes(ipv6byte.Slice(sizeof(ulong)), this.Address6B);
        var ipv6 = new IPAddress(ipv6byte);
        endPoint = new(this.RelayId, new(ipv6, this.Port));
        return true;
    }

    /*[MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CreateIPEndPoint(out IPEndPoint endPoint)
    {
        if (this.IsValidIpv4)
        {
            endPoint = new(this.Address4, this.Port);
        }
        else if (this.IsValidIpv6)
        {
            Span<byte> ipv6byte = stackalloc byte[16];
            BitConverter.TryWriteBytes(ipv6byte, this.Address6A);
            BitConverter.TryWriteBytes(ipv6byte.Slice(sizeof(ulong)), this.Address6B);
            var ipv6 = new IPAddress(ipv6byte);
            endPoint = new(this.Address4, this.Port);
        }
        else
        {
            endPoint = new(0, 0);
        }
    }*/

    public bool Validate()
    {
        var ipv4 = this.IsValidIpv4;
        var ipv6 = this.IsValidIpv6;
        if (!ipv4 && !ipv6)
        {
            return false;
        }

        if (SkipValidation)
        {
            return true;
        }

        /* Port number is not checked because it may be changed by NAT
        if (this.Port < NetConstants.MinPort || this.Port > NetConstants.MaxPort)
        {
            return false;
        }*/

        if (ipv4)
        {
            if (!Validate4(this.Address4))
            {
                return false;
            }
        }

        if (ipv6)
        {
            if (!Validate6(this.Address6A, this.Address6B))
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString()
    {
        Span<char> span = stackalloc char[MaxStringLength];
        return this.TryFormat(span, out var written) ? span.Slice(0, written).ToString() : string.Empty;
    }

    /*public bool Equals(NetAddress other)
        => this.RelayId == other.RelayId &&
        this.Port == other.Port &&
        this.Address4 == other.Address4 &&
        this.Address6A == other.Address6A &&
        this.Address6B == other.Address6B;

    public override int GetHashCode()
        => HashCode.Combine(this.RelayId, this.Port, this.Address4, this.Address6A, this.Address6B);*/

    private static bool TryParseRelayId(ref ReadOnlySpan<char> source, out RelayId relayId)
    {// 123:1.2.3.4:456, 1.2.3.4:456, []
        relayId = 0;
        if (source.Length == 0)
        {
            return false;
        }

        var i = 0;
        for (; i < source.Length; i++)
        {
            var c = source[i];
            if (c >= '0' && c <= '9')
            {
                continue;
            }
            else if (c == '.' || c == ':')
            {// IPv4 or IPv6
                return false;
            }
            else
            {
                break;
            }
        }

        if (i == 0)
        {
            return false;
        }
        else if (i >= source.Length)
        {
            i = source.Length - 1;
        }

        var result = RelayId.TryParse(source.Slice(0, i), out relayId);
        source = source.Slice(i + 1);
        return result;
    }

    private static bool TryParseIPv4(ref ReadOnlySpan<char> source, ref ushort port, out uint address4)
    {
        address4 = 0;

        var index = source.IndexOf(':');
        if (index < 0)
        {
            return false;
        }

        var sourceAddress = source.Slice(0, index); // "1.2.3.4"
        ReadOnlySpan<char> sourcePort;
        source = source.Slice(index + 1); // :"xxxx"
        index = source.IndexOf('[');
        if (index < 0)
        {// Only IPv4
            sourcePort = source;
            source = ReadOnlySpan<char>.Empty;
        }
        else
        {
            sourcePort = source.Slice(0, index); // "xxx"[
            source = source.Slice(index); // "[xxxx]"
        }

        if (!IPAddress.TryParse(sourceAddress, out var ipAddress) ||
            ipAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        Span<byte> span = stackalloc byte[4];
        if (!ipAddress.TryWriteBytes(span, out _))
        {
            return false;
        }

        address4 = BitConverter.ToUInt32(span);

        int digitCount;
        for (digitCount = 0; digitCount < sourcePort.Length; digitCount++)
        {
            if (sourcePort[digitCount] >= '0' && sourcePort[digitCount] <= '9')
            {
                continue;
            }
            else
            {
                break;
            }
        }

        if (digitCount == 0)
        {
            return false;
        }
        else
        {
            sourcePort = sourcePort.Slice(0, digitCount);
        }

        if (!ushort.TryParse(sourcePort, out var p))
        {
            return false;
        }

        port = p;
        return true;
    }

    private static bool TryParseIPv6(ref ReadOnlySpan<char> source, ref ushort port, out ulong address6a, out ulong address6b)
    {// [ipv6 address]:port
        address6a = 0;
        address6b = 0;

        if (source.Length == 0)
        {
            return false;
        }

        ReadOnlySpan<char> sourceAddress;
        ReadOnlySpan<char> sourcePort;
        int index;
        if (source[0] == '[')
        {// [123::1]:Port
            index = source.IndexOf(']');
            if (index < 0)
            {
                return false;
            }

            sourceAddress = source.Slice(1, index - 1); // "123::1"
            source = source.Slice(index + 1);
            index = source.IndexOf(':');
            if (index < 0)
            {
                return false;
            }

            sourcePort = source.Slice(index + 1); // :"xxxx"
        }
        else
        {// 123::1:Port
            index = source.LastIndexOf(':');
            if (index < 0)
            {
                return false;
            }

            sourceAddress = source.Slice(0, index);
            sourcePort = source.Slice(index + 1);
        }

        source = ReadOnlySpan<char>.Empty;

        if (!IPAddress.TryParse(sourceAddress, out var ipAddress) ||
            ipAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return false;
        }

        Span<byte> span = stackalloc byte[16];
        if (!ipAddress.TryWriteBytes(span, out _))
        {
            return false;
        }

        address6a = BitConverter.ToUInt64(span);
        span = span.Slice(sizeof(ulong));
        address6b = BitConverter.ToUInt64(span);

        int digitCount;
        for (digitCount = 0; digitCount < sourcePort.Length;)
        {
            if (sourcePort[digitCount] >= '0' && sourcePort[digitCount] <= '9')
            {
                digitCount++;
            }
            else
            {
                break;
            }
        }

        if (digitCount == 0)
        {
            return false;
        }
        else
        {
            sourcePort = sourcePort.Slice(0, digitCount);
        }

        if (!ushort.TryParse(sourcePort, out var p))
        {
            return false;
        }

        port = p;
        return true;
    }

    private static bool IsIpv4Address(ReadOnlySpan<char> source)
    {
        if (source.Length == 0)
        {
            return false;
        }
        else if (source[0] == '[')
        {// IPv6
            return false;
        }

        foreach (var x in source)
        {
            if (x == '.')
            {// IPv4
                return true;
            }
            else if (x == ':')
            {// IPv6
                return false;
            }
        }

        return false; // Unknown
    }

    private static bool Validate4(uint address4)
    {
        Span<byte> address = stackalloc byte[4];
        BitConverter.TryWriteBytes(address, address4);

        if (address[0] == 0 || address[0] == 10 || address[0] == 127)
        {// Current network, Private network, loopback addresses.
            return false;
        }
        else if (address[0] == 100)
        {
            if (address[1] >= 64 && address[1] <= 127)
            {// Private network
                return false;
            }
        }
        else if (address[0] == 169 && address[1] == 254)
        {// Link-local addresses.
            return false;
        }
        else if (address[0] == 172)
        {// Private network
            if (address[1] >= 16 && address[1] <= 31)
            {
                return false;
            }
        }
        else if (address[0] == 192)
        {
            if (address[1] == 0)
            {
                if (address[2] == 0 || address[2] == 2)
                {
                    return false;
                }
            }
            else if (address[1] == 88 && address[2] == 99)
            {
                return false;
            }
            else if (address[1] == 168)
            {// Private network
                return false;
            }
        }
        else if (address[0] == 198)
        {
            if (address[1] == 18 || address[1] == 19)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Validate6(ulong address6A, ulong address6B)
    {
        if (address6A == 0 && (address6B == 0 || address6B == 0x0100000000000000))
        {// Unspecified address, Loopback address
            return false;
        }

        Span<byte> b = stackalloc byte[8];
        BitConverter.TryWriteBytes(b, address6A);

        var b0 = (byte)address6A;
        var b1 = (byte)(address6A >> 8);
        if (b0 == 0xFC || b0 == 0xFD)
        {// Unique local address
            return false;
        }
        else if (b0 == 0xFE)
        {
            if (b1 >= 0x80 && b1 <= 0xBF)
            {// Link-local address
                return false;
            }
        }
        else if (b0 == 0x20 && b1 == 0x01)
        {// 2001
            var b2 = (byte)(address6A >> 16);
            var b3 = (byte)(address6A >> 24);
            if (b2 == 0x0D && b3 == 0xB8)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsLocalLoopbackAddressIPv4()
    {
        Span<byte> address = stackalloc byte[4];
        if (!BitConverter.TryWriteBytes(address, this.Address4))
        {
            return false;
        }

        return address[0] == 127 && address[1] == 0 && address[2] == 0;
    }

    private unsafe bool IsLocalLoopbackAddressIPv6()
    {
        return this.Address6A == 0 && this.Address6B == 0x0100000000000000;
    }

    private bool IsPrivateOrLocalLoopbackAddressIPv4()
    {
        Span<byte> address = stackalloc byte[4];
        if (!BitConverter.TryWriteBytes(address, this.Address4))
        {
            return false;
        }

        if (address[0] == 10 || address[0] == 127)
        {// Private network, loopback addresses.
            return true;
        }
        else if (address[0] == 100)
        {
            if (address[1] >= 64 && address[1] <= 127)
            {// Private network
                return true;
            }
        }
        else if (address[0] == 172)
        {// Private network
            if (address[1] >= 16 && address[1] <= 31)
            {
                return true;
            }
        }
        else if (address[0] == 192)
        {
            if (address[1] == 168)
            {// Private network
                return true;
            }
        }

        return false;
    }

    private bool IsPrivateOrLocalLoopbackAddressIPv6()
    {
        if (this.Address6A == 0 && this.Address6B == 0x0100000000000000)
        {// Loopback address
            return true;
        }

        Span<byte> b = stackalloc byte[8];
        BitConverter.TryWriteBytes(b, this.Address6A);
        if (b[0] == 0xFC || b[0] == 0xFD)
        {// Unique local address
            return true;
        }
        else if (b[0] == 0xFE)
        {
            if (b[1] >= 0x80 && b[1] <= 0xBF)
            {// Link-local address
                return true;
            }
        }

        return false;
    }
}

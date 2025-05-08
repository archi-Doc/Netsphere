// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using Arc.Crypto;
using Netsphere;
using Netsphere.Crypto;
using Xunit;

namespace xUnitTest;

public class NodeTest
{
    [Fact]
    public void DualAddress1()
    {
        TestDualAddress("192.168.0.0:49152", false).IsTrue();
        TestDualAddress("192.168.0.1:49152", false).IsTrue();
        // TestDualAddress("0.0.0.0:49152", false, false).IsTrue();
        TestDualAddress("10.1.2.3:49152", false).IsTrue();
        TestDualAddress("100.64.1.2:49152", false).IsTrue();
        TestDualAddress("127.0.0.0:49152", false).IsTrue();
        TestDualAddress("172.30.5.4:49152", false).IsTrue();
        TestDualAddress("192.0.1.1:49152", true).IsTrue();
        TestDualAddress("192.0.0.5:49152", false).IsTrue();
        TestDualAddress("172.217.25.228:49152", true).IsTrue();

        // TestDualAddress("[::]:49152", false, false).IsTrue();
        TestDualAddress("[::1]:49152", false).IsTrue();
        TestDualAddress("[fe80::]:49152", false).IsTrue();
        TestDualAddress("[fe8b::]:49152", false).IsTrue();
        TestDualAddress("[febc:1111::]:49152", false).IsTrue();
        TestDualAddress("[fecd:1111::]:49152", true).IsTrue();
        TestDualAddress("[fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff]:49152", false).IsTrue();
        TestDualAddress("[fc00::]:49152", false).IsTrue();
        TestDualAddress("[2404:6800:4004:80a::2004]:49152", true).IsTrue();

        TestDualAddress("172.217.25.228:49152[2404:6800:4004:80a::2004]:49152", true).IsTrue();
        TestDualAddress("2404:6800:4004:80a::2004:49152", true, false).IsTrue();

        NetAddress address;

        NetAddress.TryParse("127.0.0.1:49152", out address);
        address.Validate().IsFalse();
        address.IsPrivateOrLocalLoopbackAddress().IsTrue();

        NetAddress.TryParse("[::1]:49152", out address);
        address.Validate().IsFalse();
        address.IsPrivateOrLocalLoopbackAddress().IsTrue();

        NetAddress.TryParse("[fc00::]:49152", out address);
        address.Validate().IsFalse();
        address.IsPrivateOrLocalLoopbackAddress().IsTrue();

        NetAddress.TryParse("[fd00:1234::]:49152", out address);
        address.Validate().IsFalse();
        address.IsPrivateOrLocalLoopbackAddress().IsTrue();

        TestDualAddress("1&192.168.0.0:49152", false).IsTrue();
        TestDualAddress("12345&192.168.0.0:49152", false).IsTrue();
        TestDualAddress("123&[2404:6800:4004:80a::2004]:49152", true).IsTrue();
        TestDualAddress("123&172.217.25.228:49152[2404:6800:4004:80a::2004]:49152", true).IsTrue();

        NetAddress.TryParse("222.111.222.111:49152", out address);
        ((int)address.Port).Is(49152);
        NetAddress.TryParse("222.111.222.111:1024", out address);
        ((int)address.Port).Is(1024);
    }

    [Fact]
    public void DualAddressAndPublicKey1()
    {
        TestDualNode("127.0.0.1:49999(e:sSe258iWUhPXCzadvA5xMMCb9czjKgUrPIJWebm-CoEMCb_G)", false).IsTrue();
    }

    [Fact]
    public void DualAddressAndPublicKeyRandom()
    {
        const int N = 10;
        var r = RandomVault.Xoshiro;

        for (var i = 0; i < N; i++)
        {
            GenerateDualNode(r, 0, out var address);
            TestDualNode(address).IsTrue();
        }

        for (var i = 0; i < N; i++)
        {
            GenerateDualNode(r, 1, out var address);
            TestDualNode(address).IsTrue();
        }

        for (var i = 0; i < N; i++)
        {
            GenerateDualNode(r, 2, out var address);
            TestDualNode(address).IsTrue();
        }
    }

    [Fact]
    public void IsValidIPv4()
    {
        this.CreateAddress("192.168.0.0").Validate().IsFalse();
        this.CreateAddress("192.168.0.1").Validate().IsFalse();
        this.CreateAddress("0.0.0.0").Validate().IsFalse();
        this.CreateAddress("10.1.2.3").Validate().IsFalse();
        this.CreateAddress("100.64.1.2").Validate().IsFalse();
        this.CreateAddress("127.0.0.0").Validate().IsFalse();
        this.CreateAddress("172.30.5.4").Validate().IsFalse();
        this.CreateAddress("192.0.1.1").Validate().IsTrue();
        this.CreateAddress("192.0.0.5").Validate().IsFalse();
        this.CreateAddress("172.217.25.228").Validate().IsTrue();
    }

    [Fact]
    public void IsValidIPv6()
    {
        this.CreateAddress("::").Validate().IsFalse();
        this.CreateAddress("::1").Validate().IsFalse();
        this.CreateAddress("fe80::").Validate().IsFalse();
        this.CreateAddress("fe8b::").Validate().IsFalse();
        this.CreateAddress("febc:1111::").Validate().IsFalse();
        this.CreateAddress("fecd:1111::").Validate().IsTrue();
        this.CreateAddress("fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff").Validate().IsFalse();
        this.CreateAddress("fc00::").Validate().IsFalse();
        this.CreateAddress("2404:6800:4004:80a::2004").Validate().IsTrue();
    }

    private static bool TestDualAddress(string utf16, bool validation, bool compareUtf16 = true)
    {
        NetAddress.TryParse(utf16, out var address).IsTrue();

        Span<char> destination = stackalloc char[NetAddress.MaxStringLength];
        address.TryFormat(destination, out var written).IsTrue();
        destination = destination.Slice(0, written);

        if (compareUtf16)
        {
            utf16.Is(destination.ToString());
        }

        NetAddress.TryParse(destination, out var address2).IsTrue();
        address2.Equals(address).IsTrue();

        address.Validate().Is(validation);

        return true;
    }

    private static bool TestDualNode(string utf16, bool validation, bool compareUtf16 = true)
    {
        if (!NetNode.TryParse(utf16, out var node, out _))
        {
            throw new Exception();
        }

        Span<char> destination = stackalloc char[NetNode.MaxStringLength];
        node.TryFormat(destination, out var written).IsTrue();
        destination = destination.Slice(0, written);

        if (compareUtf16)
        {
            utf16.Is(destination.ToString());
        }

        if (!NetNode.TryParse(destination, out var node2, out _))
        {
            throw new Exception();
        }

        node2.Equals(node).IsTrue();

        node.Validate().Is(validation);

        return true;
    }

    private static bool TestDualNode(NetNode addressAndKey)
    {
        Span<char> destination = stackalloc char[NetNode.MaxStringLength];
        addressAndKey.TryFormat(destination, out var written).IsTrue();
        destination = destination.Slice(0, written);

        if (!NetNode.TryParse(destination, out var addressAndKey2, out _))
        {
            throw new Exception();
        }

        addressAndKey2.Equals(addressAndKey).IsTrue();

        // addressAndKey.Validate().Is(true);

        return true;
    }

    private static void GenerateDualNode(RandomVault r, int type, out NetNode addressAndKey)
    {
        var key = SeedKey.NewEncryption();

        var port = (ushort)r.NextInt32(NetConstants.MinPort, NetConstants.MaxPort);
        if (type == 0)
        {// IPv4
            var address4 = r.NextUInt32();
            addressAndKey = new(new(address4, 0, 0, port), key.GetEncryptionPublicKey());
        }
        else if (type == 1)
        {// IPv6
            var address6a = r.NextUInt64();
            var address6b = r.NextUInt64();
            addressAndKey = new(new(0, address6a, address6b, port), key.GetEncryptionPublicKey());
        }
        else
        {
            var address4 = r.NextUInt32();
            var address6a = r.NextUInt64();
            var address6b = r.NextUInt64();
            addressAndKey = new(new(address4, address6a, address6b, port), key.GetEncryptionPublicKey());
        }
    }

    private NetAddress CreateAddress(string address) => new NetAddress(IPAddress.Parse(address), NetConstants.MinPort);
}

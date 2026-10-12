// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using Netsphere;
using Xunit;
using xUnitTest.NetsphereTest;

namespace xUnitTest;

[Collection(NetFixtureCollection.Name)]
public class AddressFamilyAuditTest
{
    [Theory]
    [InlineData("0.0.0.0", false, false)]
    [InlineData("10.1.2.3", false, true)]
    [InlineData("100.64.1.2", false, true)]
    [InlineData("127.0.0.1", false, true)]
    [InlineData("169.254.1.2", false, false)]
    [InlineData("172.16.1.2", false, true)]
    [InlineData("192.168.1.2", false, true)]
    [InlineData("192.0.2.1", false, false)]
    [InlineData("8.8.8.8", true, false)]
    public void MappedIpv4UsesTheSameValidationAndLocalClassification(string text, bool valid, bool local)
    {
        var ipv4 = IPAddress.Parse(text);
        var mapped = ipv4.MapToIPv6();
        var address = new NetAddress(mapped, 12345);
        Assert.Equal(valid, NetAddress.Validate(mapped));
        Assert.Equal(NetAddress.Validate(ipv4), NetAddress.Validate(mapped));
        Assert.Equal(valid, address.Validate());
        Assert.Equal(local, address.IsPrivateOrLocalLoopbackAddress());
        Assert.Equal(local, new NetEndpoint(0, new IPEndPoint(mapped, 12345)).IsPrivateOrLocalLoopbackAddress());
    }

    [Theory]
    [InlineData("::1", null, "addressIpv4")]
    [InlineData(null, "127.0.0.1", "addressIpv6")]
    [InlineData("::1", "127.0.0.1", "addressIpv4")]
    public void DualAddressConstructorRejectsMismatchedFamilies(string? ipv4, string? ipv6, string parameter)
    {
        var exception = Assert.Throws<ArgumentException>(() => new NetAddress(ipv4 is null ? null : IPAddress.Parse(ipv4), ipv6 is null ? null : IPAddress.Parse(ipv6), 12345));
        Assert.Equal(parameter, exception.ParamName);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DualAddressConstructorPreservesEachFamily(bool includeIpv4, bool includeIpv6)
    {
        var ipv4 = includeIpv4 ? IPAddress.Parse("8.8.8.8") : null;
        var ipv6 = includeIpv6 ? IPAddress.Parse("2001:4860:4860::8888") : null;
        var address = new NetAddress(ipv4, ipv6, 12345);
        Assert.Equal(includeIpv4, address.IsValidIpv4);
        Assert.Equal(includeIpv6, address.IsValidIpv6);
        if (ipv4 is not null)
        {
            Assert.True(address.Equals(new IPEndPoint(ipv4, 12345)));
        }

        if (ipv6 is not null)
        {
            Assert.True(address.Equals(new IPEndPoint(ipv6, 12345)));
        }
    }
}

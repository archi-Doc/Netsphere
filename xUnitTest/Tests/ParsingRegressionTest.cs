// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;
using Xunit;

namespace xUnitTest;

public class ParsingRegressionTest
{
    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("123")]
    [InlineData("1.2.3.999:1234")]
    [InlineData("1.2.3.4:")]
    [InlineData("1.2.3.4:65536")]
    [InlineData("1.2.3.4:-1")]
    [InlineData("[::1]")]
    [InlineData("[::1]garbage:1234")]
    [InlineData("[::1]:65536")]
    [InlineData("65536&1.2.3.4:1234")]
    [InlineData("123!1.2.3.4:1234")]
    [InlineData("1.2.3.4:1234[invalid]:1234")]
    [InlineData("1.2.3.4:1234[::1]:4321")]
    public void InvalidAddressesFailWithoutPartialResults(string source)
    {
        Assert.False(NetAddress.TryParse(source, out var address, out var read));
        Assert.Equal(default, address);
        Assert.Equal(0, read);
    }

    [Theory]
    [InlineData("1.2.3.4:1234")]
    [InlineData("[::1]:1234")]
    [InlineData("123&1.2.3.4:1234[::1]:1234")]
    public void AddressParserReportsOnlyConsumedCharacters(string source)
    {
        Assert.True(NetAddress.TryParse(source + "(next)", out var address, out var read));
        Assert.Equal(source.Length, read);
        Assert.Equal(source, address.ToString());
    }

    [Theory]
    [InlineData(")abc(")]
    [InlineData("((")]
    [InlineData("garbage(e!sSe258iWUhPXCzadvA5xMMCb9czjKgUrPIJWebm-CoEMCb_G)")]
    [InlineData("1.2.3.4:1234garbage(e!sSe258iWUhPXCzadvA5xMMCb9czjKgUrPIJWebm-CoEMCb_G)")]
    public void InvalidNodesDoNotThrow(string source)
    {
        Assert.False(NetNode.TryParse(source, out var node, out var read));
        Assert.Null(node);
        Assert.Equal(0, read);
    }
}

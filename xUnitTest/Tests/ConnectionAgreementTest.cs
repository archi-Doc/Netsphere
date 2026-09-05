// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;
using Xunit;

namespace xUnitTest;

public class ConnectionAgreementTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void EveryLimitIsEnforcedWithFiniteStreamLength(int field)
    {
        var limit = new ConnectionAgreement { MaxStreamLength = 100 };
        var request = limit with { };
        switch (field)
        {
            case 0: request.MaxTransmissions++; break;
            case 1: request.MaxBlockSize++; break;
            case 2: request.MaxStreamLength++; break;
            case 3: request.StreamBufferSize++; break;
            case 4: request.EnableBidirectionalConnection = true; break;
            case 5: request.MinimumConnectionRetentionMics++; break;
            case 6: request.TransmissionTimeout += TimeSpan.FromSeconds(1); break;
        }

        Assert.True(limit.IsInclusive(limit));
        Assert.False(request.IsInclusive(limit));
    }

    [Theory]
    [InlineData(-1, 100, -1)]
    [InlineData(100, -1, -1)]
    [InlineData(100, 200, 200)]
    [InlineData(200, 100, 200)]
    public void AcceptAllNeverReducesStreamLimit(long current, long requested, long expected)
    {
        var agreement = new ConnectionAgreement { MaxStreamLength = current };
        agreement.AcceptAll(new ConnectionAgreement { MaxStreamLength = requested });
        Assert.Equal(expected, agreement.MaxStreamLength);
    }

    [Theory]
    [InlineData(100, -1, false)]
    [InlineData(-1, -1, false)]
    [InlineData(-1, long.MaxValue, true)]
    [InlineData(100, 100, true)]
    [InlineData(100, 101, false)]
    [InlineData(0, 0, true)]
    public void StreamRequestsRequireNonNegativeLength(long limit, long length, bool expected)
    {
        var agreement = new ConnectionAgreement { MaxStreamLength = limit };
        Assert.Equal(expected, agreement.CheckStreamLength(length));
    }
}

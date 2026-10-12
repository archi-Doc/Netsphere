// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;
using Xunit;

namespace xUnitTest.NetsphereTest;

public class TimeRangeReviewTest
{
    [Theory]
    [InlineData(9, false, true)]
    [InlineData(10, true, true)]
    [InlineData(20, true, true)]
    [InlineData(21, false, true)]
    [InlineData(22, false, false)]
    public void RangeAndMarginIncludeBothEndpoints(long target, bool within, bool withinMargin)
    {
        var range = new MicsRange(10, 20);
        Assert.Equal(within, range.IsWithin(target));
        Assert.Equal(within, MicsRange.IsWithin(target, 10, 20));
        Assert.Equal(withinMargin, range.IsWithinMargin(target, 1));
        Assert.Equal(withinMargin, MicsRange.IsWithinMargin(target, 10, 20, 1));
    }

    [Fact]
    public void RangeFactoriesPreserveTheRequestedDuration()
    {
        const long duration = 12345;
        MicsRange[] ranges =
        [
            MicsRange.FromFastCorrectedToFuture(duration),
            MicsRange.FromFastSystemToFuture(duration),
            MicsRange.FromPastToFastSystem(duration),
            MicsRange.FromPastToFastCorrected(duration),
        ];
        foreach (var range in ranges)
        {
            Assert.Equal(duration, range.UpperBound - range.LowerBound);
            Assert.True(range.IsWithin(range.LowerBound));
            Assert.True(range.IsWithin(range.UpperBound));
        }
    }

    [Fact]
    public void UnitFactoriesConvertFractionalDurationsToMicroseconds()
    {
        (MicsRange Range, long Duration)[] ranges =
        [
            (MicsRange.DaysFromFastSystem(0.5), Mics.MicsPerDay / 2),
            (MicsRange.HoursFromFastSystem(0.5), Mics.MicsPerHour / 2),
            (MicsRange.MinutesFromFastSystem(0.5), Mics.MicsPerMinute / 2),
            (MicsRange.SecondsFromFastSystem(0.5), Mics.MicsPerSecond / 2),
            (MicsRange.MillisecondsFromFastSystem(0.5), Mics.MicsPerMillisecond / 2),
            (MicsRange.MicrosecondsFromFastSystem(1.5), 1),
        ];
        foreach (var (range, duration) in ranges)
        {
            Assert.Equal(duration, range.UpperBound - range.LowerBound);
        }
    }
}

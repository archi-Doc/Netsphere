// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

public readonly record struct MicsRange
{
    public static bool IsWithin(long targetMics, long lowerBoundMics, long upperBoundMics)
        => lowerBoundMics <= targetMics && targetMics <= upperBoundMics;

    public static bool IsWithinMargin(long targetMics, long lowerBoundMics, long upperBoundMics, long margin = Mics.DefaultMarginMics)
        => (lowerBoundMics - margin <= targetMics) && (targetMics <= upperBoundMics + margin);

    /// <summary>
    /// Creates a <see cref="MicsRange"/> from the present mics (<see cref="Mics.GetCorrected"/>) to the present+specified mics.
    /// </summary>
    /// <param name="period">Mics ahead from the present mics.</param>
    /// <returns><see cref="MicsRange"/>.</returns>
    public static MicsRange FromFastCorrectedToFuture(long period)
    {
        var current = Mics.FastCorrected;
        return new MicsRange(current - Mics.DefaultMarginMics, current + period);
    }

    public static MicsRange FromFastSystemToFuture(long period)
    {
        var lower = Mics.FastSystem;
        return new(lower - Mics.DefaultMarginMics, lower + period);
    }

    public static MicsRange FromPastToFastSystem(long period)
    {
        var upper = Mics.FastSystem;
        return new(upper - period, upper + Mics.DefaultMarginMics);
    }

    public static MicsRange FromPastToFastCorrected(long period)
    {
        var upper = Mics.FastCorrected;
        return new(upper - period, upper + Mics.DefaultMarginMics);
    }

    public MicsRange(long lowerBoundMics, long upperBoundMics)
    {
        this.LowerBound = lowerBoundMics;
        this.UpperBound = upperBoundMics;
    }

    #region FieldAndProperty

    public readonly long LowerBound;
    public readonly long UpperBound;

    #endregion

    public static MicsRange DaysFromFastSystem(double days)
        => FromFastSystemToFuture((long)(days * Mics.MicsPerDay));

    public static MicsRange HoursFromFastSystem(double hours)
        => FromFastSystemToFuture((long)(hours * Mics.MicsPerHour));

    public static MicsRange MinutesFromFastSystem(double minutes)
        => FromFastSystemToFuture((long)(minutes * Mics.MicsPerMinute));

    public static MicsRange SecondsFromFastSystem(double seconds)
        => FromFastSystemToFuture((long)(seconds * Mics.MicsPerSecond));

    public static MicsRange MillisecondsFromFastSystem(double milliseconds)
        => FromFastSystemToFuture((long)(milliseconds * Mics.MicsPerMillisecond));

    public static MicsRange MicrosecondsFromFastSystem(double microseconds)
        => FromFastSystemToFuture((long)microseconds);

    public bool IsWithin(long targetMics)
        => this.LowerBound <= targetMics && targetMics <= this.UpperBound;

    public bool IsWithinMargin(long targetMics, long margin = Mics.DefaultMarginMics)
        => (this.LowerBound - margin) <= targetMics && targetMics <= (this.UpperBound + margin);
}

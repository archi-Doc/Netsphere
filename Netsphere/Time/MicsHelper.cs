// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Netsphere;

/// <summary>
/// Provides helper methods for working with microseconds (mics).
/// </summary>
public static class MicsHelper
{
    /// <summary>
    /// Checks if the specified interval has passed since the last recorded time.
    /// </summary>
    /// <param name="current">The last recorded time in microseconds.</param>
    /// <param name="interval">The interval to check in microseconds.</param>
    /// <returns>True if the interval has passed; otherwise, false.</returns>
    public static bool CheckInteval(ref long current, long interval)
    {
        var now = Mics.FastSystem;
        if (now - current < interval)
        {
            return false;
        }

        current = now;
        return true;
    }

    /// <summary>
    /// Converts the specified microseconds to a <see cref="DateTime"/>.
    /// </summary>
    /// <param name="mics">The time in microseconds.</param>
    /// <returns>A <see cref="DateTime"/> representing the specified time.</returns>
    public static DateTime MicsToDateTime(this long mics) => new DateTime((long)((double)mics * Time.MicsToTicks));

    /// <summary>
    /// Converts the specified microseconds to a <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="mics">The time in microseconds.</param>
    /// <returns>A <see cref="TimeSpan"/> representing the specified time.</returns>
    public static TimeSpan MicsToTimeSpan(this long mics) => new TimeSpan((long)((double)mics * Time.MicsToTicks));

    /// <summary>
    /// Converts the specified microseconds to a formatted date and time string.
    /// </summary>
    /// <param name="mics">The time in microseconds.</param>
    /// <param name="format">The format string. If null, the default format is used.</param>
    /// <returns>A formatted date and time string representing the specified time.</returns>
    public static string MicsToDateTimeString(this long mics, string? format = null) => MicsToDateTime(mics).ToString(format);

    /// <summary>
    /// Converts the specified microseconds to a formatted time span string.
    /// </summary>
    /// <param name="mics">The time in microseconds.</param>
    /// <returns>A formatted time span string representing the specified mics.</returns>
    public static string MicsToTimeSpanString(this long mics)
    {
        var ts = MicsToTimeSpan(mics);
        return ts.TotalDays >= 1
            ? $"{ts.TotalDays:0.00}d"
            : ts.TotalHours >= 1
                ? $"{ts.TotalHours:0.00}h"
                : ts.TotalMinutes >= 1
                    ? $"{ts.TotalMinutes:0.00}m"
                    : ts.TotalSeconds >= 1
                        ? $"{ts.TotalSeconds:0.00}s"
                        : ts.TotalMilliseconds >= 1
                            ? $"{ts.TotalMilliseconds:0.00}ms"
                            : $"{mics}μs";
    }
}

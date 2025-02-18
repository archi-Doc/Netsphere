// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Netsphere.Misc;

namespace Netsphere;

/// <summary>
/// <see cref="Mics"/> represents time in microseconds (<see cref="long"/>).
/// </summary>
public static class Mics
{
    public const long DefaultMarginMics = MicsPerSecond * 5; // 5 seconds
    public const long DefaultUpdateIntervalMics = MicsPerMillisecond * 100; // 100 milliseconds
    public const long MicsPerYear = 31_536_000_000_000;
    public const long MicsPerDay = 86_400_000_000;
    public const long MicsPerHour = 3_600_000_000;
    public const long MicsPerMinute = 60_000_000;
    public const long MicsPerSecond = 1_000_000;
    public const long MicsPerMillisecond = 1_000;
    public const double MicsPerNanosecond = 0.001d;
    public static readonly double TimestampToMics;
    private static readonly long FixedMics; // Fixed mics at application startup.
    private static long fastSystemMics;
    private static long fastApplicationMics;
    private static long fastUtcNowMics;
    private static long fastFixedUtcNowMics;
    private static long fastCorrectedMics;

    static Mics()
    {
        TimestampToMics = 1_000_000d / Stopwatch.Frequency;
        FixedMics = GetUtcNow() - (long)(Stopwatch.GetTimestamp() * TimestampToMics);
        UpdateFastSystem();
        UpdateFastApplication();
        UpdateFastUtcNow();
        UpdateFastFixedUtcNow();
        UpdateFastCorrected();
    }

    /// <summary>
    /// Gets the cached <see cref="Mics"/> (microseconds) since system startup (Stopwatch.GetTimestamp()).
    /// </summary>
    public static long FastSystem => fastSystemMics;

    /// <summary>
    /// Gets the cached <see cref="Mics"/> (microseconds) since Lp has started.<br/>
    /// Not affected by manual date/time changes.
    /// </summary>
    public static long FastApplication => fastApplicationMics;

    /// <summary>
    /// Gets the cached <see cref="Mics"/> (microseconds) expressed as UTC.<br/>
    /// Mics since 0000-01-01 00:00:00.
    /// </summary>
    public static long FastUtcNow => fastUtcNowMics;

    /// <summary>
    /// Gets the cached fixed <see cref="Mics"/> (microseconds) expressed as UTC.<br/>
    /// Mics since 0000-01-01 00:00:00.<br/>
    /// Not affected by manual date/time changes.
    /// </summary>
    public static long FastFixedUtcNow => fastFixedUtcNowMics;

    /// <summary>
    /// Gets the cached corrected <see cref="Mics"/> expressed as UTC.<br/>
    /// Mics since 0000-01-01 00:00:00.
    /// </summary>
    public static long FastCorrected => fastCorrectedMics;

    public static long UpdateFastSystem() => fastSystemMics = GetSystem();

    public static long UpdateFastApplication() => fastApplicationMics = GetApplication();

    public static long UpdateFastUtcNow() => fastUtcNowMics = GetUtcNow();

    public static long UpdateFastFixedUtcNow() => fastFixedUtcNowMics = GetFixedUtcNow();

    public static long UpdateFastCorrected() => fastCorrectedMics = GetCorrected();

    /// <summary>
    /// Gets the <see cref="Mics"/> (microseconds) since system startup (Stopwatch.GetTimestamp()).
    /// </summary>
    /// <returns><see cref="Mics"/> (microseconds).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetSystem() => (long)(Stopwatch.GetTimestamp() * TimestampToMics);

    /// <summary>
    /// Gets the <see cref="Mics"/> (microseconds) since Lp has started.<br/>
    /// Not affected by manual date/time changes.
    /// </summary>
    /// <returns><see cref="Mics"/> (microseconds).</returns>
    public static long GetApplication() => (long)(Stopwatch.GetTimestamp() * TimestampToMics) - TimeCorrection.InitialSystemMics;

    /// <summary>
    /// Gets the <see cref="Mics"/> (microseconds) expressed as UTC.<br/>
    /// Mics since 0000-01-01 00:00:00.
    /// </summary>
    /// <returns><see cref="Mics"/> (microseconds).</returns>
    public static long GetUtcNow() => (long)(DateTime.UtcNow.Ticks * 0.1d);

    /// <summary>
    /// Gets the fixed <see cref="Mics"/> (microseconds) expressed as UTC.
    /// Not affected by manual date/time changes.
    /// </summary>
    /// <returns><see cref="Mics"/> (microseconds).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetFixedUtcNow() => FixedMics + GetSystem();

    /// <summary>
    /// Get the corrected <see cref="Mics"/> expressed as UTC.
    /// </summary>
    /// <returns>The corrected <see cref="Mics"/>.</returns>
    public static long GetCorrected()
    {
        long mics;
        if (Time.NtpCorrection is { } ntpCorrection)
        {// Ntp correction
            ntpCorrection.TryGetCorrectedMics(out mics);
            return mics;
        }

        TimeCorrection.GetCorrectedMics(out mics);
        return mics;
    }

    public static long FromDays(double days) => (long)(days * MicsPerDay);

    public static long FromHours(double hours) => (long)(hours * MicsPerHour);

    public static long FromMinutes(double minutes) => (long)(minutes * MicsPerMinute);

    public static long FromSeconds(double seconds) => (long)(seconds * MicsPerSecond);

    public static long FromMilliseconds(double milliseconds) => (long)(milliseconds * MicsPerMillisecond);

    public static long FromMicroseconds(double microseconds) => (long)microseconds;

    public static long FromNanoseconds(double nanoseconds) => (long)(nanoseconds * MicsPerNanosecond);
}

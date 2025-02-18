// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Netsphere.Misc;

#pragma warning disable CA2255

namespace Netsphere;

public static class Time
{
    public static readonly double TimestampToTicks;
    public static readonly double MicsToTicks;
    private static readonly long FixedTimestamp; // Fixed timestamp at application startup.
    private static readonly DateTime FixedUtcNow; // Fixed DateTime at application startup.

    [ModuleInitializer]
    public static void Initialize()
    {
        RuntimeHelpers.RunClassConstructor(typeof(Time).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(Mics).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(TimeCorrection).TypeHandle);
    }

    static Time()
    {
        TimestampToTicks = 10_000_000d / Stopwatch.Frequency;
        MicsToTicks = TimestampToTicks / Mics.TimestampToMics;
        FixedTimestamp = Stopwatch.GetTimestamp();
        FixedUtcNow = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets a DateTime since system startup (Stopwatch.GetTimestamp()).
    /// </summary>
    /// <returns><see cref="DateTime"/>.</returns>
    public static DateTime GetSystem() => new DateTime((long)(Stopwatch.GetTimestamp() * TimestampToTicks));

    /// <summary>
    /// Gets a <see cref="DateTime"/> since Lp has started (0001/01/01 0:00:00).<br/>
    /// Not affected by manual date/time changes.
    /// </summary>
    /// <returns><see cref="DateTime"/>.</returns>
    public static DateTime GetApplication() => new DateTime((long)(Mics.GetApplication() * MicsToTicks));

    /// <summary>
    /// Gets a <see cref="DateTime"/> expressed as UTC.
    /// </summary>
    /// <returns><see cref="DateTime"/>.</returns>
    public static DateTime GetUtcNow() => DateTime.UtcNow;

    /// <summary>
    /// Gets a fixed <see cref="DateTime"/> expressed as UTC.
    /// Not affected by manual date/time changes.
    /// </summary>
    /// <returns><see cref="DateTime"/>.</returns>
    public static DateTime GetFixedUtcNow()
        => FixedUtcNow + TimeSpan.FromTicks((long)((Stopwatch.GetTimestamp() - FixedTimestamp) * TimestampToTicks));

    /// <summary>
    /// Get a corrected <see cref="DateTime"/> expressed as UTC.
    /// </summary>
    /// <returns>The corrected <see cref="DateTime"/>.</returns>
    public static DateTime GetCorrected()
    {
        if (NtpCorrection is { } ntpCorrection)
        {// Ntp correction
            ntpCorrection.TryGetCorrectedUtcNow(out var utcNow);
            return utcNow;
        }

        var result = TimeCorrection.GetCorrectedMics(out var mics);
        return new DateTime((long)(mics * MicsToTicks));
    }

    public static void SetNtpCorrection(NtpCorrection ntpCorrection)
        => NtpCorrection = ntpCorrection;

    public static NtpCorrection? NtpCorrection { get; private set; }
}

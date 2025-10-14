// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// <see cref="Seconds"/> represents time in seconds (<see cref="int"/>).
/// </summary>
public static class Seconds
{
    public const int Infinite = -1;
    public const int SecondsPerYear = 3600 * 24 * 365;
    public const int SecondsPerDay = 3600 * 24;
    public const int SecondsPerHour = 3600;
    public const int SecondsPerMinute = 60;

    public static int FromYears(double years) => (int)(years * SecondsPerYear);

    public static int FromDays(double days) => (int)(days * SecondsPerDay);

    public static int FromHours(double hours) => (int)(hours * SecondsPerHour);

    public static int FromMinutes(double minutes) => (int)(minutes * SecondsPerMinute);
}

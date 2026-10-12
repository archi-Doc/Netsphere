// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using CrossChannel;

namespace Netsphere;

/// <summary>
/// Receives observed second and minute changes from <see cref="ClockHand"/>.
/// </summary>
/// <remarks>
/// Keep handlers short and non-blocking. Delays and clock corrections can coalesce or repeat clock boundaries.
/// </remarks>
[RadioService(AutoRegisterServiceAndSender = false)]
public interface IClockHandTarget : IRadioService
{
    /// <summary>
    /// Called on the first poll and whenever the corrected clock's second changes.
    /// </summary>
    void OnEverySecond();

    /// <summary>
    /// Called when the corrected clock's minute changes between polls.
    /// </summary>
    void OnEveryMinute();
}

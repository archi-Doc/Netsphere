// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using CrossChannel;

namespace Netsphere;

/// <summary>
/// Defines clock-tick callbacks for radio service targets that need periodic time-based updates.
/// </summary>
/// <remarks>
/// Implementations are invoked by the clock hand dispatcher at fixed intervals.
/// Keep handlers lightweight and non-blocking to avoid delaying other targets.
/// </remarks>
[RadioService(AutoRegisterRadioServiceAndSender = false)]
public interface IClockHandTarget : IRadioService
{
    /// <summary>
    /// Invoked once every second.<br/>
    /// Keep handlers lightweight and non-blocking to avoid delaying other targets.
    /// </summary>
    void OnEverySecond();

    /// <summary>
    /// Invoked once every minute.<br/>
    /// Keep handlers lightweight and non-blocking to avoid delaying other targets.
    /// </summary>
    void OnEveryMinute();
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Runs a lightweight background clock loop and publishes second/minute ticks to <see cref="IClockHandTarget"/>.<br/>
/// Do not forget to call ClockHand.Start() before using it.
/// </summary>
/// <remarks>
/// The loop checks time every <see cref="MillisecondsToWait"/> milliseconds, emits
/// <see cref="IClockHandTarget.OnEverySecond"/> once per second, and emits
/// <see cref="IClockHandTarget.OnEveryMinute"/> at each minute boundary.
/// </remarks>
public class ClockHand : TaskCore
{
    /// <summary>
    /// Poll interval for the internal timing loop.
    /// </summary>
    private const int MillisecondsToWait = 50;

    private readonly IClockHandTarget broker;

    private static async Task Process(object? obj)
    {
        if (obj is not ClockHand clockHand)
        {
            return;
        }

        long lastSeconds = 0;
        while (await clockHand.Delay(MillisecondsToWait))
        {
            var currentSeconds = Mics.GetCorrected() / Mics.MicsPerSecond;
            if (currentSeconds == lastSeconds)
            {
                continue;
            }

            lastSeconds = currentSeconds;
            if (clockHand.IsPaused)
            {
                continue;
            }

            clockHand.broker.OnEverySecond();

            if (currentSeconds % 60 == 0)
            {
                clockHand.broker.OnEveryMinute();
            }
        }
    }

    public ClockHand(UnitContext unitContext)
        : base(unitContext.Core, Process, false)
    {
        this.broker = unitContext.Radio.GetChannel<IClockHandTarget>().GetBroker();
    }
}

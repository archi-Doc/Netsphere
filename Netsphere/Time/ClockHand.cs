// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Runs a lightweight background clock loop and publishes second/minute ticks to <see cref="IClockHandTarget"/>.<br/>
/// Do not forget to call ClockHand.SendSignal(ExecutionSignal.Start) before using it.
/// </summary>
/// <remarks>
/// The loop checks time every <see cref="MillisecondsToWait"/> milliseconds, emits
/// <see cref="IClockHandTarget.OnEverySecond"/> once per second, and emits
/// <see cref="IClockHandTarget.OnEveryMinute"/> at each minute boundary.
/// </remarks>
public class ClockHand : TaskCore<ClockHand>
{
    /// <summary>
    /// Poll interval for the internal timing loop.
    /// </summary>
    private const int MillisecondsToWait = 20;

    private readonly IClockHandTarget broker;

    private static async Task Process(ClockHand clockHand)
    {
        long lastSeconds = 0;
        var lastMinutes = Mics.GetCorrected() / Mics.MicsPerSecond / 60;
        while (await clockHand.TryDelay(MillisecondsToWait))
        {
            var currentSeconds = Mics.GetCorrected() / Mics.MicsPerSecond;
            if (currentSeconds == lastSeconds)
            {
                continue;
            }

            lastSeconds = currentSeconds;
            clockHand.broker.OnEverySecond();

            var currentMinutes = currentSeconds / 60;
            if (currentMinutes != lastMinutes)
            {// Compare minute numbers instead of testing for second 0, which a stall or a clock correction can skip. A backward correction must not suppress ticks.
                lastMinutes = currentMinutes;
                clockHand.broker.OnEveryMinute();
            }
        }
    }

    public ClockHand(UnitContext unitContext)
        : base(unitContext.ExecutionRoot, Process, ExecutionCoreOptions.DelayedStart)
    {
        this.broker = unitContext.Radio.GetChannel<IClockHandTarget>().GetBroker();
    }
}

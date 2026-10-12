// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Publishes observed second and minute changes to <see cref="IClockHandTarget"/>.
/// </summary>
/// <remarks>
/// Start with <c>SendSignal(ExecutionSignal.Start)</c>. The loop polls the corrected clock every 20 milliseconds.
/// Delays coalesce missed boundaries; backward clock corrections can repeat them. Callbacks run on the polling loop.
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

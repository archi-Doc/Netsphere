// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using CrossChannel;

namespace Netsphere;

[RadioService]
public interface IClockHandTarget : IRadioService
{
    /// <summary>
    /// Called once per second.
    /// </summary>
    void OnEverySecond();

    /// <summary>
    /// Called once per minute.
    /// </summary>
    void OnEveryMinute();
}

public class ClockHand : TaskCore
{
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

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

public class ClockHand
{
    private readonly IClockHandTarget broker;

    public ClockHand(RadioClass radio)
    {
        this.broker = radio.GetChannel<IClockHandTarget>().GetSender();
        this.broker.OnEveryMinute();
    }
}

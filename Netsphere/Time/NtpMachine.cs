// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using Netsphere.Misc;

namespace Netsphere.Machines;

[MachineObject(UseServiceProvider = true)]
public partial class NtpMachine : Machine
{
    private const string TimestampFormat = "MM-dd HH:mm:ss.fff K";

    public NtpMachine(ILogger<NtpMachine> logger, NetBase netBase, NetControl netControl, NtpCorrection ntpCorrection)
    {
        this.logger = logger;
        this.NetBase = netBase;
        this.NetControl = netControl;
        this.ntpCorrection = ntpCorrection;

        this.DefaultTimeout = TimeSpan.FromSeconds(5);
    }

    public NetBase NetBase { get; }

    public NetControl NetControl { get; }

    [StateMethod(0)]
    protected async Task<StateResult> Initial(StateParameter parameter)
    {
        bool corrected;
        DateTime correctedNow;

        var dif = Mics.GetUtcNow() - this.ntpCorrection.LastCorrectedMics;
        if (dif > 0 && dif < Mics.FromHours(1))
        {// Already correctedd
            corrected = this.ntpCorrection.TryGetCorrectedUtcNow(out correctedNow);
            this.logger.TryGet()?.Log($"Already corrected {corrected}, {correctedNow.ToString()}");
            this.SetLoggerTimeOffset();

            var ts = (Mics.FromHours(1) - dif).MicsToTimeSpan();
            this.TimeUntilRun = ts;
            return StateResult.Continue;
        }

        this.ntpCorrection.LastCorrectedMics = Mics.GetUtcNow();
        await this.ntpCorrection.Correct(this.CancellationToken).ConfigureAwait(false);

        var timeoffset = this.ntpCorrection.GetTimeOffset();
        if (timeoffset.TimeoffsetCount == 0)
        {
            this.ChangeState(State.SafeHoldMode, false);
            return StateResult.Continue;
        }

        this.logger.TryGet(LogLevel.Debug)?.Log($"Timeoffset {timeoffset.MeanTimeoffset} ms [{timeoffset.TimeoffsetCount}]");
        this.SetLoggerTimeOffset();

        corrected = this.ntpCorrection.TryGetCorrectedUtcNow(out correctedNow);
        this.logger.TryGet()?.Log($"Corrected {corrected}, {correctedNow.ToString()}");

        this.TimeUntilRun = TimeSpan.FromHours(1);
        return StateResult.Continue;
    }

    [StateMethod]
    protected async Task<StateResult> SafeHoldMode(StateParameter parameter)
    {
        this.logger?.TryGet(LogLevel.Warning)?.Log($"Safe-hold mode");
        if (await this.ntpCorrection.CheckConnection(this.CancellationToken).ConfigureAwait(false))
        {
            this.ntpCorrection.ResetHostnames();
            this.ChangeState(State.Initial);
            return StateResult.Continue;
        }

        return StateResult.Continue;
    }

    private void SetLoggerTimeOffset()
    {
        var offset = this.ntpCorrection.GetTimeOffset();
        UnitLogger.SetTimeOffset(TimeSpan.FromMilliseconds(offset.MeanTimeoffset));
    }

    private ILogger<NtpMachine> logger;
    private NtpCorrection ntpCorrection;
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;

#pragma warning disable SA1202 // Elements should be ordered by access
#pragma warning disable SA1300 // Element should begin with upper-case letter
#pragma warning disable CS0649

namespace Netsphere.Misc;

/*
/// <summary>
/// Good Old-Fashioned Multimedia Timer.
/// Ok, I know it's obsolete.
/// </summary>
public class MultimediaTimer : IDisposable
{
    public delegate void TimerProc();

    private delegate void Proc(int id, int msg, int user, int param1, int param2);

    private struct TimerCaps
    {
        public int PeriodMin;
        public int PeriodMax;
    }

    [DllImport("winmm.dll")]
    private static extern int timeGetDevCaps(ref TimerCaps caps, int sizeOfTimerCaps);

    [DllImport("winmm.dll")]
    private static extern int timeSetEvent(int delay, int resolution, Proc proc, int user, int mode);

    [DllImport("winmm.dll")]
    private static extern int timeKillEvent(int id);

    public static MultimediaTimer? TryCreate(int intervalMilliseconds, TimerProc timerProc)
    {
        try
        {
            return new MultimediaTimer(intervalMilliseconds, timerProc);
        }
        catch
        {
            return null;
        }
    }

    private MultimediaTimer(int intervalMilliseconds, TimerProc timerProc)
    {
        this.timerProc = new Proc((a, b, c, d, e) => { timerProc(); }); // Avoid exception: A callback was made on a garbage collected delegate of type
        this.timerId = timeSetEvent(intervalMilliseconds, 1, this.timerProc, 0, 1);
    }

    private int timerId;
    private Proc timerProc;

#pragma warning disable SA1124 // Do not use regions
    #region IDisposable Support
#pragma warning restore SA1124 // Do not use regions

    private bool disposed = false; // To detect redundant calls.

    /// <summary>
    /// Finalizes an instance of the <see cref="MultimediaTimer"/> class.
    /// </summary>
    ~MultimediaTimer()
    {
        this.Dispose(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// free managed/native resources.
    /// </summary>
    /// <param name="disposing">true: free managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this.disposed)
        {
            if (disposing)
            {
                // free managed resources.
            }

            // free native resources here if there are any.
            try
            {
                timeKillEvent(this.timerId);
            }
            catch
            {
            }

            this.disposed = true;
        }
    }
    #endregion
}*/

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Netsphere.Misc;

/// <summary>
/// Provides a set of methods that you can use to measure elapsed time for benchmark.<br/>
/// { Start() -> Stop() } x repetition -> GetResult().
/// </summary>
public class BenchTimer
{
    public BenchTimer()
    {
        this.frequencyR = 1.0d / (double)Stopwatch.Frequency;
    }

    /// <summary>
    /// Starts measuring elapsed time.
    /// </summary>
    public void Start()
    {
        this.lastTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Removes all elapsed time records and starts measuring elapsed time.
    /// </summary>
    public void Restart()
    {
        this.Clear();
        this.Start();
    }

    /// <summary>
    /// Stops measuring elapsed time.
    /// </summary>
    public void Stop()
    {
        var timestamp = Stopwatch.GetTimestamp();
        if (this.lastTimestamp == 0)
        {
            this.lastTimestamp = timestamp;
        }

        this.records.Add(timestamp - this.lastTimestamp);
        this.lastTimestamp = timestamp;
    }

    /// <summary>
    /// Stops measuring elapsed time.
    /// </summary>
    /// <param name="caption">A caption to be added to the elapsed time record.</param>
    /// <returns>A string with the caption and the elapsed time in milliseconds ("caption: 123 ms").</returns>
    public string StopAndGetText(string? caption = null)
    {
        this.Stop();
        var timestamp = this.records.LastOrDefault();
        return this.GetText(timestamp, caption);
    }

    public string GetResult(string? caption = null)
    {
        if (this.records.Count == 0)
        {
            return string.Empty;
        }
        else if (this.records.Count == 1)
        {
            var timestamp = this.records.LastOrDefault();
            return this.GetText(timestamp, caption);
        }

        var min = this.TicksToString(this.records.Min());
        var max = this.TicksToString(this.records.Max());
        var average = this.DoubleToString(this.records.Average());

        if (caption == null)
        {// 123 ms [4] (Min 100 ms, Max 150 ms)
            return $"{average} ms [{this.records.Count}] (Min {min} ms, Max {max} ms)";
        }
        else
        {// caption: 123 ms [4] (Min 100 ms, Max 150 ms)
            return $"{caption}: {average} ms [{this.records.Count}] (Min {min} ms, Max {max} ms)";
        }
    }

    /// <summary>
    /// Removes all elapsed time records.
    /// </summary>
    public void Clear()
    {
        this.records.Clear();
    }

    private readonly double frequencyR;
    private long lastTimestamp;
    private List<long> records = new();

    private string GetText(long timestamp, string? caption)
    {
        if (caption == null)
        {// 123 ms
            return $"{this.TicksToString(timestamp)} ms";
        }
        else
        {// caption: 123 ms
            return $"{caption}: {this.TicksToString(timestamp)} ms";
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string TicksToString(long ticks)
        => this.DoubleToString((double)ticks * this.frequencyR * 1000);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string DoubleToString(double ms)
    {
        if (ms < 10d)
        {// 0.12, 1.23
            return ms.ToString("F2");
        }
        else if (ms < 100d)
        {// 12.3
            return ms.ToString("F1");
        }
        else
        {
            return ms.ToString("F0");
        }
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

#pragma warning disable SA1401 // Fields should be private

namespace Netsphere.Misc;

public partial class TimeCorrection
{
    /// <summary>
    /// The maximum number of time corrections.
    /// </summary>
    public const uint MaxCorrections = 1_000;

    /// <summary>
    /// The minimum number of corrections required for a valid corrected mics/time.
    /// </summary>
    public const uint MinCorrections = 10;

    public enum Result
    {
        NotCorrected,
        Corrected,
    }

    [ValueLinkObject]
    private partial class TimeDifference
    {
        [Link(Type = ChainType.QueueList, Name = "Queue")]
        internal TimeDifference(long difference)
        {
            this.Difference = difference;
        }

        [Link(Type = ChainType.Ordered)]
        internal long Difference;
    }

    static TimeCorrection()
    {
        InitialUtcMics = Mics.GetUtcNow();
        InitialSystemMics = Mics.GetSystem();

        initialDifference = InitialUtcMics - InitialSystemMics;
        timeCorrections = new();
    }

    /// <summary>
    /// Get the corrected <see cref="Mics"/> expressed as UTC.
    /// </summary>
    /// <param name="correctedMics">The corrected <see cref="Mics"/>.</param>
    /// <returns><see cref="Result"/>.</returns>
    public static Result GetCorrectedMics(out long correctedMics)
    {
        var currentMics = Mics.GetSystem() - InitialSystemMics + InitialUtcMics;
        if (timeCorrections.QueueChain.Count < MinCorrections)
        {
            correctedMics = currentMics;
            return Result.NotCorrected;
        }

        var difference = GetCollectionDifference(currentMics);
        correctedMics = currentMics + difference;

        return Result.Corrected;
    }

    public static void AddCorrection(long utcMics)
    {
        var difference = utcMics - (Mics.GetSystem() + initialDifference);

        lock (timeCorrections)
        {
            var c = new TimeDifference(difference);
            c.Goshujin = timeCorrections;

            while (timeCorrections.QueueChain.Count > MaxCorrections)
            {
                if (timeCorrections.QueueChain.TryPeek(out var result))
                {
                    result.Goshujin = null;
                }
            }
        }
    }

    private static long GetCollectionDifference(long currentMics)
    {
        var diff = correctionDifference;
        if (diff != 0 && System.Math.Abs(currentMics - correctionMics) < Mics.FromSeconds(1.0d))
        {
            return diff;
        }

        lock (timeCorrections)
        {// Calculate the average of the differences in the middle half.
            var half = timeCorrections.DifferenceChain.Count >> 1;
            var quarter = timeCorrections.DifferenceChain.Count >> 2;

            var node = timeCorrections.DifferenceChain.First;
            for (var i = 0; i < quarter; i++)
            {
                node = node!.DifferenceLink.Next;
            }

            long total = 0;
            for (var i = 0; i < half; i++)
            {
                total += node!.Difference;
                node = node!.DifferenceLink.Next;
            }

            diff = total / half;
            correctionDifference = diff;
            correctionMics = currentMics;
        }

        return diff;
    }

    public static long InitialUtcMics { get; }

    public static long InitialSystemMics { get; }

    private static long initialDifference;
    private static TimeDifference.GoshujinClass timeCorrections;
    private static long correctionDifference;
    private static long correctionMics;
}

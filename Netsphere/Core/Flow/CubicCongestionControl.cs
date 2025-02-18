// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using Arc.Collections;

namespace Netsphere.Core;

public class CubicCongestionControl : ICongestionControl
{
    private const double BrakeThreshold = 0.05d;
    private const int CubicThreshold = 10;
    private const double InitialCwnd = 10;
    private const double MaxCwnd = 10_000;
    private const double MinCwnd = 2;
    private const double BurstRatio = 1.5d;
    private const double BurstRatioInv = 1.0d / BurstRatio;
    private const double Beta = 0.2d;
    private const double WtcpRatio = 3d * Beta / (2d - Beta);
    private const double FastConvergenceRatio = (2d - Beta) / 2d;
    private const ulong CubicFactor = (1ul << 40) / 410ul;
    private const int HystartMinCwnd = 16;
    private const int HystartRttSamples = 8;
    private const int HystartMinEta = 4_000;
    private const int HystartMaxEta = 16_000;

    private static ReadOnlySpan<byte> v =>
    [
        0, 54, 54, 54,  118, 118, 118, 118,
        123,  129,  134,  138,  143,  147,  151,  156,
        157,  161,  164,  168,  170,  173,  176,  179,
        181,  185,  187,  190,  192,  194,  197,  199,
        200,  202,  204,  206,  209,  211,  213,  215,
        217,  219,  221,  222,  224,  225,  227,  229,
        231,  232,  234,  236,  237,  239,  240,  242,
        244,  245,  246,  248,  250,  251,  252,  254,
    ];

    public static uint CubicRoot(ulong a)
    {
        var b = 64 - BitOperations.LeadingZeroCount(a);
        if (b < 7)
        {
            return ((uint)v[(int)a] + 35) >> 6;
        }

        b = ((b * 84) >> 8) - 1;
        var shift = (int)(a >> (b * 3));
        var x = (((uint)v[shift] + 10) << b) >> 6;
        x = (2 * x) + (uint)(a / ((ulong)x * (ulong)(x - 1)));
        x = (x * 341) >> 10;
        return x;
    }

    public CubicCongestionControl(Connection connection)
    {
        this.Connection = connection;
        this.cwnd = InitialCwnd;
        this.ssthresh = MaxCwnd;
        this.slowstart = true;
        this.UpdateRegen();

        if (NetConstants.LogLowLevelNet &&
            connection.IsClient)
        {
            this.logger = this.Connection.ConnectionTerminal.NetBase.UnitLogger.GetLogger<CubicCongestionControl>();
        }
    }

    #region FieldAndProperty

    public Connection Connection { get; set; }

    public int NumberInFlight
        => this.genesInFlight.Count;

    public bool IsCongested
        => this.genesInFlight.Count >= this.capacityInt;

    public double FailureRatio
    {
        get
        {
            var total = this.positiveFactor + this.negativeFactor;
            if (total == 0)
            {
                return 0;
            }
            else
            {
                return (this.negativeFactor - 1d) / total; // Decrease sensitivity by subtracting 1.
            }
        }
    }

    private readonly ILogger? logger;

    private readonly Lock lockObject = new();
    private readonly UnorderedLinkedList<SendGene> genesInFlight = new(); // Retransmission mics, gene
    private readonly ConcurrentQueue<SendGene> genesLossDetected = new();

    // Smoothing transmissions
    private double capacity; // Equivalent to cwnd, but increases gradually to prevent mass transmission at once.
    private int capacityInt; // (int)this.capacity
    private double regen; // Number of packets that can be transmitted per ms.
    private double boost; // Number of packets that can be boost-transmitted per ms.
    private long boostMicsMax; // Upper limit of boost time.
    private long boostMics; // Remaining boost time.

    // Cubic
    private bool capacityLimited;
    private int cubicCount;
    private int ackCount;
    private double increasePerAck; // 1 / this.cnt
    private double cwnd; // Upper limit of the total number of genes in-flight.
    private double tcpCwnd;
    private double ssthresh;
    private ulong k;
    private double originPoint;
    private bool slowstart;

    // Packet loss
    private long epochStart;
    private double lastMaxCwnd;
    private long refractoryMics;
    private uint deliverySuccess;
    private uint deliveryFailure;
    private double positiveFactor;
    private double negativeFactor;
    private double power;

    // Hystart
    private long currentRoundMics;
    private int previousMinRtt;
    private int currentMinRtt = int.MaxValue;
    private int currentRttSamples;

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ICongestionControl.AddInFlight(SendGene sendGene, int additional)
    {
        using (this.lockObject.EnterScope())
        {
            if (sendGene.Node is UnorderedLinkedList<SendGene>.Node node)
            {
                this.genesInFlight.MoveToLast(node);
            }
            else
            {
                sendGene.Node = this.genesInFlight.AddLast(sendGene);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ICongestionControl.RemoveInFlight(SendGene sendGene, bool ack)
    {
        using (this.lockObject.EnterScope())
        {
            if (ack)
            {
                this.ackCount++;
                this.ReportDeliverySuccess();
            }

            if (sendGene.Node is UnorderedLinkedList<SendGene>.Node node)
            {
                // Console.WriteLine($"RemoveInFlight {this.Connection.IsClient} {sendGene.GeneSerial}/{sendGene.SendTransmission.GeneSerialMax}");
                this.genesInFlight.Remove(node);
                sendGene.Node = default;
            }
        }
    }

    void ICongestionControl.LossDetected(SendGene sendGene)
    {
        if (sendGene.CurrentState == SendGene.State.LossDetected)
        {
            return;
        }
        else
        {
            sendGene.SetLossDetected();
            this.genesLossDetected.Enqueue(sendGene);
        }
    }

    bool ICongestionControl.Process(NetSender netSender, long elapsedMics, double elapsedMilliseconds)
    {// lock (ConnectionTerminal.CongestionControlList)
        if (this.Connection.IsDisposed)
        {// The connection is disposed.
            return false;
        }

        using (this.lockObject.EnterScope())
        {// To prevent deadlocks, the lock order for CongestionControl must be the lowest, and it must not acquire locks by calling functions of other classes.
            /*var nif = (double)this.NumberInFlight;
            if (nif > 0d)
            {
                nif = this.genesLossDetected.Count / nif;
                if (nif > 0d)
                {
                    Console.WriteLine($"{this.genesLossDetected.Count} {nif:F2}");
                }
            }*/

            // CongestionControl: Positive/Negative factor
            this.ProcessFactor();

            // CongestionControl: Cubic (cwnd)
            this.capacityLimited |= (this.genesInFlight.Count * 8) >= (this.capacityInt * 7);
            if (this.cubicCount++ >= CubicThreshold)
            {
                if (this.capacityLimited)
                {// Capacity limited
                    this.capacityLimited = false;
                    this.UpdateCubic((double)this.ackCount);
                    this.UpdateRegen();

                    if (NetConstants.LogLowLevelNet)
                    {
                        this.logger?.TryGet(LogLevel.Debug)?.Log($"cwnd:{this.cwnd:F2} {this.increasePerAck:F3} k:{this.k:F2} tcp:{this.tcpCwnd:F2}");
                        // Console.WriteLine($"cwnd:{this.cwnd:F2} {this.increasePerAck:F3} k:{this.k:F2} tcp:{this.tcpCwnd:F2}");
                    }
                }

                this.cubicCount = 0;
                this.ackCount = 0;
            }

            // CongestionControl: Capacity
            this.CalculateCapacity(elapsedMics, elapsedMilliseconds);
            this.capacityInt = (int)this.capacity;

            // this.logger?.TryGet(LogLevel.Debug)?.Log($"{(capacityLimited ? "CAP " : string.Empty)}current/cap/cwnd {this.NumberOfGenesInFlight}/{this.capacity:F1}/{this.cwnd:F1} regen {this.regen:F2} boost {this.boost:F2} mics/max {this.boostMics}/{this.boostMicsMax}");

            // Resend
            this.ProcessResend(netSender);
        }

        return true;
    }

    void ICongestionControl.AddRtt(int rttMics)
    {
        if (this.slowstart)
        {
            Interlocked.Increment(ref this.currentRttSamples);

            int current;
            do
            {
                current = this.currentMinRtt;
                if (current < rttMics)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref this.currentMinRtt, rttMics, current) != current);
        }
    }

    private void ProcessFactor()
    {
        if (Mics.FastSystem <= this.refractoryMics)
        {
            return;
        }

        if (this.cubicCount == 0 || this.power == 0)
        {
            this.power = Math.Pow(0.5, 1000d / this.Connection.MinimumRtt);
        }

        this.positiveFactor += Volatile.Read(ref this.deliverySuccess);
        this.negativeFactor += Volatile.Read(ref this.deliveryFailure);
        Volatile.Write(ref this.deliverySuccess, 0);
        Volatile.Write(ref this.deliveryFailure, 0);

        var failureRatio = this.FailureRatio;
        if (failureRatio > BrakeThreshold)
        {
            if (this.negativeFactor > this.positiveFactor)
            {
                this.Connection.DoubleTaichi();
            }

            Console.WriteLine($"Brake: {this.Connection.Taichi},{failureRatio:F3}");
            Console.WriteLine($"+{this.positiveFactor:F3} -{this.negativeFactor:F3} : {failureRatio:F3} power {this.power:F3}");

            this.positiveFactor = 0;
            this.negativeFactor = 0;
            this.Brake();
        }
        else
        {
            // Console.WriteLine($"+{this.positiveFactor:F3} -{this.negativeFactor:F3} : {failureFactor:F3} power {this.power:F3}");

            if (this.positiveFactor >= 2d)
            {
                this.Connection.ResetTaichi();
            }

            this.positiveFactor *= this.power;
            this.negativeFactor *= this.power;
        }
    }

    private void Brake()
    {
        this.refractoryMics = Mics.FastSystem + this.Connection.SmoothedRtt;

        this.epochStart = 0;
        if (this.cwnd < this.lastMaxCwnd)
        {// Fast convergence
            this.lastMaxCwnd = this.cwnd * FastConvergenceRatio;
        }
        else
        {
            this.lastMaxCwnd = this.cwnd;
        }

        this.cwnd *= 1 - Beta;
        if (this.cwnd < MinCwnd)
        {
            this.cwnd = MinCwnd;
        }

        this.ssthresh = this.cwnd;
    }

    private void ProcessResend(NetSender netSender)
    {// using (this.lockObject.EnterScope())
        int resendCapacity = 1 + (int)this.boost; // betacode
        SendGene? gene;

        // Loss detection
        while (resendCapacity > 0 && this.genesLossDetected.TryDequeue(out gene))
        {
            if (gene.Node is not UnorderedLinkedList<SendGene>.Node node)
            {
                continue;
            }

            resendCapacity--;
            this.ReportDeliveryFailure();
            if (!gene.Resend_NotThreadSafe(netSender, 0))
            {// Cannot send
                this.genesInFlight.Remove(node);
                gene.Node = default;
            }
            else
            {// Resend
                Console.WriteLine($"Resend(loss detection): {gene.GeneSerial}");
                this.genesInFlight.MoveToLast(node);  // Move to the last.
            }
        }

        var timeout = this.Connection.TaichiTimeout;
        while (resendCapacity > 0 && this.genesInFlight.First is { } firstNode)
        {// Retransmission. (Do not check IsCongested, as it causes Genes in-flight to be stuck and stops transmission)
            gene = firstNode.Value;
            if (Mics.FastSystem < (gene.SentMics + timeout))
            {
                break;
            }

            resendCapacity--;
            this.ReportDeliveryFailure();
            Console.WriteLine($"Resend(timeout2): {gene.GeneSerial}/{gene.SendTransmission.GeneSerialMax} ({this.Connection.IsClient}) {gene.SendTransmission.Connection.RetransmissionTimeout} mics");
            if (!gene.Resend_NotThreadSafe(netSender, 0))
            {// Cannot send
                this.genesInFlight.Remove(firstNode);
                gene.Node = default;
            }
            else
            {// Move to the last.
                this.genesInFlight.MoveToLast(firstNode);
            }
        }
    }

    private bool CalculateCapacity(long elapsedMics, double elapsedMilliseconds)
    {
        if (this.capacityInt <= this.genesInFlight.Count)
        {// Capacity limited
            if (this.boostMics > 0)
            {// Boost
                this.capacity += this.boost * elapsedMilliseconds;

                if (this.capacity >= this.cwnd)
                {
                    this.capacity = this.cwnd;

                    this.boostMics += elapsedMics;
                    if (this.boostMics > this.boostMicsMax)
                    {
                        this.boostMics = this.boostMicsMax;
                    }

                    return true;
                }
                else
                {
                    this.boostMics -= elapsedMics;
                    return true;
                }
            }
            else
            {// Normal
                this.capacity += this.regen * elapsedMilliseconds;

                if (this.capacity >= this.cwnd)
                {
                    this.capacity = this.cwnd;

                    this.boostMics += elapsedMics;
                    if (this.boostMics > this.boostMicsMax)
                    {
                        this.boostMics = this.boostMicsMax;
                    }

                    return true;
                }
                else
                {
                    return true;
                }
            }
        }
        else
        {// Not capacity limited (capacity > this.genesInFlight.Count).
            var upperLimit = this.genesInFlight.Count + this.boost;
            if (this.capacity > upperLimit)
            {
                this.capacity = upperLimit;
            }

            this.boostMics += elapsedMics;
            if (this.boostMics > this.boostMicsMax)
            {
                this.boostMics = this.boostMicsMax;
            }

            return false;
        }
    }

    private void UpdateCubic(double acked)
    {
        if (this.slowstart)
        {// Hystart
            var currentMinRtt = this.currentMinRtt;
            var currentRttSamples = this.currentRttSamples;

            if (this.cwnd >= HystartMinCwnd &&
                currentRttSamples >= HystartRttSamples &&
                currentMinRtt != 0 &&
                this.previousMinRtt != 0)
            {
                var eta = Math.Clamp(currentMinRtt >> 3, HystartMinEta, HystartMaxEta);
                if (currentMinRtt >= (this.previousMinRtt + eta))
                {// Exit slow start
                    Console.WriteLine($"Hystart MinRtt Current  {currentMinRtt / 1000} ms Previous {this.previousMinRtt / 1000} ms");
                    Console.WriteLine($"Exit slow start");
                    this.slowstart = false;
                }
            }

            if ((Mics.FastSystem - this.currentRoundMics) > this.Connection.SmoothedRtt)
            {
                this.currentRoundMics = Mics.FastSystem;
                if (currentRttSamples >= HystartRttSamples)
                {
                    this.previousMinRtt = currentMinRtt;
                }

                Volatile.Write(ref this.currentRttSamples, 0);
                Volatile.Write(ref this.currentMinRtt, int.MaxValue);
            }
        }

        if (this.slowstart)
        {
            // var cwnd = Math.Min(this.cwnd + acked, this.ssthresh);
            var cwnd = this.cwnd + acked;
            if (cwnd > this.ssthresh)
            {
                this.slowstart = false;
            }
            else
            {
                this.cwnd = Math.Min(cwnd, MaxCwnd);
                return;
            }
        }

        if (this.epochStart == 0)
        {
            this.epochStart = Mics.FastSystem;
            if (this.cwnd < this.lastMaxCwnd)
            {
                this.k = CubicRoot(CubicFactor * (ulong)(this.lastMaxCwnd - this.cwnd));
                this.originPoint = this.lastMaxCwnd;
            }
            else
            {
                this.k = 0;
                this.originPoint = this.cwnd;
            }

            this.tcpCwnd = this.cwnd;
        }

        var t = (ulong)(Mics.FastSystem + this.Connection.MinimumRtt - this.epochStart); // Mics
        t >>= 10; // Milliseconds

        ulong offset;
        double target;
        if (t > this.k)
        {
            offset = t - this.k;
            target = this.originPoint + ((410ul * offset * offset * offset) >> 40);
        }
        else
        {
            offset = this.k - t;
            target = this.originPoint - ((410ul * offset * offset * offset) >> 40);
        }

        if (target > this.cwnd)
        {
            this.increasePerAck = (target - this.cwnd) / this.cwnd;
        }
        else
        {
            this.increasePerAck = 0.01d / this.cwnd;
        }

        if (this.lastMaxCwnd == 0 && this.increasePerAck < 0.05d)
        {
            this.increasePerAck = 0.05d;
        }

        // Tcp friendliness
        this.tcpCwnd += WtcpRatio * acked / this.cwnd;
        if (this.tcpCwnd > this.cwnd)
        {
            var ratio = (this.tcpCwnd - this.cwnd) / this.cwnd;
            if (this.increasePerAck < ratio)
            {
                this.increasePerAck = ratio;
            }
        }

        this.increasePerAck = Math.Min(this.increasePerAck, 0.5);
        // Console.WriteLine($"cwnd {this.cwnd:F2} = cwnd + acked {acked:F2} * increasePerAck {this.increasePerAck:F3}");//
        this.cwnd += acked * this.increasePerAck; // MaxCwnd
        if (this.cwnd > MaxCwnd)
        {
            this.cwnd = MaxCwnd;
        }

        // Console.WriteLine($"cwnd2 {this.cwnd:F2}");
    }

    private void UpdateRegen()
    {
        var r = this.cwnd / this.Connection.SmoothedRtt * 1000;
        this.regen = r;
        this.boost = r * BurstRatio;
        this.boostMicsMax = (long)(this.Connection.SmoothedRtt * BurstRatioInv); // RTT / BurstRatio
    }

    private void ReportDeliverySuccess()
    {
        if (Mics.FastSystem > this.refractoryMics)
        {
            Interlocked.Increment(ref this.deliverySuccess);
        }
    }

    private void ReportDeliveryFailure()
    {
        if (Mics.FastSystem > this.refractoryMics)
        {
            Interlocked.Increment(ref this.deliveryFailure);
        }
    }
}

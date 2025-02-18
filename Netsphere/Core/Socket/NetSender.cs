// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Netsphere.Core;

internal class NetSender
{
    private const int RetryLimit = 3;
    private const int RetryIntervalInMilliseconds = 500;

    internal readonly struct Item
    {
        public Item(IPEndPoint endPoint, BytePool.RentMemory toBeMoved)
        {
            this.EndPoint = endPoint;
            this.MemoryOwner = toBeMoved;
        }

        public readonly IPEndPoint EndPoint;

        public readonly BytePool.RentMemory MemoryOwner;
    }

    public NetSender(NetTerminal netTerminal, NetBase netBase, ILogger<NetSender> logger)
    {
        this.netTerminal = netTerminal;
        this.netBase = netBase;
        this.logger = logger;
        this.netSocketIpv4 = new(this.netTerminal);
        this.netSocketIpv6 = new(this.netTerminal);
    }

    private class SendCore : ThreadCore
    {
        public static void Process(object? parameter)
        {
            var core = (SendCore)parameter!;
            while (!core.IsTerminated)
            {
                var prev = Mics.GetSystem();
                core.sender.Process();

                var mics = NetConstants.SendIntervalMicroseconds - (Mics.GetSystem() - prev);
                if (mics > 0)
                {
                    // core.socket.Logger?.TryGet()?.Log($"Nanosleep: {nano}");
                    // core.TryNanoSleep(nano);
                    core.microSleep.Sleep((int)mics);
                }
            }
        }

        public SendCore(ThreadCoreBase parent, NetSender sender)
                : base(parent, Process, false)
        {
            this.Thread.Priority = ThreadPriority.AboveNormal;
            this.sender = sender;
            // this.timer = MultimediaTimer.TryCreate(NetConstants.SendIntervalMilliseconds, this.sender.Process); // Use multimedia timer if available.
        }

        protected override void Dispose(bool disposing)
        {
            // this.timer?.Dispose();
            base.Dispose(disposing);
        }

        private readonly NetSender sender;
        private readonly MicroSleep microSleep = new();
        // private MultimediaTimer? timer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Send_NotThreadSafe(IPEndPoint? endPoint, BytePool.RentMemory toBeMoved)
    {
        if (endPoint is null)
        {
            return;
        }

        if (NetConstants.LogLowLevelNet)
        {
            // this.logger.TryGet(LogLevel.Debug)?.Log($"{this.netTerminal.NetTerminalString} to {endPoint.ToString()}, {toBeMoved.Span.Length} bytes");
        }

        this.SendCount++;
        if (endPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            this.itemsIpv4.Enqueue(new(endPoint, toBeMoved));
            /*if (this.netTerminal.netSocketIpv4.UnsafeUdpClient is { } client)
            {
                client.Send(data, endPoint);
            }*/
        }
        else
        {
            this.itemsIpv6.Enqueue(new(endPoint, toBeMoved));
            /*if (this.netTerminal.netSocketIpv6.UnsafeUdpClient is { } client)
            {
                client.Send(data, endPoint);
            }*/
        }
    }

    public async Task<bool> StartAsync(ThreadCoreBase parent)
    {
        var port = this.netTerminal.Port;
        int retry;

        retry = 0;
        while (true)
        {
            if (this.netSocketIpv4.Start(parent, port, false))
            {
                break;
            }

            if (retry++ >= RetryLimit)
            {
                this.logger.TryGet(LogLevel.Fatal)?.Log($"Could not create a UDP socket with port number {port}.");
                throw new PanicException();
            }
            else
            {
                this.logger.TryGet(LogLevel.Warning)?.Log($"Retry creating a UDP socket with port number {port}.");
                await Task.Delay(RetryIntervalInMilliseconds);
            }
        }

        retry = 0;
        while (true)
        {
            if (this.netSocketIpv6.Start(parent, port, true))
            {
                break;
            }

            if (retry++ >= RetryLimit)
            {
                this.logger.TryGet(LogLevel.Fatal)?.Log($"Could not create a UDP socket with port number {port}.");
                throw new PanicException();
            }
            else
            {
                this.logger.TryGet(LogLevel.Warning)?.Log($"Retry creating a UDP socket with port number {port}.");
                await Task.Delay(RetryIntervalInMilliseconds);
            }
        }

        this.sendCore ??= new SendCore(parent, this);
        this.sendCore.Start();
        return true;
    }

    public void Stop()
    {
        this.netSocketIpv4.Stop();
        this.netSocketIpv6.Stop();
        this.sendCore?.Dispose();
    }

    public void SetDeliveryFailureRatio(double ratio)
    {
        this.deliveryFailureRatio = ratio;
    }

    #region FieldAndProperty

    public bool CanSend => this.SendCapacity > this.SendCount;

    public int SendCapacity { get; private set; }

    public int SendCount { get; private set; }

    private readonly NetTerminal netTerminal;
    private readonly NetBase netBase;
    private readonly ILogger logger;
    private readonly NetSocket netSocketIpv4;
    private readonly NetSocket netSocketIpv6;
    private SendCore? sendCore;

    private Lock lockObject = new();
    private long previousSystemMics;
    private long previousUpdateMics;
    private Queue<Item> itemsIpv4 = new();
    private Queue<Item> itemsIpv6 = new();
    private double deliveryFailureRatio = 0;

    #endregion

    /*internal void SendImmediately(IPEndPoint endPoint, Span<byte> data)
    {
        if (endPoint.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            if (this.netSocketIpv4.UnsafeUdpClient is { } client)
            {
                client.Send(data, endPoint);
            }
        }
        else
        {
            if (this.netSocketIpv6.UnsafeUdpClient is { } client)
            {
                client.Send(data, endPoint);
            }
        }
    }*/

    private void Process()
    {// Invoked by multiple threads(SendCore or MultimediaTimer).
        // Check interval.
        var currentSystemMics = Mics.UpdateFastSystem();
        if (currentSystemMics > this.previousUpdateMics + Mics.DefaultUpdateIntervalMics)
        {
            this.previousUpdateMics = currentSystemMics;

            Mics.UpdateFastApplication();
            Mics.UpdateFastUtcNow();
            Mics.UpdateFastFixedUtcNow();
            Mics.UpdateFastCorrected();
        }

        var interval = Mics.FromMicroseconds((double)NetConstants.SendIntervalMicroseconds / 2); // Half for margin.
        if (currentSystemMics < (this.previousSystemMics + interval))
        {
            return;
        }

        if (!this.lockObject.TryEnter())
        {
            return;
        }

        try
        {
            this.Prepare();
            this.netTerminal.ProcessSend(this);
            this.Send();

            this.previousSystemMics = currentSystemMics;
        }
        catch
        {
        }
        finally
        {
            this.lockObject.Exit();
        }
    }

    private void Prepare()
    {
        this.SendCapacity = NetConstants.SendCapacityPerRound;
        this.SendCount = 0;
    }

    private void Send()
    {
        if (this.netSocketIpv4.UnsafeUdpClient is { } ipv4)
        {
            while (this.itemsIpv4.TryDequeue(out var item))
            {
#if DEBUG
                if (this.deliveryFailureRatio != 0 && RandomVault.Xoshiro.NextDouble() < this.deliveryFailureRatio)
                {
                    continue;
                }
#endif

                if (NetConstants.LogLowLevelNet)
                {
                    // this.logger.TryGet(LogLevel.Debug)?.Log($"Send actual4 {item.EndPoint.ToString()} {item.MemoryOwner.Span.Length}");
                }

                try
                {
                    ipv4.Send(item.MemoryOwner.Span, item.EndPoint);
                }
                catch
                {
                }
                finally
                {
                    item.MemoryOwner.Return();
                }
            }
        }
        else
        {
            this.itemsIpv4.Clear();
        }

        if (this.netSocketIpv6.UnsafeUdpClient is { } ipv6)
        {
            while (this.itemsIpv6.TryDequeue(out var item))
            {
#if DEBUG
                if (this.deliveryFailureRatio != 0 && RandomVault.Xoshiro.NextDouble() < this.deliveryFailureRatio)
                {
                    continue;
                }
#endif

                if (NetConstants.LogLowLevelNet)
                {
                    // this.logger.TryGet(LogLevel.Debug)?.Log($"Send actual6 {item.EndPoint.ToString()} {item.MemoryOwner.Span.Length}");
                }

                try
                {
                    ipv6.Send(item.MemoryOwner.Span, item.EndPoint);
                }
                catch
                {
                }
                finally
                {
                    item.MemoryOwner.Return();
                }
            }
        }
        else
        {
            this.itemsIpv6.Clear();
        }
    }
}

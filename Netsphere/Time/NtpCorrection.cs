// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Netsphere.Misc;

[TinyhandObject(LockObject = "lockObject", ExplicitKeyOnly = true, UseServiceProvider = true)]
public sealed partial class NtpCorrection : UnitBase, IUnitPreparable
{
    public const string Filename = "NtpCorrection.tinyhand";

    private const int ParallelNumber = 2;
    private const int MaxRoundtripMilliseconds = 1000;
    private readonly string[] hostNames =
    {
        "pool.ntp.org",
        "time.aws.com",
        "time.google.com",
        "time.facebook.com",
        "time.windows.com",
        "ntp.nict.jp",
        "time-a-g.nist.gov",
    };

    [TinyhandObject]
    [ValueLinkObject]
    private partial class Item
    {
        [Link(Type = ChainType.List, Name = "List", Primary = true)]
        public Item()
        {
        }

        public Item(string hostname)
        {
            this.hostname = hostname;
        }

        [IgnoreMember]
        public long RetrievedMics { get; set; }

        [IgnoreMember]
        public long TimeoffsetMilliseconds { get; set; }

        [Link(Type = ChainType.Ordered)]
        [Key(0)]
        private string hostname = string.Empty;

        [Link(Type = ChainType.Ordered, Accessibility = ValueLinkAccessibility.Public)]
        [Key(1)]
        private int roundtripMilliseconds = MaxRoundtripMilliseconds;
    }

    public NtpCorrection(UnitContext context, ILogger<NtpCorrection> logger)
        : base(context)
    {
        this.logger = logger;

        this.ResetHostnames();
    }

    public void Prepare(UnitMessage.Prepare message)
    {
        Time.SetNtpCorrection(this);
    }

    public async Task Correct(CancellationToken cancellationToken)
    {
Retry:
        string[] hostnames;
        using (this.lockObject.EnterScope())
        {
            var current = Mics.GetFixedUtcNow();
            var range = new MicsRange(current - Mics.FromHours(1), current);
            hostnames = this.goshujin.RoundtripMillisecondsChain.Where(x => !range.IsWithin(x.RetrievedMics)).Select(x => x.HostnameValue).Take(ParallelNumber).ToArray();
        }

        if (hostnames.Length == 0)
        {
            return;
        }

        await Parallel.ForEachAsync(hostnames, this.Process).ConfigureAwait(false);
        if (this.timeoffsetCount == 0)
        {
            this.logger?.TryGet(LogLevel.Error)?.Log("Retry");
            goto Retry;
        }
    }

    public async Task<TimeSpan> SendAndReceiveOffset(CancellationToken cancellationToken = default)
    {
        string? hostname;
        using (this.lockObject.EnterScope())
        {
            hostname = this.goshujin.RoundtripMillisecondsChain.First?.HostnameValue;
        }

        if (string.IsNullOrEmpty(hostname))
        {
            return default;
        }

        var packet = await this.SendAndReceivePacket(hostname, cancellationToken).ConfigureAwait(false);
        if (packet is null)
        {
            return default;
        }

        return packet.TimeOffset;
    }

    public async Task CorrectMicsAndUnitLogger(ILogger? logger = default, CancellationToken cancellationToken = default)
    {
        var offset = await this.SendAndReceiveOffset();
        UnitLogger.SetTimeOffset(offset);
        if (this.timeoffsetCount <= 1)
        {
            this.meanTimeoffset = (long)offset.TotalMilliseconds;
            this.timeoffsetCount = 1;
        }

        logger?.TryGet(LogLevel.Information)?.Log($"Corrected: {offset.ToString()}");
    }

    public async Task<bool> CheckConnection(CancellationToken cancellationToken)
    {
        if (this.hostNames.Length == 0)
        {
            return false;
        }

        var hostname = this.hostNames[RandomVault.Xoshiro.NextInt32(this.hostNames.Length)];
        using (var client = new UdpClient())
        {
            try
            {
                client.Connect(hostname, 123);
                var packet = NtpPacket.CreateSendPacket();
                await client.SendAsync(packet.PacketData, cancellationToken).ConfigureAwait(false);
                var result = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    public (long MeanTimeoffset, int TimeoffsetCount) GetTimeOffset()
        => (this.meanTimeoffset, this.timeoffsetCount);

    public bool TryGetCorrectedUtcNow(out DateTime utcNow)
    {
        if (this.timeoffsetCount == 0)
        {
            utcNow = Time.GetFixedUtcNow();
            return false;
        }
        else
        {
            utcNow = Time.GetFixedUtcNow() + TimeSpan.FromMilliseconds(this.meanTimeoffset);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetCorrectedMics(out long mics)
    {
        if (this.timeoffsetCount == 0)
        {
            mics = Mics.GetFixedUtcNow();
            return false;
        }
        else
        {
            mics = Mics.GetFixedUtcNow() + Mics.FromMilliseconds(this.meanTimeoffset);
            return true;
        }
    }

    public void ResetHostnames()
    {
        using (this.lockObject.EnterScope())
        {
            foreach (var x in this.hostNames)
            {
                if (!this.goshujin.HostnameChain.ContainsKey(x))
                {
                    this.goshujin.Add(new Item(x));
                }
            }

            // Reset host
            foreach (var x in this.goshujin)
            {
                x.RetrievedMics = 0;
            }
        }
    }

    private async Task<NtpPacket?> SendAndReceivePacket(string hostname, CancellationToken cancellationToken)
    {
        using (var client = new UdpClient())
        {
            try
            {
                client.Connect(hostname, 123);
                var packet = NtpPacket.CreateSendPacket();
                await client.SendAsync(packet.PacketData, cancellationToken).ConfigureAwait(false);
                var result = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                packet = new NtpPacket(result.Buffer);
                return packet;
            }
            catch
            {
            }
        }

        return default;
    }

    private async ValueTask Process(string hostname, CancellationToken cancellationToken)
    {
        using (var client = new UdpClient())
        {
            try
            {
                client.Connect(hostname, 123);
                var packet = NtpPacket.CreateSendPacket();
                await client.SendAsync(packet.PacketData, cancellationToken).ConfigureAwait(false);
                var result = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                packet = new NtpPacket(result.Buffer);

                this.logger?.TryGet(LogLevel.Debug)?.Log($"{hostname}, RoundtripTime: {(int)packet.RoundtripTime.TotalMilliseconds} ms, TimeOffset: {(int)packet.TimeOffset.TotalMilliseconds} ms");

                using (this.lockObject.EnterScope())
                {
                    var item = this.goshujin.HostnameChain.FindFirst(hostname);
                    if (item != null)
                    {
                        item.RetrievedMics = Mics.GetFixedUtcNow();
                        item.TimeoffsetMilliseconds = (long)packet.TimeOffset.TotalMilliseconds;
                        item.RoundtripMillisecondsValue = (int)packet.RoundtripTime.TotalMilliseconds;
                        this.UpdateTimeoffset();
                    }
                }
            }
            catch
            {
                this.logger?.TryGet(LogLevel.Error)?.Log($"{hostname}");

                using (this.lockObject.EnterScope())
                {
                    var item = this.goshujin.HostnameChain.FindFirst(hostname);
                    if (item != null)
                    {// Remove item
                        item.Goshujin = null;
                    }
                }
            }
        }
    }

    private void UpdateTimeoffset()
    {// using (this.lockObject.EnterScope())
        int count = 0;
        long timeoffset = 0;

        foreach (var x in this.goshujin.Where(x => x.RetrievedMics != 0))
        {
            count++;
            timeoffset += x.TimeoffsetMilliseconds;
        }

        this.timeoffsetCount = count;
        if (count != 0)
        {
            this.meanTimeoffset = timeoffset / count;
        }
        else
        {
            this.meanTimeoffset = 0;
        }
    }

    private ILogger<NtpCorrection>? logger;

    private Lock lockObject = new();

    [Key(0)]
    public long LastCorrectedMics { get; set; }

    [Key(1)]
    private Item.GoshujinClass goshujin = new();

    [Key(2)]
    private int timeoffsetCount;

    [Key(3)]
    private long meanTimeoffset;
}

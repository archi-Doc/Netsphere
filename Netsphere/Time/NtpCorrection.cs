// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace Netsphere.Misc;

/// <summary>
/// Queries NTP servers and maintains the clock correction offset.
/// </summary>
[TinyhandObject(LockMemberName = "lockObject", ExplicitKeysOnly = true, UseServiceProvider = true)]
public sealed partial class NtpCorrection
{
    public const string Filename = "NtpCorrection.tinyhand";

    private const int ParallelNumber = 2;
    private const int MaxRoundtripMilliseconds = 1000;
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(1);
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

        [Link(Type = ChainType.Ordered, GenerateValue = true)]
        [Key(0)]
        private string hostname = string.Empty;

        [Link(Type = ChainType.Ordered, Accessibility = ValueLinkAccessibility.Public, GenerateValue = true)]
        [Key(1)]
        private int roundtripMilliseconds = MaxRoundtripMilliseconds;
    }

    #region FieldAndProperty

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

    private bool setNtpCorrection;

    #endregion

    public NtpCorrection(ILogger<NtpCorrection> logger)
    {
        this.logger = logger;
    }

    [TinyhandOnDeserialized]
    public void OnDeserialized()
    {
        this.AddHostnames();
    }

    public async Task Correct(CancellationToken cancellationToken)
    {
        using (this.lockObject.EnterScope())
        {
            if (this.goshujin.Count == 0)
            {// Failed hosts are removed; restore the list (once per call, so that retries still end) so that a run of network failures does not stop correction permanently.
                this.AddHostnamesInternal();
            }
        }

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

        try
        {
            await Parallel.ForEachAsync(hostnames, cancellationToken, this.Process).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (this.timeoffsetCount == 0)
        {
            this.logger?.GetWriter(LogLevel.Error)?.Write("Retry");
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

        var packet = await ExchangePacket(hostname, cancellationToken).ConfigureAwait(false);
        if (packet is null)
        {
            return default;
        }

        return packet.TimeOffset;
    }

    public async Task CorrectMicsAndUnitLogger(ILogger? logger = default, CancellationToken cancellationToken = default)
    {
        string? hostname;
        using (this.lockObject.EnterScope())
        {
            hostname = this.goshujin.RoundtripMillisecondsChain.First?.HostnameValue;
        }

        // SendAndReceiveOffset() returns zero on failure, which must not be applied as a measured offset.
        var packet = string.IsNullOrEmpty(hostname) ? null : await ExchangePacket(hostname, cancellationToken).ConfigureAwait(false);
        if (packet is null)
        {
            logger?.GetWriter(LogLevel.Warning)?.Write("Correction failed");
            return;
        }

        var offset = packet.TimeOffset;
        LogUnit.SetTimestampOffset(offset);
        using (this.lockObject.EnterScope())
        {
            if (this.timeoffsetCount <= 1)
            {
                this.meanTimeoffset = (long)offset.TotalMilliseconds;
                this.timeoffsetCount = 1;
                this.SetNtpCorrection();
            }
        }

        logger?.GetWriter(LogLevel.Information)?.Write($"Corrected: {offset.ToString()}");
    }

    public async Task<bool> CheckConnection(CancellationToken cancellationToken)
    {
        if (this.hostNames.Length == 0)
        {
            return false;
        }

        var hostname = this.hostNames[RandomVault.Xoshiro.NextInt32(this.hostNames.Length)];
        return await ExchangePacket(hostname, cancellationToken).ConfigureAwait(false) is not null;
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

    public void AddHostnames()
    {
        using (this.lockObject.EnterScope())
        {
            this.AddHostnamesInternal();
        }
    }

    private static async Task<NtpPacket?> ExchangePacket(string hostname, CancellationToken cancellationToken)
    {// Cancels the receive itself on timeout; WaitAsync would leave it pending until the socket is disposed.
        using var client = new UdpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReceiveTimeout);
        try
        {
            client.Connect(hostname, 123);
            var packet = NtpPacket.CreateSendPacket();
            await client.SendAsync(packet.PacketData, timeout.Token).ConfigureAwait(false);
            var result = await client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
            return new NtpPacket(result.Buffer);
        }
        catch
        {
            return default;
        }
    }

    private void AddHostnamesInternal()
    {// using (this.lockObject.EnterScope())
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

    private async ValueTask Process(string hostname, CancellationToken cancellationToken)
    {
        var packet = await ExchangePacket(hostname, cancellationToken).ConfigureAwait(false);
        if (packet is null)
        {
            if (cancellationToken.IsCancellationRequested)
            {// Shutting down: the host did not fail.
                return;
            }

            this.logger?.GetWriter(LogLevel.Error)?.Write($"{hostname}");
            using (this.lockObject.EnterScope())
            {
                var item = this.goshujin.HostnameChain.FindFirst(hostname);
                if (item != null)
                {// Remove item
                    item.Goshujin = null;
                }
            }

            return;
        }

        this.logger?.GetWriter(LogLevel.Debug)?.Write($"{hostname}, RoundtripTime: {(int)packet.RoundtripTime.TotalMilliseconds} ms, TimeOffset: {(int)packet.TimeOffset.TotalMilliseconds} ms");

        using (this.lockObject.EnterScope())
        {
            var item = this.goshujin.HostnameChain.FindFirst(hostname);
            if (item != null)
            {
                item.RetrievedMics = Mics.GetFixedUtcNow();
                item.TimeoffsetMilliseconds = (long)packet.TimeOffset.TotalMilliseconds;
                item.RoundtripMillisecondsValue = (int)packet.RoundtripTime.TotalMilliseconds;
                this.UpdateTimeoffset();

                this.logger?.GetWriter(LogLevel.Information)?.Write($"{hostname} {item.RoundtripMillisecondsValue}ms");
            }
        }
    }

    private void SetNtpCorrection()
    {// using (this.lockObject.EnterScope())
        if (!this.setNtpCorrection)
        {
            this.setNtpCorrection = true;
            Time.SetNtpCorrection(this);
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
            this.SetNtpCorrection();
        }
        else
        {
            this.meanTimeoffset = 0;
        }
    }
}

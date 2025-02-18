// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Misc;

public class NtpPacket
{
    private const long CompensatingRate32 = 0x100000000L;
    private const double CompensatingRate16 = 0x10000d;
    private static readonly DateTime CompensatingDateTime = new DateTime(1900, 1, 1);
    private static readonly DateTime PassedCompensatingDateTime = CompensatingDateTime.AddSeconds(uint.MaxValue);

    public byte[] PacketData { get; private set; }

    public DateTime PacketCreatedTime { get; private set; }

    public static NtpPacket CreateSendPacket()
    {
        var packet = new byte[48];
        var time = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(DateTimeToNtpTimeStamp(Time.GetFixedUtcNow())));

        packet[0] = 0x1B;
        Array.Copy(time, 0, packet, 40, 8);
        return new NtpPacket(packet);
    }

    public NtpPacket(byte[] packetData)
    {
        this.PacketData = packetData;
        this.PacketCreatedTime = Time.GetFixedUtcNow();
    }

    private static DateTime GetCompensatingDatetime(uint seconds)
        => (seconds & 0x80000000) == 0 ? PassedCompensatingDateTime : CompensatingDateTime;

    private static DateTime GetCompensatingDatetime(DateTime dateTime)
        => dateTime >= PassedCompensatingDateTime ? PassedCompensatingDateTime : CompensatingDateTime;

    private static double SignedFixedPointToDouble(int signedFixedPoint)
    {
        var number = (short)(signedFixedPoint >> 16);
        var fraction = (ushort)(signedFixedPoint & short.MaxValue);
        return number + (fraction / CompensatingRate16);
    }

    private static DateTime NtpTimeStampToDateTime(long ntpTimeStamp)
    {
        var seconds = (uint)(ntpTimeStamp >> 32);
        var secondsFraction = (uint)(ntpTimeStamp & uint.MaxValue);
        var milliseconds = ((long)seconds * 1000) + ((long)secondsFraction * 1000 / CompensatingRate32);
        return GetCompensatingDatetime(seconds) + TimeSpan.FromMilliseconds(milliseconds);
    }

    private static long DateTimeToNtpTimeStamp(DateTime dateTime)
    {
        var compensatingDatetime = GetCompensatingDatetime(dateTime);
        var ntpStandardTick = (dateTime - compensatingDatetime).TotalMilliseconds;

        var seconds = (uint)(dateTime - compensatingDatetime).TotalSeconds;
        var secondsFraction = (uint)((ntpStandardTick % 1000) * CompensatingRate32 / 1000);
        return (long)((ulong)seconds << 32 | secondsFraction);
    }

    public int LeapIndicator
        => this.PacketData[0] >> 6 & 0x03;

    public int Version
        => this.PacketData[0] >> 3 & 0x03;

    public int Mode
        => this.PacketData[0] & 0x03;

    public int Stratum
        => this.PacketData[1];

    public int PollInterval => (sbyte)this.PacketData[2] switch
    {
        0 => 0,
        1 => 1,
        var interval => (int)Math.Pow(2, interval),
    };

    public double Precision
        => Math.Pow(2, (sbyte)this.PacketData[3]);

    public double RootDelay
    => SignedFixedPointToDouble(IPAddress.NetworkToHostOrder(BitConverter.ToInt32(this.PacketData, 4)));

    public double RootDispersion
    => SignedFixedPointToDouble(IPAddress.NetworkToHostOrder(BitConverter.ToInt32(this.PacketData, 8)));

    public DateTime ReferenceTimestamp
        => NtpTimeStampToDateTime(IPAddress.NetworkToHostOrder(BitConverter.ToInt64(this.PacketData, 16)));

    public DateTime OriginateTimestamp // t0
        => NtpTimeStampToDateTime(IPAddress.NetworkToHostOrder(BitConverter.ToInt64(this.PacketData, 24)));

    public DateTime ReceiveTimestamp // t1
        => NtpTimeStampToDateTime(IPAddress.NetworkToHostOrder(BitConverter.ToInt64(this.PacketData, 32)));

    public DateTime TransmitTimestamp // t2
        => NtpTimeStampToDateTime(IPAddress.NetworkToHostOrder(BitConverter.ToInt64(this.PacketData, 40)));

    public TimeSpan TimeOffset // ((t1 - t0) + (t2 - t3)) / 2
        => new TimeSpan((this.ReceiveTimestamp - this.OriginateTimestamp + (this.TransmitTimestamp - this.PacketCreatedTime)).Ticks / 2);

    public TimeSpan RoundtripTime // t3 - t0 - (t2 - t1)
        => this.PacketCreatedTime - this.OriginateTimestamp - (this.TransmitTimestamp - this.ReceiveTimestamp);

    public DateTime CorrectedUtcNow => Time.GetFixedUtcNow() + this.TimeOffset;
}

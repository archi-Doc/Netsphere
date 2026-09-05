// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Buffers.Binary;
using Netsphere.Misc;
using Xunit;

namespace xUnitTest;

public class NtpPacketTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(47)]
    public void RejectsTruncatedPackets(int length)
        => Assert.Throws<ArgumentException>(() => new NtpPacket(new byte[length]));

    [Fact]
    public void RejectsNullPackets()
        => Assert.Throws<ArgumentNullException>(() => new NtpPacket(null!));

    [Theory]
    [InlineData(0, 4, 4)]
    [InlineData(3, 7, 7)]
    [InlineData(1, 3, 3)]
    public void ReadsAllHeaderBits(int leap, int version, int mode)
    {
        var data = new byte[48];
        data[0] = (byte)((leap << 6) | (version << 3) | mode);
        var packet = new NtpPacket(data);
        Assert.Equal(leap, packet.LeapIndicator);
        Assert.Equal(version, packet.Version);
        Assert.Equal(mode, packet.Mode);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(6, 64)]
    public void PollIntervalIsPowerOfTwo(byte exponent, int seconds)
    {
        var data = new byte[48];
        data[2] = exponent;
        Assert.Equal(seconds, new NtpPacket(data).PollInterval);
    }

    [Theory]
    [InlineData(0x00008000, 0.5)]
    [InlineData(0x0001FFFF, 1.9999847412109375)]
    [InlineData(-32768, -0.5)]
    public void RootDelayPreservesFraction(int encoded, double expected)
    {
        var data = new byte[48];
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), encoded);
        Assert.Equal(expected, new NtpPacket(data).RootDelay);
    }

    [Fact]
    public void RootDispersionIsUnsigned()
    {
        var data = new byte[48];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 0x80008000);
        Assert.Equal(32768.5, new NtpPacket(data).RootDispersion);
    }

    [Theory]
    [InlineData(uint.MaxValue, 2036, 2, 7, 6, 28, 15)]
    [InlineData(0u, 2036, 2, 7, 6, 28, 16)]
    [InlineData(1u, 2036, 2, 7, 6, 28, 17)]
    public void TimestampHandlesEraRollover(uint seconds, int year, int month, int day, int hour, int minute, int second)
    {
        var data = new byte[48];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(40), seconds);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(44), 0x80000000);
        var timestamp = new NtpPacket(data).TransmitTimestamp;
        Assert.Equal(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc).AddMilliseconds(500), timestamp);
        Assert.Equal(DateTimeKind.Utc, timestamp.Kind);
    }

    [Fact]
    public void SendPacketContainsCurrentTransmitTimestamp()
    {
        var packet = NtpPacket.CreateSendPacket();
        Assert.Equal(48, packet.PacketData.Length);
        Assert.Equal(3, packet.Mode);
        Assert.Equal(3, packet.Version);
        Assert.InRange(Math.Abs((packet.TransmitTimestamp - packet.PacketCreatedTime).TotalSeconds), 0, 1);
    }
}

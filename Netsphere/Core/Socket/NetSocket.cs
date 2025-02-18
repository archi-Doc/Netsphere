// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net.Sockets;
using Netsphere.Packet;

namespace Netsphere.Core;

/// <summary>
/// NetSocket provides low-level network service.
/// </summary>
public sealed class NetSocket
{
    private const int ReceiveTimeout = 100;
    private const int SendBufferSize = 1 * 1024 * 1024;
    private const int ReceiveBufferSize = 4 * 1024 * 1024;

    private class RecvCore : ThreadCore
    {
        public static async void Process(object? parameter)
        {
            var core = (RecvCore)parameter!;

            IPEndPoint anyEP;
            if (core.socket.UnsafeUdpClient?.Client.AddressFamily == AddressFamily.InterNetwork)
            {
                anyEP = new IPEndPoint(IPAddress.Any, 0); // IPEndPoint.MinPort
            }
            else
            {
                anyEP = new IPEndPoint(IPAddress.IPv6Any, 0); // IPEndPoint.MinPort
            }

            BytePool.RentArray? rentArray = null;
            while (!core.IsTerminated)
            {
                var udp = core.socket.UnsafeUdpClient;
                if (udp == null)
                {
                    break;
                }

                try
                {// nspi 10^5
                    var remoteEP = (EndPoint)anyEP;
                    rentArray ??= PacketPool.Rent();
                    var received = udp.Client.ReceiveFrom(rentArray.Array, 0, rentArray.Array.Length, SocketFlags.None, ref remoteEP);
                    // var vt = await udp.Client.ReceiveFromAsync(arrayOwner.ByteArray.AsMemory(), SocketFlags.None, remoteEP, core.CancellationToken);
                    if (NetConstants.LogLowLevelNet)
                    {
                        // core.socket.netTerminal.UnitLogger.Get<NetSocket>(LogLevel.Debug)?.Log($"Receive actual {received}");
                    }

                    if (received > PacketHeader.Length &&
                        received <= NetConstants.MaxPacketLength)
                    {// nspi
                        core.socket.netTerminal.ProcessReceive((IPEndPoint)remoteEP, rentArray, received);
                        if (rentArray.Count > 1)
                        {// Byte array is used by multiple owners. Return and rent a new one next time.
                            rentArray = rentArray.Return();
                        }
                    }
                }
                catch
                {
                }
            }
        }

        public RecvCore(ThreadCoreBase parent, NetSocket socket)
                : base(parent, Process, false)
        {
            // this.Thread.Priority = ThreadPriority.AboveNormal;
            this.socket = socket;
        }

        private NetSocket socket;
    }

    public NetSocket(NetTerminal netTerminal)
    {
        this.netTerminal = netTerminal;
    }

    #region FieldAndProperty

#pragma warning disable SA1401 // Fields should be private
    internal UdpClient? UnsafeUdpClient;
#pragma warning restore SA1401 // Fields should be private

    private readonly NetTerminal netTerminal;
    private RecvCore? recvCore;

    #endregion

    public bool Start(ThreadCoreBase parent, int port, bool ipv6)
    {
        this.recvCore ??= new RecvCore(parent, this);

        try
        {
            this.PrepareUdpClient(port, ipv6);
        }
        catch
        {
            return false;
        }

        this.recvCore.Start();

        return true;
    }

    public void Stop()
    {
        this.recvCore?.Dispose();

        try
        {
            if (this.UnsafeUdpClient != null)
            {
                this.UnsafeUdpClient.Dispose();
                this.UnsafeUdpClient = null;
            }
        }
        catch
        {
        }
    }

    private void PrepareUdpClient(int port, bool ipv6)
    {
        var addressFamily = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        var udp = new UdpClient(port, addressFamily);
        try
        {
            const int SIO_UDP_CONNRESET = -1744830452;
            udp.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
        }
        catch
        {
        }

        udp.Client.SendBufferSize = SendBufferSize;
        udp.Client.ReceiveBufferSize = ReceiveBufferSize;
        udp.Client.ReceiveTimeout = ReceiveTimeout;

        try
        {
            if (this.UnsafeUdpClient != null)
            {
                this.UnsafeUdpClient.Dispose();
                this.UnsafeUdpClient = null;
            }
        }
        catch
        {
        }

        this.UnsafeUdpClient = udp;
    }
}

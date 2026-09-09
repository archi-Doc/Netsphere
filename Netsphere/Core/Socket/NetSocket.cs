// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Versioning;
using Netsphere.Packet;

namespace Netsphere.Core;

/// <summary>
/// Owns a UDP socket and its receive loop for one address family.
/// </summary>
public sealed class NetSocket
{
    private const int ReceiveTimeout = 100;
    private const int SendBufferSize = 1 * 1024 * 1024;
    private const int ReceiveBufferSize = 4 * 1024 * 1024;

    private class RecvCore : ThreadCore
    {
        public static void Process(object? parameter)
        {
            var core = (RecvCore)parameter!;

            IPEndPoint anyEP;
            if (core.addressFamily == AddressFamily.InterNetwork)
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
                var udp = core.udp;

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

                    if (received < PacketHeader.Length &&
                        remoteEP is IPEndPoint endpoint)
                    {
                        var address = new NetAddress(endpoint.Address, (ushort)endpoint.Port);
                        if (address.IsPrivateOrLocalLoopbackAddress())
                        {// Healthcheck
                            udp.Client.SendTo(rentArray.AsSpan(0, received), endpoint);
                        }
                    }
                    else if (received <= NetConstants.MaxPacketLength)
                    {
                        core.socket.netTerminal.ProcessReceive((IPEndPoint)remoteEP, rentArray, received);
                    }
                }
                catch
                {
                }
                finally
                {
                    if (rentArray is { Count: > 1 })
                    {// Byte array is used by multiple owners. Return and rent a new one next time.
                        rentArray = rentArray.Return();
                    }
                }
            }

            rentArray?.Return();
        }

        public RecvCore(ExecutionGroup parent, NetSocket socket, UdpClient udp)
                : base(parent, Process, ExecutionCoreOptions.DelayedStart)
        {
            // this.Thread.Priority = ThreadPriority.AboveNormal;
            this.Thread.IsBackground = true;
            this.socket = socket;
            this.udp = udp;
            this.addressFamily = udp.Client.AddressFamily;
        }

        private readonly NetSocket socket;
        private readonly UdpClient udp;
        private readonly AddressFamily addressFamily;
    }

    public NetSocket(NetTerminal netTerminal)
    {
        this.netTerminal = netTerminal;
    }

    #region FieldAndProperty

#pragma warning disable SA1401 // Fields should be private
    internal volatile UdpClient? UnsafeUdpClient;
#pragma warning restore SA1401 // Fields should be private

    private readonly NetTerminal netTerminal;
    private readonly Lock lifecycleLock = new();
    private RecvCore? recvCore;

    #endregion

    /// <summary>
    /// Starts a stopped socket with a new receive loop. Concurrent starts allow only one caller to succeed.
    /// </summary>
    /// <param name="parent">The execution group that owns the receive loop.</param>
    /// <param name="port">The UDP port, or zero to select an available port.</param>
    /// <param name="ipv6">Whether to use IPv6.</param>
    /// <param name="boundPort">The assigned port on success.</param>
    /// <returns>Whether binding and starting succeeded; false if already running.</returns>
    public bool Start(ExecutionGroup parent, int port, bool ipv6, out int boundPort)
    {
        using var scope = this.lifecycleLock.EnterScope();
        boundPort = 0;
        if (this.recvCore is not null)
        {
            return false;
        }

        try
        {
            this.PrepareUdpClient(port, ipv6);
            boundPort = ((IPEndPoint)this.UnsafeUdpClient!.Client.LocalEndPoint!).Port;
            this.recvCore = new RecvCore(parent, this, this.UnsafeUdpClient);
        }
        catch
        {
            this.UnsafeUdpClient?.Dispose();
            this.UnsafeUdpClient = null;
            return false;
        }

        this.recvCore.SendSignal(ExecutionSignal.Start);

        return true;
    }

    /// <summary>
    /// Closes the socket and releases its receive loop. Repeated calls are safe.
    /// </summary>
    public void Stop()
    {
        using var scope = this.lifecycleLock.EnterScope();
        var core = this.recvCore;
        this.recvCore = null;

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

        core?.Dispose();
    }

    private void PrepareUdpClient(int port, bool ipv6)
    {
        var addressFamily = ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;
        var udp = new UdpClient(addressFamily);
        try
        {
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            IPEndPoint endpoint;
            if (ipv6 &&
                !this.netTerminal.NetBase.NetOptions.EnableTemporaryIpv6Address &&
                OperatingSystem.IsWindows() &&
                NetHelper.TryGetStaticIpv6Address(out var ipv6Address))
            {
                endpoint = new IPEndPoint(ipv6Address, port);
            }
            else
            {
                endpoint = new IPEndPoint(ipv6 ? IPAddress.IPv6Any : IPAddress.Any, port);
            }

            udp.Client.Bind(endpoint);

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
                this.UnsafeUdpClient?.Dispose();
            }
            catch
            {
            }

            this.UnsafeUdpClient = udp;
        }
        catch
        {
            udp.Dispose();
            throw;
        }
    }
}

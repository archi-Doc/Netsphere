// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Threading;
using Netsphere.Core;
using Netsphere.Crypto;
using Netsphere.Internal;
using Netsphere.Packet;

namespace Netsphere;

[ValueLinkObject(Isolation = IsolationLevel.Serializable, Restricted = true)]
public sealed partial class ClientConnection : Connection, IClientConnectionInternal, IEquatable<ClientConnection>, IComparable<ClientConnection>
{
    [Link(Primary = true, Type = ChainType.Unordered, TargetMember = "ConnectionId")]
    [Link(Type = ChainType.Unordered, Name = "DestinationEndpoint", TargetMember = "DestinationEndpoint")]
    internal ClientConnection(PacketTerminal packetTerminal, ConnectionTerminal connectionTerminal, ulong connectionId, NetNode node, NetEndpoint endPoint)
        : base(packetTerminal, connectionTerminal, connectionId, node, endPoint)
    {
        this.context = this.NetBase.NewClientConnectionContext(this);
    }

    internal ClientConnection(ServerConnection serverConnection)
        : base(serverConnection)
    {
        this.context = this.NetBase.NewClientConnectionContext(this);
        this.BidirectionalConnection = serverConnection;
    }

    #region FieldAndProperty

    public override bool IsClient => true;

    public override bool IsServer => false;

    public ServerConnection? BidirectionalConnection { get; internal set; } // lock (this.ConnectionTerminal.serverConnections.SyncObject)

    public CancellationToken CancellationToken => this.cts.Token;

    private CancellationTokenSource cts = new();

    private int openCount;

    private ClientConnectionContext context;

    #endregion

    public override void Dispose()
    {
        if (this.DecrementOpenCount() <= 0)
        {
            base.Dispose();
        }
    }

    public ClientConnectionContext GetContext()
        => this.context;

    public TContext GetContext<TContext>()
        where TContext : ClientConnectionContext
        => (TContext)this.context;

    public TService GetService<TService>()
        where TService : INetService
        => StaticNetService.CreateClient<TService>(this);

    public async Task<NetResult> Send<TSend>(TSend data, ulong dataId = 0, CancellationToken cancellationToken = default)
    {
        if (!this.IsActive)
        {
            return NetResult.Closed;
        }

        if (!NetHelper.TrySerialize(data, out var rentMemory))
        {
            return NetResult.SerializationFailed;
        }

        var timeout = this.NetBase.DefaultTransmissionTimeout;
        using (var transmissionAndTimeout = await this.TryCreateSendTransmission(timeout, cancellationToken).ConfigureAwait(false))
        {
            if (transmissionAndTimeout.Transmission is null)
            {
                rentMemory.Return();
                return NetResult.NoTransmission;
            }

            var tcs = new TaskCompletionSource<NetResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = transmissionAndTimeout.Transmission.SendBlock(0, dataId, rentMemory, tcs);
            rentMemory.Return();
            if (result != NetResult.Success)
            {
                return result;
            }

            try
            {
                result = await tcs.Task.WaitAsync(transmissionAndTimeout.Timeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return NetResult.Timeout;
            }
            catch
            {
                return NetResult.Canceled;
            }

            return result;
        }
    }

    public async Task<NetResultValue<TReceive>> SendAndReceive<TSend, TReceive>(TSend data, ulong dataId = 0, CancellationToken cancellationToken = default)
    {
        if (!this.IsActive)
        {
            return new(NetResult.Closed);
        }

        dataId = dataId != 0 ? dataId : NetHelper.GetDataId<TSend, TReceive>();
        if (!NetHelper.TrySerialize(data, out var rentMemory))
        {
            return new(NetResult.SerializationFailed);
        }

        NetResponse response;
        var timeout = this.NetBase.DefaultTransmissionTimeout;
        using (var transmissionAndTimeout = await this.TryCreateSendTransmission(timeout, cancellationToken).ConfigureAwait(false))
        {
            if (transmissionAndTimeout.Transmission is null)
            {
                rentMemory.Return();
                return new(NetResult.NoTransmission);
            }

            var result = transmissionAndTimeout.Transmission.SendBlock(0, dataId, rentMemory, default);
            rentMemory.Return();
            if (result != NetResult.Success)
            {
                return new(result);
            }

            var tcs = new TaskCompletionSource<NetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var receiveTransmission = this.TryCreateReceiveTransmission(transmissionAndTimeout.Transmission.TransmissionId, tcs))
            {
                if (receiveTransmission is null)
                {
                    return new(NetResult.NoTransmission);
                }

                try
                {
                    response = await tcs.Task.WaitAsync(transmissionAndTimeout.Timeout).ConfigureAwait(false);
                    if (response.IsFailure)
                    {
                        return new(response.Result);
                    }
                }
                catch (TimeoutException)
                {
                    return new(NetResult.Timeout);
                }
                catch
                {
                    return new(NetResult.Canceled);
                }
            }
        }

        if (typeof(TReceive) == typeof(NetResult))
        {// In the current implementation, the value of NetResult is assigned to DataId.
            response.Return();
            var netResult = (NetResult)response.DataId;
            return new(NetResult.Success, Unsafe.As<NetResult, TReceive>(ref netResult));
        }

        if (response.Received.Memory.Length == 0)
        {
            response.Return();
            return new((NetResult)response.DataId);
        }

        if (!NetHelper.TryDeserialize<TReceive>(response.Received, out var receive))
        {
            response.Return();
            return new(NetResult.DeserializationFailed);
        }

        response.Return();
        return new(NetResult.Success, receive);
    }

    /*public async Task<(NetResult Result, SendStream? Stream)> SendBlockAndStream<TSend>(TSend data, long maxLength, ulong dataId = 0)
    {
        if (!NetHelper.TrySerializeWithLength(data, out var rentMemory))
        {
            return (NetResult.SerializationFailed, default);
        }

        if (rentMemory.Length > this.Agreement.MaxBlockSize)
        {
            return (NetResult.BlockSizeLimit, default);
        }

        try
        {
            var (result, stream) = await this.SendStream(rentMemory.Length + maxLength, dataId).ConfigureAwait(false);
            if (result != NetResult.Success || stream is null)
            {
                return (result, default);
            }

            result = await stream.Send(rentMemory.Memory);
            if (result != NetResult.Success)
            {
                return (result, default);
            }

            return (result, stream);
        }
        finally
        {
            rentMemory.Return();
        }
    }*/

    /*public async Task<(NetResult Result, SendStream? Stream)> SendStream(long maxLength, ulong dataId = 0)
    {
        if (!this.IsActive)
        {
            return (NetResult.Closed, default);
        }

        if (!this.Agreement.CheckStreamLength(maxLength))
        {
            return (NetResult.StreamLengthLimit, default);
        }

        var timeout = this.NetBase.DefaultSendTimeout;
        var transmissionAndTimeout = await this.TryCreateSendTransmission(timeout).ConfigureAwait(false);
        if (transmissionAndTimeout.Transmission is null)
        {
            return (NetResult.NoTransmission, default);
        }

        var tcs = new TaskCompletionSource<NetResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var result = transmissionAndTimeout.Transmission.SendStream(maxLength, tcs);
        if (result != NetResult.Success)
        {
            transmissionAndTimeout.Transmission.Dispose();
            return (result, default);
        }

        return (NetResult.Success, new SendStream(transmissionAndTimeout.Transmission, maxLength, dataId));
    }*/

    public (NetResult Result, SendStream? Stream) SendStream(long maxLength, ulong dataId = 0)
    {
        var r = this.PrepareSendStream(maxLength);
        if (r.SendTransmission is null)
        {
            return (r.Result, default);
        }

        return new(NetResult.Success, new SendStream(r.SendTransmission, maxLength, dataId));
    }

    public (NetResult Result, SendStreamAndReceive<TReceive>? Stream) SendStreamAndReceive<TReceive>(long maxLength, ulong dataId = 0)
    {
        var r = this.PrepareSendStream(maxLength);
        if (r.SendTransmission is null)
        {
            return (r.Result, default);
        }

        return new(NetResult.Success, new SendStreamAndReceive<TReceive>(r.SendTransmission, maxLength, dataId));
    }

    public async Task<(NetResult Result, SendStream? Stream)> SendBlockAndStream<TSend>(TSend data, long maxLength, ulong dataId = 0)
    {
        if (!NetHelper.TrySerializeWithLength(data, out var rentMemory))
        {
            return (NetResult.SerializationFailed, default);
        }

        if (rentMemory.Length > this.Agreement.MaxBlockSize)
        {
            return (NetResult.BlockSizeLimit, default);
        }

        try
        {
            var (result, stream) = this.SendStream(rentMemory.Length + maxLength, dataId);
            if (result != NetResult.Success || stream is null)
            {
                return (result, default);
            }

            result = await stream.Send(rentMemory.Memory).ConfigureAwait(false);
            if (result != NetResult.Success)
            {
                return (result, default);
            }

            return (result, stream);
        }
        finally
        {
            rentMemory.Return();
        }
    }

    public async Task<(NetResult Result, SendStreamAndReceive<TReceive>? Stream)> SendBlockAndStreamAndReceive<TSend, TReceive>(TSend data, long maxLength, ulong dataId = 0)
    {
        if (!NetHelper.TrySerializeWithLength(data, out var rentMemory))
        {
            return (NetResult.SerializationFailed, default);
        }

        if (rentMemory.Length > this.Agreement.MaxBlockSize)
        {
            return (NetResult.BlockSizeLimit, default);
        }

        try
        {
            var (result, stream) = this.SendStreamAndReceive<TReceive>(rentMemory.Length + maxLength, dataId);
            if (result != NetResult.Success || stream is null)
            {
                return (result, default);
            }

            result = await stream.Send(rentMemory.Memory).ConfigureAwait(false);
            if (result != NetResult.Success)
            {
                return (result, default);
            }

            return (result, stream);
        }
        finally
        {
            rentMemory.Return();
        }
    }

    public async Task<(NetResult Result, ReceiveStream? Stream)> SendAndReceiveStream<TSend>(TSend packet, ulong dataId = 0, CancellationToken cancellationToken = default)
    {
        if (!this.IsActive)
        {
            return (NetResult.Closed, default);
        }

        if (!NetHelper.TrySerialize(packet, out var rentMemory))
        {
            return (NetResult.SerializationFailed, default);
        }

        NetResponse response;
        ReceiveTransmission? receiveTransmission;
        var timeout = this.NetBase.DefaultTransmissionTimeout;
        using (var transmissionAndTimeout = await this.TryCreateSendTransmission(timeout, cancellationToken).ConfigureAwait(false))
        {
            if (transmissionAndTimeout.Transmission is null)
            {
                rentMemory.Return();
                return (NetResult.NoTransmission, default);
            }

            var result = transmissionAndTimeout.Transmission.SendBlock(0, dataId, rentMemory, default);
            rentMemory.Return();
            if (result != NetResult.Success)
            {
                return (result, default);
            }

            var tcs = new TaskCompletionSource<NetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            receiveTransmission = this.TryCreateReceiveTransmission(transmissionAndTimeout.Transmission.TransmissionId, tcs);
            if (receiveTransmission is null)
            {
                return (NetResult.NoTransmission, default);
            }

            try
            {
                response = await tcs.Task.WaitAsync(transmissionAndTimeout.Timeout).ConfigureAwait(false);
                if (response.IsFailure || !response.Received.IsEmpty)
                {// Failure or not stream.
                    receiveTransmission.Dispose();
                    return new(response.Result, default);
                }
            }
            catch (TimeoutException)
            {
                receiveTransmission.Dispose();
                return (NetResult.Timeout, default);
            }
            catch
            {
                receiveTransmission.Dispose();
                return (NetResult.Canceled, default);
            }
        }

        if (response.Additional == 0)
        {// No stream
            return ((NetResult)response.DataId, default);
        }

        var stream = new ReceiveStream(receiveTransmission, response.DataId, response.Additional);
        return new(NetResult.Success, stream);
    }

    /*public async Task<NetResult> UpdateAgreement(CertificateToken<ConnectionAgreement> token)
    {
        var r = await this.SendAndReceive<CertificateToken<ConnectionAgreement>, bool>(token, ConnectionAgreement.UpdateId).ConfigureAwait(false);
        if (r.Result == NetResult.Success && r.Value)
        {
            this.Agreement.AcceptAll(token.Target);
            this.ApplyAgreement();
        }

        return r.Result;
    }

    public async Task<NetResult> ConnectBidirectionally(CertificateToken<ConnectionAgreement>? token)
    {
        this.PrepareBidirectionalConnection(); // Create the ServerConnection in advance, as packets may not arrive in order.

        var r = await this.SendAndReceive<CertificateToken<ConnectionAgreement>?, bool>(token, ConnectionAgreement.BidirectionalId).ConfigureAwait(false);
        if (r.Result == NetResult.Success)
        {
            if (r.Value)
            {
                this.Agreement.EnableBidirectionalConnection = true;
                return NetResult.Success;
            }
            else
            {
                return NetResult.NotAuthorized;
            }
        }

        return r.Result;
    }*/

    public async Task<NetResult> SetAuthenticationToken(AuthenticationToken token)
    {
        if (token.Equals(this.context.AuthenticationToken))
        {// Identical token
            return NetResult.Success;
        }

        var r = await this.SendAndReceive<AuthenticationToken, NetResult>(token, ConnectionAgreement.AuthenticationTokenId).ConfigureAwait(false);
        if (r.Result == NetResult.Success)
        {
            this.context.AuthenticationToken = token;
            return r.Value;
        }

        return r.Result;
    }

    async Task<(NetResult Result, ulong DataId, BytePool.RentMemory Value)> IClientConnectionInternal.RpcSendAndReceive(BytePool.RentMemory data, ulong dataId)
    {
        if (!this.IsActive)
        {
            return new(NetResult.Closed, 0, default);
        }

        NetResponse response;
        var timeout = this.NetBase.DefaultTransmissionTimeout;
        using (var transmissionAndTimeout = await this.TryCreateSendTransmission(timeout, default).ConfigureAwait(false))
        {
            if (transmissionAndTimeout.Transmission is null)
            {
                return new(NetResult.NoTransmission, 0, default);
            }

            var result = transmissionAndTimeout.Transmission.SendBlock(1, dataId, data, default);
            if (result != NetResult.Success)
            {
                return new(result, 0, default);
            }

            var tcs = new TaskCompletionSource<NetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var receiveTransmission = this.TryCreateReceiveTransmission(transmissionAndTimeout.Transmission.TransmissionId, tcs))
            {
                if (receiveTransmission is null)
                {
                    return new(NetResult.NoTransmission, 0, default);
                }

                try
                {
                    response = await tcs.Task.WaitAsync(transmissionAndTimeout.Timeout).ConfigureAwait(false);
                    if (response.IsFailure)
                    {
                        return new(response.Result, 0, default);
                    }
                }
                catch (TimeoutException)
                {
                    return new(NetResult.Timeout, 0, default);
                }
                catch
                {
                    return new(NetResult.Canceled, 0, default);
                }
            }
        }

        return new(NetResult.Success, response.DataId, response.Received);
    }

    async Task<(NetResult Result, ReceiveStream? Stream)> IClientConnectionInternal.RpcSendAndReceiveStream(BytePool.RentMemory data, ulong dataId)
    {
        if (!this.IsActive)
        {
            return (NetResult.Closed, default);
        }

        NetResponse response;
        ReceiveTransmission? receiveTransmission;
        var timeout = this.NetBase.DefaultTransmissionTimeout;
        using (var transmissionAndTimeout = await this.TryCreateSendTransmission(timeout, default).ConfigureAwait(false))
        {
            if (transmissionAndTimeout.Transmission is null)
            {
                return (NetResult.NoTransmission, default);
            }

            var tcs = new TaskCompletionSource<NetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            receiveTransmission = this.TryCreateReceiveTransmission(transmissionAndTimeout.Transmission.TransmissionId, tcs);
            if (receiveTransmission is null)
            {
                return (NetResult.NoTransmission, default);
            }

            var result = transmissionAndTimeout.Transmission.SendBlock(1, dataId, data, default);
            if (result != NetResult.Success)
            {
                receiveTransmission.Dispose();
                return (result, default);
            }

            try
            {
                response = await tcs.Task.WaitAsync(transmissionAndTimeout.Timeout).ConfigureAwait(false);
                if (response.IsFailure || !response.Received.IsEmpty)
                {// Failure or not stream.
                    receiveTransmission.Dispose();
                    return new(response.Result, default);
                }
            }
            catch (TimeoutException)
            {
                receiveTransmission.Dispose();
                return (NetResult.Timeout, default);
            }
            catch
            {
                receiveTransmission.Dispose();
                return (NetResult.Canceled, default);
            }
        }

        /*if (response.Additional == 0)
        {// No stream
            return ((NetResult)response.DataId, default);
        }*/

        var stream = new ReceiveStream(receiveTransmission, response.DataId, response.Additional);
        return new(NetResult.Success, stream);
    }

    async Task<ServiceResponse<NetResult>> IClientConnectionInternal.UpdateAgreement(ulong dataId, CertificateToken<ConnectionAgreement> a1)
    {
        if (!NetHelper.TrySerialize(a1, out var rentMemory))
        {
            return new(NetResult.SerializationFailed, NetResult.SerializationFailed);
        }

        var response = await ((IClientConnectionInternal)this).RpcSendAndReceive(rentMemory, dataId).ConfigureAwait(false);
        rentMemory.Return();

        try
        {
            if (response.Result != NetResult.Success)
            {
                return new(response.Result, response.Result);
            }

            NetHelper.DeserializeNetResult(response.DataId, response.Value.Memory.Span, out var result);
            if (result == NetResult.Success)
            {
                this.Agreement.AcceptAll(a1.Target);
                // this.ApplyAgreement();
            }

            return new(result, result);
        }
        finally
        {
            response.Value.Return();
        }
    }

    async Task<ServiceResponse<NetResult>> IClientConnectionInternal.ConnectBidirectionally(ulong dataId, CertificateToken<ConnectionAgreement>? a1)
    {
        if (!NetHelper.TrySerialize(a1, out var rentMemory))
        {
            return new(NetResult.SerializationFailed, NetResult.SerializationFailed);
        }

        this.PrepareBidirectionalConnection(); // Create the ServerConnection in advance, as packets may not arrive in order.
        var response = await ((IClientConnectionInternal)this).RpcSendAndReceive(rentMemory, dataId).ConfigureAwait(false);
        rentMemory.Return();

        try
        {
            if (response.Result != NetResult.Success)
            {
                return new(response.Result, response.Result);
            }

            NetHelper.DeserializeNetResult(response.DataId, response.Value.Memory.Span, out var result);
            if (result == NetResult.Success)
            {
                this.Agreement.EnableBidirectionalConnection = true;
                if (a1 is not null)
                {
                    this.Agreement.AcceptAll(a1.Target);
                    // this.ApplyAgreement();
                }
            }

            return new(result, result);
        }
        finally
        {
            response.Value.Return();
        }
    }

    public ServerConnection PrepareBidirectionalConnection()
    {
        if (this.BidirectionalConnection is { } connection)
        {
            return connection;
        }
        else
        {
            return this.ConnectionTerminal.PrepareBidirectionalConnection(this);
        }
    }

    public bool Equals(ClientConnection? other)
    {
        if (other is null)
        {
            return false;
        }

        return this.ConnectionId == other.ConnectionId;
    }

    public override int GetHashCode()
        => (int)this.ConnectionId;

    public int CompareTo(ClientConnection? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (this.ConnectionId < other.ConnectionId)
        {
            return -1;
        }
        else if (this.ConnectionId > other.ConnectionId)
        {
            return 1;
        }
        else
        {
            return 0;
        }
    }

    public void Close()
        => this.Dispose();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void IncrementOpenCount()
    {
        Interlocked.Increment(ref this.openCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int DecrementOpenCount()
    {
        return Interlocked.Decrement(ref this.openCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetOpenCount(int count)
    {
        Volatile.Write(ref this.openCount, count);
    }

    internal override void OnStateChanged()
    {
        if (this.CurrentState == State.Open)
        {// Reopen
            this.cts.Dispose();
            this.cts = new();
        }
        else if (this.CurrentState == State.Closed)
        {// Close
            this.cts.Cancel();
        }
        else
        {// Disposed
            this.cts.Dispose();
        }
    }

    private (NetResult Result, SendTransmission? SendTransmission) PrepareSendStream(long maxLength)
    {
        if (!this.IsActive)
        {
            return (NetResult.Closed, default);
        }

        if (!this.Agreement.CheckStreamLength(maxLength))
        {
            return new(NetResult.StreamLengthLimit, default);
        }

        var transmission = this.TryCreateSendTransmission();
        if (transmission is null)
        {
            return new(NetResult.NoTransmission, default);
        }

        var result = transmission.SendStream(maxLength);
        if (result != NetResult.Success)
        {
            transmission.Dispose();
            return new(result, default);
        }

        return new(NetResult.Success, transmission);
    }
}

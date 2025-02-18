// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Netsphere.Core;
using Netsphere.Crypto;

#pragma warning disable SA1401

namespace Netsphere;

public interface ITransmissionContextInternal
{
    // ReceiveStream GetReceiveStream();
}

public sealed class TransmissionContext : ITransmissionContextInternal
{
    public static TransmissionContext Current => AsyncLocal.Value!;

    internal static AsyncLocal<TransmissionContext?> AsyncLocal = new();

    internal TransmissionContext(ServerConnection serverConnection, uint transmissionId, uint dataKind, ulong dataId, BytePool.RentMemory toBeShared)
    {
        this.ServerConnection = serverConnection;
        this.TransmissionId = transmissionId;
        this.DataKind = dataKind;
        this.DataId = dataId;
        this.RentMemory = toBeShared;
    }

    #region FieldAndProperty

    public bool IsAuthenticated
        => this.ServerConnection.GetContext().AuthenticationToken is not null;

    public ServerConnection ServerConnection { get; } // => this.ConnectionContext.ServerConnection;

    public uint TransmissionId { get; }

    public uint DataKind { get; } // 0:Block, 1:RPC, 2:Control

    public ulong DataId { get; }

    public BytePool.RentMemory RentMemory { get; set; }

    public NetResult Result { get; set; }

    public bool IsSent { get; private set; }

    private ReceiveStream? receiveStream;

    private SendStream? sendStream;

    #endregion

    /*public bool TryGetAuthenticationToken([MaybeNullWhen(false)] out AuthenticationToken authenticationToken)
        => this.ServerConnection.GetContext().TryGetAuthenticationToken(out authenticationToken);*/

    public bool AuthenticationTokenEquals(SignaturePublicKey publicKey)
        => this.ServerConnection.GetContext().AuthenticationToken is { } t &&
        t.PublicKey.Equals(publicKey);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Return()
    {
        this.RentMemory = this.RentMemory.Return();
    }

    public NetResult SendAndForget<TSend>(TSend data, ulong dataId = 0)
    {
        if (!this.ServerConnection.IsActive)
        {
            return NetResult.Closed;
        }
        else if (this.IsSent)
        {
            return NetResult.InvalidOperation;
        }

        if (typeof(TSend) == typeof(NetResult))
        {
            return this.SendAndForget(BytePool.RentMemory.Empty, Unsafe.As<TSend, ulong>(ref data));
        }

        if (!NetHelper.TrySerialize(data, out var rentMemory))
        {
            return NetResult.SerializationFailed;
        }

        var transmission = this.ServerConnection.TryCreateSendTransmission(this.TransmissionId);
        if (transmission is null)
        {
            rentMemory.Return();
            return NetResult.NoTransmission;
        }

        this.IsSent = true;
        var result = transmission.SendBlock(0, dataId, rentMemory, default);
        rentMemory.Return();
        return result; // SendTransmission is automatically disposed either upon completion of transmission or in case of an Ack timeout.
    }

    public ReceiveStream GetReceiveStream()
        => this.receiveStream ?? throw new InvalidOperationException();

    public ReceiveStream<TResponse> GetReceiveStream<TResponse>()
        => new ReceiveStream<TResponse>(this, this.GetReceiveStream());

    public (NetResult Result, SendStream? Stream) GetSendStream(long maxLength, ulong dataId = 0)
    {
        if (this.sendStream is not null)
        {
            if (this.sendStream.RemainingLength < maxLength)
            {// Insufficient length.
                return (NetResult.InvalidOperation, default);
            }

            return (NetResult.Success, this.sendStream);
        }

        if (!this.ServerConnection.IsActive)
        {
            return (NetResult.Canceled, default);
        }
        else if (!this.ServerConnection.Agreement.CheckStreamLength(maxLength))
        {
            return (NetResult.StreamLengthLimit, default);
        }
        else if (this.IsSent)
        {
            return (NetResult.InvalidOperation, default);
        }

        var sendTransmission = this.ServerConnection.TryCreateSendTransmission(this.TransmissionId);
        if (sendTransmission is null)
        {
            return (NetResult.NoTransmission, default);
        }

        this.IsSent = true;
        var result = sendTransmission.SendStream(maxLength);
        if (result != NetResult.Success)
        {
            sendTransmission.Dispose();
            return (result, default);
        }

        this.sendStream = new SendStream(sendTransmission, maxLength, dataId);
        return (NetResult.Success, this.sendStream);
    }

    /*public async NetTask<NetResult> InternalUpdateAgreement(ulong dataId, CertificateToken<ConnectionAgreement> a1)
    {
        if (!NetHelper.TrySerialize(a1, out var rentMemory))
        {
            return NetResult.SerializationFailed;
        }

        var response = await this.RpcSendAndReceive(rentMemory, dataId).ConfigureAwait(false);
        rentMemory.Return();

        try
        {
            if (response.Result != NetResult.Success)
            {
                return response.Result;
            }

            if (!NetHelper.TryDeserializeNetResult(response.Value.Memory.Span, out var result))
            {
                return NetResult.DeserializationFailed;
            }

            if (result == NetResult.Success)
            {
                this.Agreement.AcceptAll(a1.Target);
                this.ApplyAgreement();
            }

            return result;
        }
        finally
        {
            response.Value.Return();
        }
    }

    public async NetTask<NetResult> InternalConnectBidirectionally(ulong dataId, CertificateToken<ConnectionAgreement>? a1)
    {
        if (!NetHelper.TrySerialize(a1, out var rentMemory))
        {
            return NetResult.SerializationFailed;
        }

        this.PrepareBidirectionally(); // Create the ServerConnection in advance, as packets may not arrive in order.
        var response = await this.RpcSendAndReceive(rentMemory, dataId).ConfigureAwait(false);
        rentMemory.Return();

        try
        {
            if (response.Result != NetResult.Success)
            {
                return response.Result;
            }

            if (!NetHelper.TryDeserializeNetResult(response.Value.Memory.Span, out var result))
            {
                return NetResult.DeserializationFailed;
            }

            if (result == NetResult.Success)
            {
                this.Agreement.EnableBidirectionalConnection = true;
            }

            return result;
        }
        finally
        {
            response.Value.Return();
        }
    }*/

    /*public (NetResult Result, ReceiveStream? Stream) ReceiveStream(long maxLength)
    {
        if (this.Connection.CancellationToken.IsCancellationRequested)
        {
            return (NetResult.Canceled, default);
        }
        else if (!this.Connection.Agreement.CheckStreamLength(maxLength))
        {
            return (NetResult.StreamLengthLimit, default);
        }
        else if (this.receiveTransmission is null)
        {
            return (NetResult.InvalidOperation, default);
        }

        var stream = new ReceiveStream(this.receiveTransmission, this.DataId, maxLength);
        this.receiveTransmission = default;
        return (NetResult.Success, stream);
    }*/

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal NetResult SendResultAndForget(NetResult result)
        => this.SendAndForget(BytePool.RentMemory.Empty, (ulong)result);

    internal NetResult SendAndForget(BytePool.RentMemory toBeShared, ulong dataId = 0)
    {
        if (!this.ServerConnection.IsActive)
        {
            return NetResult.Closed;
        }
        else if (this.IsSent)
        {
            return NetResult.InvalidOperation;
        }

        var transmission = this.ServerConnection.TryCreateSendTransmission(this.TransmissionId);
        if (transmission is null)
        {
            return NetResult.NoTransmission;
        }

        this.IsSent = true;
        var result = transmission.SendBlock(0, dataId, toBeShared, default);
        return result; // SendTransmission is automatically disposed either upon completion of transmission or in case of an Ack timeout.
    }

    internal bool CreateReceiveStream(ReceiveTransmission receiveTransmission, long maxLength)
    {
        if (!this.ServerConnection.IsActive)
        {
            return false;
        }
        else if (!this.ServerConnection.Agreement.CheckStreamLength(maxLength))
        {
            return false;
        }
        else if (this.receiveStream is not null)
        {
            return false;
        }

        this.receiveStream = new ReceiveStream(receiveTransmission, this.DataId, maxLength);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void CheckReceiveStream()
    {
        if (this.receiveStream is { } stream &&
            stream.ReceiveTransmission.Mode != NetTransmissionMode.Disposed)
        {// Not completed
            this.Result = NetResult.NotReceived;
        }
    }

    internal void ReturnAndDisposeStream()
    {
        this.Return();

        if (this.receiveStream is not null)
        {
            this.receiveStream.DisposeImmediately();
            this.receiveStream = default;
        }

        if (this.sendStream is not null)
        {
            this.sendStream.Dispose(false);
            this.sendStream = default;
        }
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Netsphere.Core;
using Netsphere.Crypto;

#pragma warning disable SA1401

namespace Netsphere;

/// <summary>
/// Marks a transmission context for internal transport integration.
/// </summary>
public interface ITransmissionContextInternal
{
    // ReceiveStream GetReceiveStream();
}

/// <summary>
/// Provides request data, connection state, and response operations for one transmission.
/// </summary>
public sealed class TransmissionContext : ITransmissionContextInternal
{
    public static TransmissionContext Current => AsyncLocal.Value!;

    internal static AsyncLocal<TransmissionContext?> AsyncLocal = new();

    internal TransmissionContext(ServerConnection serverConnection, uint transmissionId, uint dataKind, ulong dataId, BytePool.RentedMemory toBeShared)
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

    /// <summary>
    /// Gets or sets the owned request or response buffer. Return the previous lease before replacing it.
    /// </summary>
    public BytePool.RentedMemory RentMemory { get; set; }

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

    /// <summary>
    /// Copies a response, reusing the request buffer when it has exclusive ownership and sufficient space.
    /// </summary>
    /// <param name="response">The response bytes, which may overlap the request buffer.</param>
    /// <remarks>Call from the request handler before sending. Do not access this context concurrently.</remarks>
    public void SetResponseMemory(ReadOnlyMemory<byte> response)
    {
        if (response.IsEmpty)
        {
            this.Return();
        }
        else if (this.RentMemory.Owner is { ReferenceCount: 1 } && response.Length <= this.RentMemory.Length)
        {
            response.Span.CopyTo(this.RentMemory.Span);
            this.RentMemory = this.RentMemory.Slice(0, response.Length);
        }
        else
        {
            var owner = BytePool.Default.Rent(response.Length).AsMemory(0, response.Length);
            response.CopyTo(owner.Memory);
            this.Return();
            this.RentMemory = owner;
        }
    }

    /// <summary>
    /// Sets a response, retaining the request lease when the response aliases its buffer.
    /// </summary>
    /// <param name="response">The borrowed request lease (or a slice), or an owned lease for a different buffer.</param>
    /// <remarks>
    /// A response with the same owner is borrowed, regardless of other references to the buffer.
    /// To transfer a separately acquired reference to the same buffer, use <see cref="SetResponseOwnedRentMemory"/>.
    /// Call from the request handler before sending. Do not access this context concurrently.
    /// </remarks>
    public void SetResponseRentMemory(BytePool.RentedMemory response)
    {
        var request = this.RentMemory;
        if (ReferenceEquals(request.Owner, response.Owner))
        {// The handler returned the borrowed request lease itself. Adopt the returned range instead of releasing the array.
            this.RentMemory = response;
            return;
        }

        this.SetResponseOwnedRentMemory(response);
    }

    /// <summary>
    /// Transfers an owned response lease to this context, releasing the previous request lease.
    /// </summary>
    /// <param name="response">An independently owned lease, even when it shares the request buffer.</param>
    /// <remarks>
    /// The caller relinquishes this reference and must not return it after this call.
    /// Do not pass the borrowed request lease; use <see cref="SetResponseRentMemory"/> for that case.
    /// Call from the request handler before sending. Do not access this context concurrently.
    /// </remarks>
    public void SetResponseOwnedRentMemory(BytePool.RentedMemory response)
    {
        this.Return();
        this.RentMemory = response;
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
            return this.SendAndForget(BytePool.RentedMemory.Empty, (ulong)Unsafe.As<TSend, NetResult>(ref data));
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
        => this.SendAndForget(BytePool.RentedMemory.Empty, (ulong)result);

    internal NetResult SendAndForget(BytePool.RentedMemory toBeShared, ulong dataId = 0)
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

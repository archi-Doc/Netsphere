// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Netsphere.Core;

#pragma warning disable SA1202 // Elements should be ordered by access

public abstract class SendStreamBase
{
    internal SendStreamBase(SendTransmission sendTransmission, long maxLength, ulong dataId)
    {
        this.SendTransmission = sendTransmission;
        this.RemainingLength = maxLength;
        this.DataId = dataId;
    }

    internal SendTransmission SendTransmission { get; }

    public ulong DataId { get; protected set; }

    public long RemainingLength { get; internal set; }

    public long SentLength { get; internal set; }

    internal void Dispose(bool disposeTransmission)
    {
        if (this.SendTransmission.Mode == NetTransmissionMode.Stream)
        {
            this.SendTransmission.TrySendControl(this, DataControl.Cancel); // Stream -> StreamCompleted
        }

        if (disposeTransmission)
        {
            this.SendTransmission.Dispose();
        }
        else
        {// Delay the disposal of SendTransmission until the transmission is complete.
        }
    }

    public Task<NetResult> Cancel(CancellationToken cancellationToken = default)
        => this.SendInternal(DataControl.Cancel, ReadOnlyMemory<byte>.Empty, cancellationToken);

    internal async Task<NetResult> SendInternal(DataControl dataControl, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var result = await this.SendTransmission.ProcessSend(this, dataControl, buffer, cancellationToken).ConfigureAwait(false);
        if (result.IsError())
        {// Error
            this.Dispose(true);
        }

        return result;
    }

    public Task<NetResult> Send(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => this.SendInternal(DataControl.Valid, buffer, cancellationToken);

    public async Task<NetResult> SendBlock<TSend>(TSend data, CancellationToken cancellationToken = default)
    {
        if (!NetHelper.TrySerializeWithLength(data, out var rentMemory))
        {
            return NetResult.SerializationFailed;
        }

        if (rentMemory.Length > this.SendTransmission.Connection.Agreement.MaxBlockSize)
        {
            return NetResult.BlockSizeLimit;
        }

        NetResult result;
        try
        {
            result = await this.Send(rentMemory.Memory, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            rentMemory.Return();
        }

        return result;
    }

    /*protected async Task<NetResult> SendControl(DataControl dataControl, CancellationToken cancellationToken)
    {
        if (this.SendTransmission.Mode != NetTransmissionMode.Stream)
        {
            return NetResult.InvalidOperation;
        }

        var result = await this.SendTransmission.ProcessSend(this, dataControl, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        return result;
    }*/

    protected async Task<NetResultValue<TReceive>> InternalComplete<TReceive>(CancellationToken cancellationToken)
    {
        var result = await this.SendTransmission.ProcessSend(this, DataControl.Complete, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
        if (result.IsError())
        {// Error
            this.Dispose(true);
            return new(result);
        }

        // Stream -> StreamCompleted

        try
        {
            var connection = this.SendTransmission.Connection;
            if (connection.IsServer)
            {// On the server side, it does not receive completion of the stream since ReceiveTransmission is already consumed.
                result = NetResult.Success;
                if (this.SendTransmission.SentTcs is { } sentTcs)
                {
                    result = await this.SendTransmission.Wait(sentTcs.Task, -1, cancellationToken).ConfigureAwait(false);
                }

                return new(result);
            }

            NetResponse response;
            var tcs = new TaskCompletionSource<NetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var receiveTransmission = connection.TryCreateReceiveTransmission(this.SendTransmission.TransmissionId, tcs))
            {
                if (receiveTransmission is null)
                {
                    return new(NetResult.NoTransmission);
                }

                try
                {
                    response = await receiveTransmission.Wait(tcs.Task, -1, cancellationToken).ConfigureAwait(false);
                    if (response.IsFailure)
                    {
                        return new(response.Result);
                    }
                }
                catch
                {
                    return new(NetResult.Canceled);
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
        finally
        {
            this.SendTransmission.Dispose();
        }
    }
}

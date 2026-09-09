// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Netsphere.Core;

#pragma warning disable SA1202 // Elements should be ordered by access

/// <summary>
/// Provides shared state and operations for sending a length-limited stream.
/// </summary>
/// <remarks>Concurrent sends are serialized. Await each send when their order matters. Complete the stream once.</remarks>
public abstract class SendStreamBase
{
    internal SendStreamBase(SendTransmission sendTransmission, long maxLength, ulong dataId)
    {
        this.SendTransmission = sendTransmission;
        this.RemainingLength = maxLength;
        this.DataId = dataId;
        this.sentTask = sendTransmission.SentTcs?.Task;
        if (!sendTransmission.Connection.IsServer)
        {
            this.responseSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            this.receiveTransmission = sendTransmission.Connection.TryCreateReceiveTransmission(sendTransmission.TransmissionId, this.responseSource);
        }
    }

    internal SendTransmission SendTransmission { get; }

    public ulong DataId { get; protected set; }

    public long RemainingLength { get; internal set; }

    public long SentLength { get; internal set; }

    private readonly Task<NetResult>? sentTask;
    private readonly TaskCompletionSource<NetResponse>? responseSource;
    private readonly ReceiveTransmission? receiveTransmission;
    private int completionStarted;

    internal void Dispose(bool disposeTransmission)
    {
        if (!disposeTransmission && this.SendTransmission.Mode == NetTransmissionMode.Stream)
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

        this.ReleaseResponse();
    }

    public Task<NetResult> Cancel(CancellationToken cancellationToken = default)
        => this.SendInternal(DataControl.Cancel, ReadOnlyMemory<byte>.Empty, cancellationToken);

    internal async Task<NetResult> SendInternal(DataControl dataControl, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (this.responseSource is not null && this.receiveTransmission is null)
        {
            this.Dispose(true);
            return NetResult.NoTransmission;
        }

        var result = await this.SendTransmission.ProcessSend(this, dataControl, buffer, cancellationToken).ConfigureAwait(false);
        if (result.IsError())
        {// Error
            this.Dispose(true);
        }
        else if (dataControl == DataControl.Cancel)
        {
            this.ReleaseResponse();
        }

        return result;
    }

    /// <summary>
    /// Sends a chunk within the stream's remaining length.
    /// </summary>
    /// <param name="buffer">The source bytes. Keep them unchanged until the returned task completes.</param>
    /// <param name="cancellationToken">Stops the local send operation.</param>
    /// <returns>The transport result for this chunk.</returns>
    public Task<NetResult> Send(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => this.SendInternal(DataControl.Valid, buffer, cancellationToken);

    public async Task<NetResult> SendBlock<TSend>(TSend data, CancellationToken cancellationToken = default)
    {
        if (!NetHelper.TrySerializeWithLength(data, out var rentMemory))
        {
            return NetResult.SerializationFailed;
        }

        NetResult result;
        try
        {
            if (rentMemory.Length - sizeof(int) > this.SendTransmission.Connection.Agreement.MaxBlockSize)
            {
                return NetResult.BlockSizeLimit;
            }

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

    protected async Task<NetResultAndValue<TReceive>> InternalComplete<TReceive>(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref this.completionStarted, 1) != 0)
        {
            return new(NetResult.InvalidOperation);
        }

        var responseClaimed = false;

        // Stream -> StreamCompleted

        try
        {
            var result = this.sentTask is { IsCompletedSuccessfully: true } && this.sentTask.Result == NetResult.Success
                ? NetResult.Success
                : await this.SendTransmission.ProcessSend(this, DataControl.Complete, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
            if (result.IsError())
            {// Error
                return new(result);
            }

            var connection = this.SendTransmission.Connection;
            if (connection.IsServer)
            {// On the server side, it does not receive completion of the stream since ReceiveTransmission is already consumed.
                result = NetResult.Success;
                if (this.sentTask is { } sentTask)
                {
                    result = sentTask.IsCompletedSuccessfully ? sentTask.Result :
                        await this.SendTransmission.Wait(sentTask, -1, cancellationToken).ConfigureAwait(false);
                }

                return new(result);
            }

            NetResponse response;
            var tcs = this.responseSource!;
            using (var receiveTransmission = this.receiveTransmission)
            {
                if (receiveTransmission is null)
                {
                    return new(NetResult.NoTransmission);
                }

                try
                {
                    responseClaimed = true;
                    response = await receiveTransmission.Wait(tcs.Task, -1, cancellationToken).ConfigureAwait(false);
                    if (response.IsFailure)
                    {
                        response.Return();
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
            this.receiveTransmission?.Dispose();
            if (!responseClaimed && this.responseSource is { } source)
            {
                ReturnUnclaimedResponse(source.Task);
            }
        }
    }

    private static void ReturnUnclaimedResponse(Task<NetResponse> task)
        => _ = task.ContinueWith(static completed => completed.Result.Return(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

    private void ReleaseResponse()
    {
        this.receiveTransmission?.Dispose();
        if (Interlocked.Exchange(ref this.completionStarted, 1) == 0 && this.responseSource is { } source)
        {
            ReturnUnclaimedResponse(source.Task);
        }
    }
}

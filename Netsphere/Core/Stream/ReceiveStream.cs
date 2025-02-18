// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Netsphere.Core;

namespace Netsphere;

#pragma warning disable SA1202 // Elements should be ordered by access

public interface IReceiveStreamInternal
{
    Task<NetResultValue<TReceive>> ReceiveBlock<TReceive>(CancellationToken cancellationToken = default);
}

public readonly struct ReceiveStream<TResponse>
{
    internal ReceiveStream(TransmissionContext transmissionContext, ReceiveStream receiveStream)
    {
        this.transmissionContext = transmissionContext;
        this.receiveStream = receiveStream;
    }

    public ulong DataId => this.receiveStream.DataId;

    public long MaxStreamLength => this.receiveStream.MaxStreamLength;

    public long ReceivedLength => this.receiveStream.ReceivedLength;

    private readonly TransmissionContext transmissionContext;
    private readonly ReceiveStream receiveStream;

    public Task<(NetResult Result, int Written)> Receive(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => this.receiveStream.Receive(buffer, cancellationToken);

    public NetResult SendAndDispose(TResponse data)
    {
        if (this.receiveStream.ReceiveTransmission.Mode != NetTransmissionMode.Disposed)
        {
            return NetResult.InvalidOperation;
        }

        var result = this.transmissionContext.SendAndForget(data, this.transmissionContext.DataId);
        this.receiveStream.Dispose();
        return result;
    }
}

public class ReceiveStream : IReceiveStreamInternal // , IDisposable
{
    internal ReceiveStream(ReceiveTransmission receiveTransmission, ulong dataId, long maxStreamLength)
    {
        this.ReceiveTransmission = receiveTransmission;
        this.DataId = dataId;
        this.MaxStreamLength = maxStreamLength;
    }

    #region FieldAndProperty

    internal ReceiveTransmission ReceiveTransmission { get; }

    public ulong DataId { get; }

    public long MaxStreamLength { get; internal set; }

    public long ReceivedLength { get; internal set; }

    internal int CurrentGene { get; set; }

    internal int CurrentPosition { get; set; }

    #endregion

    internal void Dispose()
    {
        this.DisposeImmediately();
    }

    public async Task<(NetResult Result, int Written)> Receive(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var r = await this.ReceiveTransmission.ProcessReceive(this, buffer, cancellationToken).ConfigureAwait(false);
        if (r.Result != NetResult.Success)
        {
            if (r.Result == NetResult.Completed ||
                r.Result == NetResult.Canceled)
            {
                Debug.Assert(this.ReceiveTransmission.Mode == NetTransmissionMode.Disposed);
            }
            else
            {
                this.Dispose();
            }
        }

        return r;
    }

    async Task<NetResultValue<TReceive>> IReceiveStreamInternal.ReceiveBlock<TReceive>(CancellationToken cancellationToken)
    {
        var rentArray = BytePool.Default.Rent(TinyhandSerializer.InitialBufferSize);
        try
        {
            var (result, written) = await this.Receive(rentArray.AsMemory(0, sizeof(int)).Memory, cancellationToken).ConfigureAwait(false);
            if (result != NetResult.Success)
            {
                return new(result);
            }
            else if (written != sizeof(int))
            {
                this.Dispose();
                return new(NetResult.DeserializationFailed);
            }

            var length = BitConverter.ToInt32(rentArray.AsSpan());
            if (length > this.ReceiveTransmission.Connection.Agreement.MaxBlockSize)
            {
                this.Dispose();
                return new(NetResult.BlockSizeLimit);
            }

            var memory = rentArray.AsMemory(sizeof(int));
            if (memory.Length > length)
            {
                memory = memory.Slice(0, length);
            }
            else
            {
                rentArray.Return();
                rentArray = BytePool.Default.Rent(length);
                memory = rentArray.AsMemory(0, length);
            }

            (result, written) = await this.Receive(memory.Memory, cancellationToken).ConfigureAwait(false);
            if (result.IsError())
            {
                return new(result);
            }
            else if (written != length)
            {
                this.Dispose();
                return new(NetResult.DeserializationFailed);
            }

            if (!TinyhandSerializer.TryDeserialize<TReceive>(memory.Span, out var value))
            {
                this.Dispose();
                return new(NetResult.DeserializationFailed);
            }

            this.MaxStreamLength -= this.ReceivedLength;
            this.ReceivedLength = 0;
            return new(NetResult.Success, value);
        }
        finally
        {
            rentArray.Return();
        }
    }

    internal void DisposeImmediately()
    {
        this.ReceiveTransmission.ProcessDispose();
    }
}

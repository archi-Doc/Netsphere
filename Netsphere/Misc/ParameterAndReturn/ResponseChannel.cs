// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Tinyhand.IO;
using Tinyhand.Tree;
using static FastExpressionCompiler.ImTools.SmallMap;

namespace Netsphere;

public delegate void ResponseDelegate<TReceive>(NetResult result, TReceive? value);

public interface IResponseChannelInternal
{
    void Invoke(NetResult result);

    void Invoke(NetResponse response);

    // internal void SetSendTransmission(SendTransmission sendTransmission);
}

/// <summary>
/// A shared structure used to send and receive data between the client and server.<br/>
/// On the client side, a delegate is invoked when data is received.<br/>
/// Its advantage is that it does not rely on Task, but its use is not recommended.<br/>
/// <br/>
/// Client side: Set SendValue and call the NetService method.<br/>
/// Server side: Read SendValue and set ReceiveValue.<br/>
/// If asynchronous processing is required, do not use ResponseChannel; define a normal NetService method instead.
/// </summary>
/// <typeparam name="TResponse"></typeparam>
[TinyhandObject]
public partial record struct ResponseChannel<TResponse> : IResponseChannelInternal, ITinyhandSerializable<ResponseChannel<TResponse>>, ITinyhandReconstructable<ResponseChannel<TResponse>>, ITinyhandCloneable<ResponseChannel<TResponse>>, ITinyhandSingleLayoutSerializable
{
    // public readonly TResponse? Value;
    // public readonly bool IsValueSet;

    /// <summary>
    /// Gets the value returned by the server to the client.
    /// </summary>
    public TResponse? Value { get; private set; }

    /// <summary>
    /// Gets the callback invoked on the client when a response is received.
    /// </summary>
    // public ResponseDelegate<TResponse>? ResponseDelegate { get; private set; }
    public readonly ResponseDelegate<TResponse>? ResponseDelegate;

    [MemberNotNull(nameof(Value))]
    public bool IsValueSet { get; private set; }

    public ResponseChannel(ResponseDelegate<TResponse>? receiveDelegate)
    {
        this.ResponseDelegate = receiveDelegate;
    }

    public ResponseChannel()
    {
    }

    private ResponseChannel(TResponse? value, bool isValueSet, ResponseDelegate<TResponse>? responseDelegate)
    {
        this.Value = value;
        this.IsValueSet = isValueSet;
        this.ResponseDelegate = responseDelegate;
    }

    public unsafe void SetResponse(TResponse value)
    {
        this.Value = value;
        this.IsValueSet = true;
    }

    void IResponseChannelInternal.Invoke(NetResponse response)
    {
        if (response.Result != NetResult.Success ||
            response.Received.IsEmpty)
        {
            this.ResponseDelegate?.Invoke(response.Result, default);
            return;
        }

        if (response.DataId != 0)
        {
            this.ResponseDelegate?.Invoke((NetResult)response.DataId, default);
            return;
        }

        var span = response.Received.Span;
        TResponse? receiveValue = default;
        try
        {
            var reader = new TinyhandReader(span);
            if (reader.TryReadNil())
            {
                this.ResponseDelegate?.Invoke(NetResult.DeserializationFailed, default);
                return;
            }

            receiveValue = TinyhandSerializer.Deserialize<TResponse>(ref reader, TinyhandSerializerOptions.Standard);
        }
        catch
        {
            this.ResponseDelegate?.Invoke(NetResult.DeserializationFailed, default);
            return;
        }

        this.ResponseDelegate?.Invoke(response.Result, receiveValue);
    }

    void IResponseChannelInternal.Invoke(NetResult result)
    {
        this.ResponseDelegate?.Invoke(result, default);
    }

    static void ITinyhandSerializable<ResponseChannel<TResponse>>.Serialize(ref TinyhandWriter writer, scoped ref ResponseChannel<TResponse> v, TinyhandSerializerOptions options)
    {
        if (!v.IsValueSet)
        {
            writer.WriteNil();
            return;
        }

        TinyhandSerializer.Serialize<TResponse>(ref writer, v.Value!, options);
    }

    static unsafe void ITinyhandSerializable<ResponseChannel<TResponse>>.Deserialize(ref TinyhandReader reader, scoped ref ResponseChannel<TResponse> v, TinyhandSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return;
        }

        v.Value = TinyhandSerializer.Deserialize<TResponse>(ref reader, options);
    }

    static void ITinyhandReconstructable<ResponseChannel<TResponse>>.Reconstruct([NotNull] scoped ref ResponseChannel<TResponse> v, TinyhandSerializerOptions options)
    {
    }

    static ResponseChannel<TResponse> ITinyhandCloneable<ResponseChannel<TResponse>>.Clone(scoped ref ResponseChannel<TResponse> v, TinyhandSerializerOptions options)
    {
        return new(TinyhandSerializer.Clone(v.Value), v.IsValueSet, v.ResponseDelegate);
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Tinyhand.IO;

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
/// Client side: Set SendValue and call the NetService.<br/>
/// Server side: Read SendValue and set ReceiveValue.<br/>
/// If asynchronous processing is required, do not use NetUnion; define a normal NetService method instead.
/// </summary>
/// <typeparam name="TResponse"></typeparam>
[TinyhandObject]
public sealed partial record class ResponseChannel<TResponse> : IResponseChannelInternal, ITinyhandSerializable<ResponseChannel<TResponse>>, ITinyhandCloneable<ResponseChannel<TResponse>>, ITinyhandReconstructable<ResponseChannel<TResponse>>
{
    /// <summary>
    /// Gets the value returned by the server to the client.
    /// </summary>
    public TResponse? Value { get; private set; }

    /// <summary>
    /// Gets the callback invoked on the client when a response is received.
    /// </summary>
    public ResponseDelegate<TResponse>? ResponseDelegate { get; private set; }

    [MemberNotNull(nameof(Value))]
    public bool IsValueSet { get; private set; }

    // private SendTransmission? sendTransmission;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResponseChannel{TReceive}"/> class with a request value and optional receive callback.
    /// </summary>
    /// <param name="receiveDelegate">The optional callback to invoke when a response is received on the client.</param>
    public ResponseChannel(ResponseDelegate<TResponse>? receiveDelegate)
    {
        this.ResponseDelegate = receiveDelegate;
    }

    private ResponseChannel()
    {
    }

    public void SetResponse(TResponse value)
    {
        this.Value = value;
        this.IsValueSet = true;
    }

    /*void INetUnionInternal.SetSendTransmission(SendTransmission sendTransmission)
    {
        this.sendTransmission = sendTransmission;
    }*/

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

            var numberOfData = reader.ReadArrayHeader();
            byte b = 0;
            if (numberOfData-- > 0 && !reader.TryReadNil())
            {
                b = reader.ReadUInt8();
            }

            if (numberOfData-- > 0 && !reader.TryReadNil())
            {
                if (b == 0)
                {// SendValue
                }
                else
                {// ReceiveValue
                    receiveValue = TinyhandSerializer.Deserialize<TResponse>(ref reader, TinyhandSerializerOptions.Standard);
                }
            }
        }
        catch
        {
            this.ResponseDelegate?.Invoke(NetResult.DeserializationFailed, default);
            return;
        }

        // this.IsResponded = true;
        // this.ReceiveValue = receiveValue;

        /*if (this.sendTransmission is not null)
        {
            this.sendTransmission.Dispose();
            this.sendTransmission = default;
        }*/

        this.ResponseDelegate?.Invoke(response.Result, receiveValue);
    }

    void IResponseChannelInternal.Invoke(NetResult result)
    {
        // this.IsResponded = true;

        /*if (this.sendTransmission is not null)
        {
            this.sendTransmission.Dispose();
            this.sendTransmission = default;
        }*/

        this.ResponseDelegate?.Invoke(result, default);
    }

    static void ITinyhandSerializable<ResponseChannel<TResponse>>.Serialize(ref TinyhandWriter writer, scoped ref ResponseChannel<TResponse>? value, TinyhandSerializerOptions options)
    {
        if (value is null ||
            !value.IsValueSet)
        {
            writer.WriteNil();
            return;
        }

        TinyhandSerializer.Serialize<TResponse>(ref writer, value.Value, options);
    }

    static void ITinyhandSerializable<ResponseChannel<TResponse>>.Deserialize(ref TinyhandReader reader, scoped ref ResponseChannel<TResponse>? value, TinyhandSerializerOptions options)
    {
        value ??= new();
        if (reader.TryReadNil())
        {
            return;
        }

        value.Value = TinyhandSerializer.Deserialize<TResponse>(ref reader, options);
    }

    static void ITinyhandReconstructable<ResponseChannel<TResponse>>.Reconstruct([NotNull] scoped ref ResponseChannel<TResponse>? value, TinyhandSerializerOptions options)
    {
        value ??= new();
    }

    static ResponseChannel<TResponse>? ITinyhandCloneable<ResponseChannel<TResponse>>.Clone(scoped ref ResponseChannel<TResponse>? value, TinyhandSerializerOptions options)
    {
        if (value is null)
        {
            return null;
        }

        var newValue = new ResponseChannel<TResponse>();
        newValue.Value = TinyhandSerializer.Clone(value.Value);
        newValue.ResponseDelegate = value.ResponseDelegate;
        return newValue;
    }
}

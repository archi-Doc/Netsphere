// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Tinyhand.IO;

namespace Netsphere;

public delegate void ReceiveDelegate<TReceive>(NetResult result, TReceive? value);

public interface IReceiveDelegateAndValueInternal
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
/// <typeparam name="TReceive"></typeparam>
[TinyhandObject]
public sealed partial record class ReceiveDelegateAndValue<TReceive> : IReceiveDelegateAndValueInternal, ITinyhandSerializable<ReceiveDelegateAndValue<TReceive>>, ITinyhandCloneable<ReceiveDelegateAndValue<TReceive>>, ITinyhandReconstructable<ReceiveDelegateAndValue<TReceive>>
{
    /// <summary>
    /// Gets the value returned by the server to the client.
    /// </summary>
    public TReceive? Value { get; private set; }

    /// <summary>
    /// Gets the callback invoked on the client when a response is received.
    /// </summary>
    public ReceiveDelegate<TReceive>? ReceiveDelegate { get; private set; }

    // private SendTransmission? sendTransmission;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReceiveDelegateAndValue{TReceive}"/> class with a request value and optional receive callback.
    /// </summary>
    /// <param name="receiveDelegate">The optional callback to invoke when a response is received on the client.</param>
    public ReceiveDelegateAndValue(ReceiveDelegate<TReceive>? receiveDelegate)
    {
        this.ReceiveDelegate = receiveDelegate;
    }

    private ReceiveDelegateAndValue()
    {
    }

    public void SetResponse(TReceive receiveValue)
    {
        this.Value = receiveValue;
    }

    /*void INetUnionInternal.SetSendTransmission(SendTransmission sendTransmission)
    {
        this.sendTransmission = sendTransmission;
    }*/

    void IReceiveDelegateAndValueInternal.Invoke(NetResponse response)
    {
        if (response.Result != NetResult.Success ||
            response.Received.IsEmpty)
        {
            this.ReceiveDelegate?.Invoke(response.Result, default);
            return;
        }

        if (response.DataId != 0)
        {
            this.ReceiveDelegate?.Invoke((NetResult)response.DataId, default);
            return;
        }

        var span = response.Received.Span;
        TReceive? receiveValue = default;
        try
        {
            var reader = new TinyhandReader(span);
            if (reader.TryReadNil())
            {
                this.ReceiveDelegate?.Invoke(NetResult.DeserializationFailed, default);
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
                    receiveValue = TinyhandSerializer.Deserialize<TReceive>(ref reader, TinyhandSerializerOptions.Standard);
                }
            }
        }
        catch
        {
            this.ReceiveDelegate?.Invoke(NetResult.DeserializationFailed, default);
            return;
        }

        // this.IsResponded = true;
        // this.ReceiveValue = receiveValue;

        /*if (this.sendTransmission is not null)
        {
            this.sendTransmission.Dispose();
            this.sendTransmission = default;
        }*/

        this.ReceiveDelegate?.Invoke(response.Result, receiveValue);
    }

    void IReceiveDelegateAndValueInternal.Invoke(NetResult result)
    {
        // this.IsResponded = true;

        /*if (this.sendTransmission is not null)
        {
            this.sendTransmission.Dispose();
            this.sendTransmission = default;
        }*/

        this.ReceiveDelegate?.Invoke(result, default);
    }

    static void ITinyhandSerializable<ReceiveDelegateAndValue<TReceive>>.Serialize(ref TinyhandWriter writer, scoped ref ReceiveDelegateAndValue<TReceive>? value, TinyhandSerializerOptions options)
    {
        if (value is null ||
            value.Value is null)
        {
            writer.WriteNil();
            return;
        }

        TinyhandSerializer.Serialize<TReceive>(ref writer, value.Value, options);
    }

    static void ITinyhandSerializable<ReceiveDelegateAndValue<TReceive>>.Deserialize(ref TinyhandReader reader, scoped ref ReceiveDelegateAndValue<TReceive>? value, TinyhandSerializerOptions options)
    {
        value ??= new();
        if (reader.TryReadNil())
        {
            return;
        }

        value.Value = TinyhandSerializer.Deserialize<TReceive>(ref reader, options);
    }

    static void ITinyhandReconstructable<ReceiveDelegateAndValue<TReceive>>.Reconstruct([NotNull] scoped ref ReceiveDelegateAndValue<TReceive>? value, TinyhandSerializerOptions options)
    {
        value ??= new();
    }

    static ReceiveDelegateAndValue<TReceive>? ITinyhandCloneable<ReceiveDelegateAndValue<TReceive>>.Clone(scoped ref ReceiveDelegateAndValue<TReceive>? value, TinyhandSerializerOptions options)
    {
        if (value is null)
        {
            return null;
        }

        var newValue = new ReceiveDelegateAndValue<TReceive>();
        newValue.Value = TinyhandSerializer.Clone(value.Value);
        newValue.ReceiveDelegate = value.ReceiveDelegate;
        return newValue;
    }
}

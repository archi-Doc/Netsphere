// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Netsphere;
using Tinyhand.IO;

namespace Netsphere;

/// <summary>
/// A shared structure used to send and receive data between the client and server.<br/>
/// On the client side, a delegate is invoked when data is received.<br/>
/// Its advantage is that it does not rely on Task, but its use is not recommended.<br/>
/// <br/>
/// Client side: Set SendValue and call the NetService.<br/>
/// Server side: Read SendValue and set ReceiveValue.<br/>
/// If asynchronous processing is required, do not use NetUnion; define a normal NetService method instead.
/// </summary>
/// <typeparam name="TSend"></typeparam>
/// <typeparam name="TReceive"></typeparam>
[TinyhandObject]
public sealed partial record class NetUnion<TSend, TReceive> : ITinyhandSerializable<NetUnion<TSend, TReceive>>, ITinyhandCloneable<NetUnion<TSend, TReceive>>, ITinyhandReconstructable<NetUnion<TSend, TReceive>>
{
    /// <summary>
    /// Gets the value sent from the client.
    /// </summary>
    public TSend? SendValue { get; private set; }

    /// <summary>
    /// Gets the value returned by the server to the client.
    /// </summary>
    public TReceive? ReceiveValue { get; private set; }

    /// <summary>
    /// Gets the callback invoked on the client when a response is received.
    /// </summary>
    public ReceiveDelegate<TReceive>? ReceiveDelegate { get; private set; }

    public bool IsResponded { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="NetUnion{TSend, TReceive}"/> class with a request value and optional receive callback.
    /// </summary>
    /// <param name="sendValue">The value to send to the service.</param>
    /// <param name="receiveDelegate">The optional callback to invoke when a response is received on the client.</param>
    public NetUnion(TSend sendValue, ReceiveDelegate<TReceive>? receiveDelegate)
    {
        this.SendValue = sendValue;
        this.ReceiveDelegate = receiveDelegate;
    }

    private NetUnion()
    {
    }

    public void SetResponse(TReceive receiveValue)
    {
        this.IsResponded = true;
        this.ReceiveValue = receiveValue;
    }

    static void ITinyhandSerializable<NetUnion<TSend, TReceive>>.Serialize(ref TinyhandWriter writer, scoped ref NetUnion<TSend, TReceive>? value, TinyhandSerializerOptions options)
    {
        if (value is null ||
            value.SendValue is null)
        {
            writer.WriteNil();
            return;
        }

        writer.WriteArrayHeader(2);
        if (!value.IsResponded)
        {// 0, SendValue
            writer.WriteUInt8(0);
            TinyhandSerializer.Serialize<TSend>(ref writer, value.SendValue, options);
        }
        else if (value.ReceiveValue is not null)
        {// 1, ReceiveValue
            writer.WriteUInt8(1);
            TinyhandSerializer.Serialize<TReceive>(ref writer, value.ReceiveValue, options);
        }
        else
        {
            writer.WriteNil();
            writer.WriteNil();
        }
    }

    static void ITinyhandSerializable<NetUnion<TSend, TReceive>>.Deserialize(ref TinyhandReader reader, scoped ref NetUnion<TSend, TReceive>? value, TinyhandSerializerOptions options)
    {
        if (reader.TryReadNil())
        {
            return;
        }

        value ??= new();
        var numberOfData = reader.ReadArrayHeader();
        options.Security.DepthStep(ref reader);
        try
        {
            byte b = 0;
            if (numberOfData-- > 0 && !reader.TryReadNil())
            {
                reader.TryRead(out b);
            }

            if (numberOfData-- > 0 && !reader.TryReadNil())
            {
                if (b == 0)
                {// SendValue
                    value.SendValue = TinyhandSerializer.Deserialize<TSend>(ref reader, options);
                }
                else
                {// ReceiveValue
                    value.ReceiveValue = TinyhandSerializer.Deserialize<TReceive>(ref reader, options);
                }
            }
        }
        finally
        {
            reader.Depth--;
        }
    }

    static void ITinyhandReconstructable<NetUnion<TSend, TReceive>>.Reconstruct([NotNull] scoped ref NetUnion<TSend, TReceive>? value, TinyhandSerializerOptions options)
    {
        value ??= new();
    }

    static NetUnion<TSend, TReceive>? ITinyhandCloneable<NetUnion<TSend, TReceive>>.Clone(scoped ref NetUnion<TSend, TReceive>? value, TinyhandSerializerOptions options)
    {
        if (value is null)
        {
            return null;
        }

        var newValue = new NetUnion<TSend, TReceive>();
        newValue.SendValue = TinyhandSerializer.Clone(value.SendValue);
        newValue.ReceiveValue = TinyhandSerializer.Clone(value.ReceiveValue);
        newValue.ReceiveDelegate = value.ReceiveDelegate;
        return newValue;
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Netsphere;
using Tinyhand.IO;

namespace xUnitTest.NetsphereTest;

[TinyhandObject]
public sealed partial record class NetUnion<TSend, TReceive> : ITinyhandSerializable<NetUnion<TSend, TReceive>>, ITinyhandCloneable<NetUnion<TSend, TReceive>>, ITinyhandReconstructable<NetUnion<TSend, TReceive>>
{
    public TSend? SendValue { get; private set; }

    public TReceive? ReceiveValue { get; set; }

    public ReceiveDelegate? ReceiveDelegate { get; private set; }

    public NetUnion(TSend value, ReceiveDelegate? receiveDelegate)
    {
        this.SendValue = value;
        this.ReceiveDelegate = receiveDelegate;
    }

    private NetUnion()
    {
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
        if (value.ReceiveValue is null)
        {// 0, SendValue
            writer.WriteUInt8(0);
            TinyhandSerializer.Serialize<TSend>(ref writer, value.SendValue, options);
        }
        else
        {// 1, ReceiveValue
            writer.WriteUInt8(1);
            TinyhandSerializer.Serialize<TReceive>(ref writer, value.ReceiveValue, options);
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

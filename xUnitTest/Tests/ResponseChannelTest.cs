// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;
using Tinyhand;
using Xunit;

namespace xUnitTest;

public class ResponseChannelTest
{
    [Fact]
    public void EmptyErrorResponsePreservesRemoteResult()
    {
        var actual = NetResult.Success;
        IResponseChannelInternal channel = new ResponseChannel<int>((result, _) => actual = result);
        channel.Invoke(new NetResponse(NetResult.Success, (ulong)NetResult.NotFound, 0, default));
        Assert.Equal(NetResult.NotFound, actual);
    }

    [Fact]
    public void SerializationRoundtripPreservesResponseState()
    {
        var channel = new ResponseChannel<int>();
        channel.SetResponse(42);
        var serialized = TinyhandSerializer.Serialize(channel);
        var restored = TinyhandSerializer.Deserialize<ResponseChannel<int>>(serialized);
        Assert.True(restored.IsValueSet);
        Assert.Equal(42, restored.Value);
        Assert.Equal(serialized, TinyhandSerializer.Serialize(restored));
    }

    [Fact]
    public void DeserializationFailureInvokesThrowingCallbackOnlyOnce()
    {
        var calls = 0;
        IResponseChannelInternal channel = new ResponseChannel<int>((_, _) =>
        {
            calls++;
            throw new InvalidOperationException("callback");
        });
        Assert.True(NetHelper.TrySerialize<string?>(null, out var memory));
        try
        {
            Assert.Throws<InvalidOperationException>(() => channel.Invoke(new NetResponse(NetResult.Success, 0, 0, memory)));
            Assert.Equal(1, calls);
        }
        finally
        {
            memory.Return();
        }
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using Arc.Collections;
using Netsphere;
using Netsphere.Core;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class ResponderIsolationTest
{
    private readonly NetFixture fixture;

    public ResponderIsolationTest(NetFixture fixture) => this.fixture = fixture;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentResponsesKeepTheirOwnConnection(bool asynchronous)
    {
        using var client = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(client);
        var first = new ServerConnection(client);
        var second = new ServerConnection(client);
        first.ChangeStateInternal(Connection.State.Closed);
        second.ChangeStateInternal(Connection.State.Closed);
        using var gate = new Barrier(2);
        var observed = new ConcurrentDictionary<int, ServerConnection>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remaining = 2;
        void Observe(int index, Func<ServerConnection> getConnection)
        {
            if (!gate.SignalAndWait(TimeSpan.FromSeconds(10)))
            {
                completed.TrySetException(new TimeoutException("Concurrent responder did not arrive."));
                return;
            }

            observed[index] = getConnection();
            if (Interlocked.Decrement(ref remaining) == 0)
            {
                completed.TrySetResult();
            }
        }

        INetResponder responder = asynchronous ? new ConcurrentAsyncResponder(Observe) : new ConcurrentSyncResponder(Observe);
        var firstContext = CreateContext(first, 1);
        var secondContext = CreateContext(second, 2);
        await Task.WhenAll(
            Task.Run(() => responder.Respond(firstContext), TestContext.Current.CancellationToken),
            Task.Run(() => responder.Respond(secondContext), TestContext.Current.CancellationToken));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.Same(first, observed[1]);
        Assert.Same(second, observed[2]);
        Assert.Null(TransmissionContext.AsyncLocal.Value);
    }

    private static TransmissionContext CreateContext(ServerConnection connection, int value)
    {
        Assert.True(NetHelper.TrySerialize(value, out BytePool.RentMemory memory));
        return new(connection, (uint)value, 0, 0, memory);
    }

    private sealed class ConcurrentSyncResponder(Action<int, Func<ServerConnection>> observe) : SyncResponder<int, string>
    {
        public override NetResultAndValue<string> RespondSync(int value)
        {
            observe(value, () => this.ServerConnection);
            return default;
        }
    }

    private sealed class ConcurrentAsyncResponder(Action<int, Func<ServerConnection>> observe) : AsyncResponder<int, string>
    {
        public override NetResultAndValue<string> RespondAsync(int value)
        {
            observe(value, () => this.ServerConnection);
            return default;
        }
    }
}

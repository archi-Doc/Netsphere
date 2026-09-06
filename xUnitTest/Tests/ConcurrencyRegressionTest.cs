// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using Arc.Collections;
using Arc.Unit;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;
using Netsphere.Core;
using Netsphere.Packet;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class ConcurrencyRegressionTest
{
    private static int nextTransmissionId;
    private readonly NetFixture fixture;

    public ConcurrencyRegressionTest(NetFixture fixture) => this.fixture = fixture;

    [Theory]
    [InlineData(DataControl.Cancel)]
    [InlineData(DataControl.Complete)]
    public async Task ControlFrameAdvancesSendPosition(DataControl control)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new SendTransmission(connection, (uint)Interlocked.Increment(ref nextTransmissionId));
        Assert.Equal(NetResult.Success, transmission.SendStream(100));
        var stream = new SendStream(transmission, 100, 0);
        try
        {
            Assert.True(transmission.TrySendControl(stream, control));
            Assert.Equal(1, transmission.GeneSerialMax);
            Assert.Equal(NetTransmissionMode.StreamCompleted, transmission.Mode);
        }
        finally
        {
            stream.Dispose(true);
        }
    }

    [Fact]
    public async Task FullWindowDoesNotCommitControlFrame()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new SendTransmission(connection, (uint)Interlocked.Increment(ref nextTransmissionId));
        transmission.SendStream(100);
        var stream = new SendStream(transmission, 100, 0);
        try
        {
            transmission.MaxReceivePosition = 0;
            Assert.False(transmission.TrySendControl(stream, DataControl.Cancel));
            Assert.Equal(NetTransmissionMode.Stream, transmission.Mode);
            transmission.MaxReceivePosition = 1;
            Assert.True(transmission.TrySendControl(stream, DataControl.Cancel));
        }
        finally
        {
            stream.Dispose(true);
        }
    }

    [Fact]
    public async Task DisposalReleasesSendWaitingForWindow()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new SendTransmission(connection, (uint)Interlocked.Increment(ref nextTransmissionId));
        transmission.SendStream(100);
        var stream = new SendStream(transmission, 100, 0);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            transmission.MaxReceivePosition = 1;
            Assert.Equal(NetResult.Success, await stream.Send(new byte[1], timeout.Token));
            var pending = stream.Send(new byte[1], timeout.Token);
            Assert.False(pending.IsCompleted);
            transmission.Dispose();
            Assert.Equal(NetResult.Closed, await pending.WaitAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken));
        }
        finally
        {
            stream.Dispose(true);
        }
    }

    [Fact]
    public async Task CanceledSendDoesNotQueuePayload()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        var (_, stream) = connection.SendStream(100);
        Assert.NotNull(stream);
        try
        {
            Assert.Equal(NetResult.Canceled, await stream.Send(new byte[10], new CancellationToken(true)));
            Assert.Equal(0, stream.SentLength);
        }
        finally
        {
            stream.Dispose(true);
        }
    }

    [Fact]
    public async Task StreamRegistersResponseBeforeSending()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        var (_, stream) = connection.SendStream(100);
        Assert.NotNull(stream);
        try
        {
            var id = stream.SendTransmission.TransmissionId + connection.ConnectionTerminal.ReceiveTransmissionGap;
            var receivers = (ReceiveTransmission.GoshujinClass)typeof(Connection).GetField("receiveTransmissions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(connection)!;
            ReceiveTransmission? receiver;
            using (receivers.LockObject.EnterScope())
            {
                Assert.True(receivers.TransmissionIdChain.TryGetValue(id, out receiver));
            }

            Assert.NotNull(receiver);
            receiver.SetState_Receiving(1);
            var packet = BytePool.Default.Rent(12).AsMemory(0, 12);
            try
            {
                packet.Span.Clear();
                BitConverter.TryWriteBytes(packet.Span.Slice(4), (ulong)NetResult.Success);
                receiver.ProcessReceive_Gene(DataControl.Valid, 0, packet);
            }
            finally
            {
                packet.Return();
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            Assert.Equal(NetResult.Success, await stream.Complete(timeout.Token));
        }
        finally
        {
            stream.Dispose(true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParallelSendsPreserveCallBoundaries(bool cancelSecond)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new SendTransmission(connection, (uint)Interlocked.Increment(ref nextTransmissionId));
        transmission.SendStream(10_000);
        var stream = new SendStream(transmission, 10_000, 0);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            transmission.MaxReceivePosition = 1;
            var first = stream.Send(new byte[FirstGeneFrame.MaxGeneLength + 1], timeout.Token);
            Assert.False(first.IsCompleted);
            var second = cancelSecond ? stream.Cancel(timeout.Token) : stream.Send(new byte[10], timeout.Token);
            Assert.False(second.IsCompleted);
            transmission.MaxReceivePosition = 10;
            Assert.Equal(NetResult.Success, await first);
            Assert.Equal(NetResult.Success, await second);
            Assert.Equal(FirstGeneFrame.MaxGeneLength + 1 + (cancelSecond ? 0 : 10), stream.SentLength);
            Assert.Equal(3, transmission.GeneSerialMax);
        }
        finally
        {
            stream.Dispose(true);
        }
    }

    [Fact]
    public async Task LateResponseAfterCanceledWaitReturnsMemory()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        var completion = new TaskCompletionSource<NetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, completion, null);
        transmission.SetState_Receiving(1);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transmission.Wait(completion.Task, -1, new CancellationToken(true)));
        var packet = BytePool.Default.Rent(13).AsMemory(0, 13);
        try
        {
            packet.Span.Clear();
            transmission.ProcessReceive_Gene(DataControl.Valid, 0, packet);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            while (packet.RentArray!.Count != 1)
            {
                await Task.Delay(1, timeout.Token);
            }
        }
        finally
        {
            packet.Return();
        }
    }

    [Fact]
    public async Task DuplicatePacketsCompleteCallbackOnceInParallel()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var received = 0;
            IResponseChannelInternal channel = new ResponseChannel<int>((_, _) => Interlocked.Increment(ref received));
            using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, channel);
            transmission.SetState_Receiving(1);
            var packet = BytePool.Default.Rent(13).AsMemory(0, 13);
            try
            {
                packet.Span.Clear();
                await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => transmission.ProcessReceive_Gene(DataControl.Valid, 0, packet), TestContext.Current.CancellationToken)));
                Assert.Equal(1, received);
                Assert.Equal(1, packet.RentArray!.Count);
            }
            finally
            {
                packet.Return();
            }
        }
    }

    [Fact]
    public async Task LossDetectionIgnoresDisposedGenesAndCoalescesDuplicates()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new SendTransmission(connection, (uint)Interlocked.Increment(ref nextTransmissionId));
        transmission.SendStream(100);
        var gene = new SendGene(transmission);
        ICongestionControl congestion = new CubicCongestionControl(connection);
        congestion.AddInFlight(gene, 0);
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => congestion.LossDetected(gene), TestContext.Current.CancellationToken)));
        var queue = (System.Collections.ICollection)typeof(CubicCongestionControl).GetField("genesLossDetected", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(congestion)!;
        Assert.Single(queue);
        congestion.RemoveInFlight(gene, false);
        gene.Dispose(false);
        var disposed = new SendGene(transmission);
        disposed.Dispose(false);
        congestion.LossDetected(disposed);
        Assert.Single(queue);
    }

    [Fact]
    public async Task SendAndDisposeDoNotRetainPacketOrRequeueGene()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new SendTransmission(connection, (uint)Interlocked.Increment(ref nextTransmissionId));
        var sender = new NetSender(this.fixture.NetUnit.NetTerminal, this.fixture.NetUnit.NetBase, this.fixture.NetUnit.ServiceProvider.GetRequiredService<LogUnit>().RootLogService.GetLogger<NetSender>());
        try
        {
            for (var iteration = 0; iteration < 200; iteration++)
            {
                var packet = BytePool.Default.Rent(32).AsMemory(0, 32);
                var owner = packet.RentArray!;
                var gene = new SendGene(transmission);
                gene.SetSend(packet);
                await Task.WhenAll(
                    Task.Run(() => gene.Send_NotThreadSafe(sender, 0), TestContext.Current.CancellationToken),
                    Task.Run(() => gene.Dispose(false), TestContext.Current.CancellationToken));
                Assert.Null(gene.Node);
                Assert.False(gene.Send_NotThreadSafe(sender, 0));
                sender.Stop();
                Assert.Equal(0, owner.Count);
            }
        }
        finally
        {
            sender.Stop();
        }
    }

    [Fact]
    public async Task CanceledWindowCannotBeReopenedByDelayedAck()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new SendTransmission(connection, (uint)Interlocked.Increment(ref nextTransmissionId));
        transmission.SendStream(100);
        transmission.ProcessReceive_AckBlock(0, 0, Span<byte>.Empty, 0);
        transmission.ProcessReceive_AckBlock(10, 0, Span<byte>.Empty, 0);
        Assert.Equal(0, transmission.MaxReceivePosition);
    }

    [Fact]
    public async Task RetransmissionBackoffDoesNotOverflow()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var original = connection.Taichi;
        try
        {
            connection.ResetTaichi();
            var previous = connection.TaichiTimeout;
            for (var iteration = 0; iteration < 64; iteration++)
            {
                connection.DoubleTaichi();
                Assert.InRange(connection.TaichiTimeout, previous, int.MaxValue);
                previous = connection.TaichiTimeout;
            }

            Assert.Equal(int.MaxValue, previous);
        }
        finally
        {
            connection.Taichi = original;
        }
    }

    [Fact]
    public async Task CanceledResponseCompletionReturnsSharedMemory()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        var completion = new TaskCompletionSource<NetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, completion, null);
        transmission.SetState_Receiving(1);
        completion.SetCanceled(TestContext.Current.CancellationToken);
        var packet = BytePool.Default.Rent(13).AsMemory(0, 13);
        try
        {
            packet.Span.Clear();
            transmission.ProcessReceive_Gene(DataControl.Valid, 0, packet);
            Assert.Equal(1, packet.RentArray!.Count);
        }
        finally
        {
            packet.Return();
        }
    }

    [Fact]
    public async Task ReusingConnectionDuringReleaseKeepsNewReferenceOpen()
    {
        var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        Task release;
        using var started = new ManualResetEventSlim();
        using (connection.Goshujin!.LockObject.EnterScope())
        {
            release = Task.Run(
                () =>
                {
                    started.Set();
                    connection.Dispose();
                },
                TestContext.Current.CancellationToken);
            Assert.True(started.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
            var count = typeof(ClientConnection).GetField("openCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
            // Give the release a chance to reach the connection lock before simulating reuse.
            SpinWait.SpinUntil(() => (int)count.GetValue(connection)! == 0, 50);
            connection.IncrementOpenCount();
        }

        try
        {
            await release.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.True(connection.IsOpen);
            Assert.Equal(2, await connection.GetService<IBasicService>().IncrementInt(1));
        }
        finally
        {
            connection.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledReceiveReleasesBufferedData(bool cancelBeforeRead)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, null);
        transmission.SetState_ReceivingStream(100);
        var stream = new ReceiveStream(transmission, 0, 100);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var packet = BytePool.Default.Rent(13).AsMemory(0, 13);
        try
        {
            packet.Span.Clear();
            transmission.ProcessReceive_Gene(DataControl.Valid, 0, packet);
            if (cancelBeforeRead)
            {
                cancellation.Cancel();
            }

            var pending = stream.Receive(new byte[2], cancellation.Token);
            cancellation.Cancel();
            var result = await pending;
            Assert.Equal(NetResult.Canceled, result.Result);
            Assert.Equal(cancelBeforeRead ? 0 : 1, result.Written);
            Assert.True(transmission.IsDisposed);
            Assert.Equal(1, packet.RentArray!.Count);
        }
        finally
        {
            packet.Return();
        }
    }

    [Fact]
    public async Task ParallelBlockReceivesKeepLengthPrefixAndPayloadTogether()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, null);
        transmission.SetState_ReceivingStream(10);
        IReceiveStreamInternal stream = new ReceiveStream(transmission, 0, 10);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var packet = BytePool.Default.Rent(16).AsMemory(0, 16);
        var following = BytePool.Default.Rent(6).AsMemory(0, 6);
        try
        {
            packet.Span.Clear();
            BitConverter.TryWriteBytes(packet.Span.Slice(12), 1);
            transmission.ProcessReceive_Gene(DataControl.Valid, 0, packet);
            var first = stream.ReceiveBlock<int>(timeout.Token);
            var second = stream.ReceiveBlock<int>(timeout.Token);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            new byte[] { 42, 1, 0, 0, 0, 43 }.CopyTo(following.Span);
            transmission.ProcessReceive_Gene(DataControl.Valid, 1, following);
            var results = await Task.WhenAll(first, second);
            Assert.All(results, result => Assert.Equal(NetResult.Success, result.Result));
            Assert.Equal(42, results[0].Value);
            Assert.Equal(43, results[1].Value);
        }
        finally
        {
            packet.Return();
            following.Return();
        }
    }

    [Fact]
    public async Task ParallelStreamsReceiveResponsesAfterSendAcknowledgment()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var service = connection.GetService<IStreamService>();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async index =>
        {
            var data = Enumerable.Repeat((byte)index, 10_000 + index).ToArray();
            var stream = await service.PutAndGetHash(data.Length).WaitAsync(timeout.Token);
            Assert.NotNull(stream);
            try
            {
                Assert.Equal(NetResult.Success, await stream.Send(data, timeout.Token));
                var sent = stream.SendTransmission.SentTcs?.Task;
                if (sent is not null)
                {
                    Assert.Equal(NetResult.Success, await sent.WaitAsync(timeout.Token));
                }

                var response = await stream.CompleteSendAndReceive(timeout.Token);
                Assert.Equal(NetResult.Success, response.Result);
                Assert.Equal(Arc.Crypto.XxHash3.Hash64(data), response.Value);
                Assert.Equal(NetResult.InvalidOperation, (await stream.CompleteSendAndReceive(timeout.Token)).Result);
            }
            finally
            {
                stream.Dispose(true);
            }
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowProbeDoesNotCancelAnUninitializedReceiver(bool initialized)
    {
        using var connected = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connected);
        var terminal = new PacketTerminal(this.fixture.NetUnit.NetBase, this.fixture.NetUnit.NetTerminal, this.fixture.NetUnit.ServiceProvider.GetRequiredService<LogUnit>().RootLogService.GetLogger<PacketTerminal>());
        using var connection = new ClientConnection(terminal, connected.ConnectionTerminal, 1, Alternative.NetNode, connected.DestinationEndpoint);
        connection.Initialize(connected.Agreement, new byte[Connection.EmbryoSize]);
        using var receiver = connection.TryCreateReceiveTransmission(1, null);
        Assert.NotNull(receiver);
        if (initialized)
        {
            receiver.SetState_ReceivingStream(100);
        }

        var probe = BytePool.Default.Rent(4).AsMemory(0, 4);
        var items = (System.Collections.IEnumerable)typeof(PacketTerminal).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(terminal)!;
        try
        {
            BitConverter.TryWriteBytes(probe.Span, receiver.TransmissionId);
            connection.ProcessReceive_Knock(connection.DestinationEndpoint, probe);
            Assert.Equal(initialized ? 1 : 0, items.Cast<object>().Count());
        }
        finally
        {
            probe.Return();
            foreach (var item in items.Cast<object>().ToArray())
            {
                item.GetType().GetMethod("Remove")!.Invoke(item, null);
            }
        }
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(2, 100)]
    [InlineData(0, 0)]
    [InlineData(200, 200)]
    public async Task WindowProbeResponsesPreserveMonotonicWindow(int advertised, int expected)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new SendTransmission(connection, (uint)Interlocked.Increment(ref nextTransmissionId));
        transmission.SendStream(100);
        transmission.MaxReceivePosition = 100;
        transmission.ProcessReceive_KnockResponse(advertised);
        Assert.Equal(expected, transmission.MaxReceivePosition);
        transmission.ProcessReceive_KnockResponse(0);
        transmission.ProcessReceive_KnockResponse(200);
        Assert.Equal(0, transmission.MaxReceivePosition);
    }

    [Fact]
    public async Task WindowReadsCanRaceWithReceiverDisposal()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        for (var iteration = 0; iteration < 100; iteration++)
        {
            using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, null);
            transmission.SetState_ReceivingStream(100);
            var initialWindow = transmission.MaxReceivePosition;
            await Task.WhenAll(
                Task.Run(
                    () =>
                    {
                        for (var read = 0; read < 100; read++)
                        {
                            var window = transmission.MaxReceivePosition;
                            Assert.True(window == 0 || window == initialWindow);
                        }
                    },
                    TestContext.Current.CancellationToken),
                Task.Run(() => transmission.Dispose(), TestContext.Current.CancellationToken));
            Assert.Equal(0, transmission.MaxReceivePosition);
        }
    }
}

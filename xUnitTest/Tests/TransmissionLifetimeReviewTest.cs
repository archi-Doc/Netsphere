// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using System.Reflection;
using Arc.Collections;
using Arc.Unit;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;
using Netsphere.Core;
using Netsphere.Packet;
using Netsphere.Service;
using Xunit;

namespace xUnitTest.NetsphereTest;

/// <summary>
/// Covers send-transmission reclamation, response buffer ownership, and service disposal races.
/// </summary>
[Collection(NetFixtureCollection.Name)]
public class TransmissionLifetimeReviewTest
{
    private readonly NetFixture fixture;

    public TransmissionLifetimeReviewTest(NetFixture fixture) => this.fixture = fixture;

    [Fact]
    public void CleanReleasesSendTransmissionsThatStoppedBeingAcknowledged()
    {
        using var connection = this.CreateConnection();
        var transmission = connection.TryCreateSendTransmission();
        Assert.NotNull(transmission);
        Assert.Equal(1, connection.SendTransmissionsCount);

        connection.CleanTransmission();
        Assert.Equal(1, connection.SendTransmissionsCount); // Still within the timeout.
        Assert.False(transmission.IsDisposed);

        transmission.AckedMics = Mics.FastSystem - NetConstants.TransmissionTimeoutMics - 1;
        connection.CleanTransmission();

        Assert.Equal(0, connection.SendTransmissionsCount);
        Assert.True(transmission.IsDisposed);
        Assert.Null(this.GetSenders(connection).AckedListChain.First);
    }

    [Fact]
    public void CleanKeepsRecentlyAcknowledgedSendTransmissions()
    {
        using var connection = this.CreateConnection();
        var stale = connection.TryCreateSendTransmission();
        var fresh = connection.TryCreateSendTransmission();
        Assert.NotNull(stale);
        Assert.NotNull(fresh);

        stale.AckedMics = Mics.FastSystem - NetConstants.TransmissionTimeoutMics - 1;
        connection.CleanTransmission();

        Assert.True(stale.IsDisposed);
        Assert.False(fresh.IsDisposed);
        Assert.Equal(1, connection.SendTransmissionsCount);
        Assert.Same(fresh, this.GetSenders(connection).AckedListChain.First);
    }

    [Fact]
    public void AcknowledgementMovesTheTransmissionBehindTheOlderOnes()
    {
        using var connection = this.CreateConnection();
        var first = connection.TryCreateSendTransmission();
        var second = connection.TryCreateSendTransmission();
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Same(first, this.GetSenders(connection).AckedListChain.First);

        var senders = this.GetSenders(connection);
        using (senders.LockObject.EnterScope())
        {
            connection.UpdateAckedNode(first);
        }

        Assert.Same(second, senders.AckedListChain.First);
        Assert.Equal(2, connection.SendTransmissionsCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingTransmissionsUnlinksThemFromEveryChain(bool all)
    {
        using var connection = this.CreateConnection();
        for (var i = 0; i < 4; i++)
        {
            Assert.NotNull(connection.TryCreateSendTransmission());
        }

        if (all)
        {
            connection.CloseAllTransmission();
        }
        else
        {
            connection.CloseSendTransmission();
        }

        Assert.Equal(0, connection.SendTransmissionsCount);
        Assert.Null(this.GetSenders(connection).AckedListChain.First);

        // A reopened connection must not inherit the released transmissions.
        var reused = connection.TryCreateSendTransmission();
        Assert.NotNull(reused);
        Assert.Same(reused, this.GetSenders(connection).AckedListChain.First);
        connection.CloseSendTransmission();
    }

    [Fact]
    public void EchoedRentMemoryResponseKeepsTheRequestLease()
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        var request = BytePool.Default.Rent(64).AsMemory(0, 64);
        var array = request.RentArray!;
        var context = new TransmissionContext(server, 42, 1, 0, request);

        // The handler returns the borrowed request lease, as ITestService3.SendMemoryOwner does.
        context.SetResponseRentMemory(request.Slice(0, 8));

        Assert.Equal(1, array.Count);
        Assert.Same(array, context.RentMemory.RentArray);
        Assert.Equal(8, context.RentMemory.Length);

        context.Return();
        Assert.Equal(0, array.Count);
    }

    [Fact]
    public void IndependentRentMemoryResponseReleasesTheRequestLease()
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        var request = BytePool.Default.Rent(64).AsMemory(0, 64);
        var requestArray = request.RentArray!;
        var context = new TransmissionContext(server, 42, 1, 0, request);
        var response = BytePool.Default.Rent(8).AsMemory(0, 8);

        context.SetResponseRentMemory(response);

        Assert.Equal(0, requestArray.Count);
        Assert.Same(response.RentArray, context.RentMemory.RentArray);
        Assert.Equal(1, response.RentArray!.Count);

        context.Return();
    }

    [Fact]
    public void SharedRentMemoryResponseReleasesOnlyTheRequestReference()
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        var request = BytePool.Default.Rent(64).AsMemory(0, 64);
        var array = request.RentArray!;
        var context = new TransmissionContext(server, 42, 1, 0, request);

        // The handler acquired its own reference before returning the same buffer.
        context.SetResponseRentMemory(request.IncrementAndShare());

        Assert.Equal(1, array.Count);
        Assert.Same(array, context.RentMemory.RentArray);

        context.Return();
        Assert.Equal(0, array.Count);
    }

    [Fact]
    public async Task EchoedRentMemoryResponseSurvivesConcurrentRenting()
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        for (var i = 0; i < 200; i++)
        {
            var request = BytePool.Default.Rent(64).AsMemory(0, 64);
            request.Span.Fill(7);
            var context = new TransmissionContext(server, 42, 1, 0, request);
            context.SetResponseRentMemory(request);

            // Nothing else may obtain the buffer while it is still the pending response.
            var contention = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(
                () =>
                {
                    var other = BytePool.Default.Rent(64);
                    other.AsSpan().Fill(0);
                    other.Return();
                    return true;
                },
                TestContext.Current.CancellationToken)));

            Assert.All(contention, Assert.True);
            Assert.Equal(64, context.RentMemory.Length);
            foreach (var b in context.RentMemory.Span)
            {
                Assert.Equal(7, b);
            }

            context.Return();
        }
    }

    [Fact]
    public async Task GeneratedRentMemoryEchoKeepsTheResponseOutOfThePool()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        terminal.Services.EnableNetService<ITransportReviewService>();
        using var connection = await terminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var service = connection.GetService<ITransportReviewService>();

        var work = Task.WhenAll(Enumerable.Range(0, 16).Select(worker => Task.Run(
            async () =>
            {
                for (var i = 0; i < 20; i++)
                {
                    var payload = new byte[64];
                    payload.AsSpan().Fill((byte)(worker + 1));
                    var request = BytePool.Default.Rent(payload.Length).AsMemory(0, payload.Length);
                    try
                    {
                        payload.CopyTo(request.Span);
                        var response = await service.EchoRent(request).ConfigureAwait(false);
                        try
                        {
                            // A response served from a buffer that was already returned to the pool may hold another worker's bytes.
                            Assert.Equal(payload, response.Span.ToArray());
                        }
                        finally
                        {
                            response.Return();
                        }
                    }
                    finally
                    {
                        request.Return();
                    }
                }
            },
            TestContext.Current.CancellationToken)));

        // A corrupted pool stalls the exchange, so fail fast instead of hanging.
        await work.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void ServiceDisposalDetachesTheArrayInsteadOfClearingIt()
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        var context = server.GetContext();
        Assert.True(context.EnableNetService<IBasicService>());

        var field = typeof(ServerConnectionContext).GetField("netServiceItems", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var snapshot = (NetServiceItem[])field.GetValue(context)!;
        Assert.NotEmpty(snapshot);

        context.DisposeActual();

        Assert.Empty((NetServiceItem[])field.GetValue(context)!);
        Assert.All(snapshot, x => Assert.NotNull(x.NetServiceInfo)); // A concurrent reader's snapshot stays usable.
        Assert.False(context.IsNetServiceEnabled<IBasicService>());
    }

    [Fact]
    public async Task ServiceLookupDoesNotFaultWhileTheContextIsDisposed()
    {
        var method = typeof(ServerConnectionContext).GetMethod("TryGetServiceMethod", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var i = 0; i < 100; i++)
        {
            using var client = this.CreateConnection();
            using var server = new ServerConnection(client);
            var context = server.GetContext();
            context.EnableNetService<IBasicService>();
            context.EnableNetService<IStreamService>();
            context.EnableNetService<IBidirectionalService>();

            await Task.WhenAll(
                Task.Run(
                    () =>
                    {
                        for (var j = 0; j < 50; j++)
                        {
                            method.Invoke(context, new object[] { (ulong)j });
                        }
                    },
                    TestContext.Current.CancellationToken),
                Task.Run(() => context.DisposeActual(), TestContext.Current.CancellationToken));
        }
    }

    private ClientConnection CreateConnection()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var connection = new ClientConnection(this.CreatePacketTerminal(), terminal.ConnectionTerminal, 42, Alternative.NetNode, new NetEndpoint(0, new IPEndPoint(IPAddress.Loopback, 12345)));
        connection.Initialize(new ConnectionAgreement { MaxBlockSize = 100_000, MaxStreamLength = 1000, StreamBufferSize = 10_000 }, new byte[Connection.EmbryoSize]);
        return connection;
    }

    private SendTransmission.GoshujinClass GetSenders(Connection connection)
        => (SendTransmission.GoshujinClass)typeof(Connection).GetField("sendTransmissions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(connection)!;

    private PacketTerminal CreatePacketTerminal()
        => new(this.fixture.NetUnit.NetBase, this.fixture.NetUnit.NetTerminal, this.fixture.NetUnit.ServiceProvider.GetRequiredService<LogUnit>().RootLogService.GetLogger<PacketTerminal>());
}

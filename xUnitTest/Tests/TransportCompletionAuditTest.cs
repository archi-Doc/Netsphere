// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using System.Reflection;
using Netsphere;
using Netsphere.Core;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class TransportCompletionAuditTest
{
    private readonly NetFixture fixture;

    public TransportCompletionAuditTest(NetFixture fixture) => this.fixture = fixture;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalAcknowledgmentDuringCompletionKeepsSuccessfulResult(bool serverSide)
    {
        using var client = this.CreateConnection();
        using var server = new ServerConnection(client);
        using var transmission = new SendTransmission(serverSide ? server : client, 42);
        Assert.Equal(NetResult.Success, transmission.SendStream(1));
        var stream = new SendStreamAndReceive<NetResult>(transmission, 1, 0);
        try
        {
            Assert.Equal(NetResult.Success, await stream.Send(new byte[] { 1 }, TestContext.Current.CancellationToken));
            var gate = (SemaphoreSlim)typeof(SendTransmission).GetField("streamSendLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transmission)!;
            Task<NetResultAndValue<NetResult>> pending;
            await gate.WaitAsync(TestContext.Current.CancellationToken);
            try
            {
                pending = stream.CompleteSendAndReceive(TestContext.Current.CancellationToken);
                Assert.False(pending.IsCompleted);
                transmission.ProcessReceive_AckBlock(2, 1, Span<byte>.Empty, 0);
                Assert.True(transmission.IsDisposed);
                if (!serverSide)
                {
                    var response = (TaskCompletionSource<NetResponse>)typeof(SendStreamBase).GetField("responseSource", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(stream)!;
                    Assert.True(response.TrySetResult(new(NetResult.Success, (ulong)NetResult.Success, 0, default)));
                }
            }
            finally
            {
                gate.Release();
            }

            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(NetResult.Success, result.Result);
            Assert.Equal(NetResult.Success, result.Value);
        }
        finally
        {
            stream.Dispose(true);
        }
    }

    [Fact]
    public async Task PreCanceledRequestDoesNotAllocateAnAvailableTransmission()
    {
        using var connection = this.CreateConnection();
        using var allocated = await connection.TryCreateSendTransmission(TimeSpan.FromSeconds(1), new CancellationToken(true));
        Assert.Null(allocated.Transmission);
        Assert.Equal(0, connection.SendTransmissionsCount);
    }

    private ClientConnection CreateConnection()
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var connection = new ClientConnection(terminal.PacketTerminal, terminal.ConnectionTerminal, 42, Alternative.NetNode, new NetEndpoint(0, new IPEndPoint(IPAddress.Loopback, 12345)));
        connection.Initialize(new ConnectionAgreement { MaxStreamLength = 100, StreamBufferSize = 10_000 }, new byte[Connection.EmbryoSize]);
        return connection;
    }
}

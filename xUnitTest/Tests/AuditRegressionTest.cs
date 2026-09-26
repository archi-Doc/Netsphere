// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using System.Reflection;
using Arc;
using Arc.Collections;
using Arc.Crypto;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;
using Netsphere.Core;
using Netsphere.Crypto;
using Netsphere.Logging;
using Netsphere.Packet;
using Netsphere.Stats;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class AuditRegressionTest
{
    private readonly NetFixture fixture;

    public AuditRegressionTest(NetFixture fixture) => this.fixture = fixture;

    [Fact]
    public void FreshlyParsedKeyCanDecrypt()
    {
        using var sender = SeedKey.NewEncryption();
        using var receiver = SeedKey.NewEncryption();
        Assert.True(SeedKey.TryParse(receiver.UnsafeToString(), out var parsed)); // A parsed key has not derived its key pair yet.
        using var parsedKey = parsed;

        var message = new byte[] { 1, 2, 3 };
        var nonce = new byte[CryptoBox.NonceSize];
        var cipher = new byte[message.Length + CryptoBox.MacSize];
        Assert.True(sender.TryEncrypt(message, nonce, receiver.GetEncryptionPublicKeySpan(), cipher));
        var data = new byte[message.Length];
        Assert.True(parsedKey.TryDecrypt(cipher, nonce, sender.GetEncryptionPublicKeySpan(), data));
        Assert.Equal(message, data);
    }

    [Theory]
    [InlineData("!!!A!!!")]
    [InlineData("!!!AB=!!!")]
    [InlineData("!!!AAAAA!!!")]
    public void MalformedSeedKeyIsRejected(string text)
    {
        Assert.False(SeedKey.TryParse(text, out _, out var read));
        Assert.Equal(0, read);
    }

    [Theory]
    [InlineData("{AB}")]
    [InlineData("{A=}")]
    [InlineData("{AAAAAA}")]
    public void MalformedAuthenticationTokenIsRejected(string text)
        => Assert.False(AuthenticationToken.TryParse(text, out _, out _));

    [Fact]
    public void MalformedCertificateTokenIsRejected()
        => Assert.False(CertificateToken<AuthenticationToken>.TryParse("{CAAAAA}", out _, out _));

    [Fact]
    public void ClearedSeedKeyCannotBeUsed()
    {
        var key = SeedKey.NewSignature();
        _ = key.GetSignaturePublicKey(); // Derive and cache the key pair before clearing.
        key.Dispose();

        Assert.False(key.IsValid);
        Assert.Equal(string.Empty, key.UnsafeToString());
        Assert.Throws<InvalidOperationException>(() => key.Sign(new byte[] { 1 }, new byte[CryptoSign.SignatureSize]));
    }

    [Fact]
    public void InvalidSeedKeyIsNotTreatedAsTheZeroKey()
    {
        Assert.False(SeedKey.Invalid.IsValid);
        _ = SeedKey.Invalid.GetHashCode();
        Assert.Equal(string.Empty, SeedKey.Invalid.UnsafeToString());
        Assert.Throws<ArgumentException>(() => SeedKey.New(SeedKey.Invalid, new byte[] { 1 }));
    }

    [Fact]
    public void LargeCertificateTokenConvertsToString()
    {
        using var seedKey = SeedKey.NewSignature();
        var target = AuthenticationToken.UnsafeConstructor();
        Assert.True(seedKey.SignWithSalt(target, 1));
        var token = new CertificateToken<AuthenticationToken>(target);
        Assert.True(seedKey.SignWithSalt(token, 2));

        var text = token.ConvertToString();
        Assert.True(text.Length > 256); // Longer than the former fixed maximum length.
        Assert.True(CertificateToken<AuthenticationToken>.TryParse(text, out var parsed, out _));
        Assert.True(token.Equals(parsed));
    }

    [Fact]
    public void NegativeStreamLengthIsAcceptedAsUnlimited()
    {
        var agreement = new ConnectionAgreement { MaxStreamLength = 100 };
        agreement.AcceptAll(new ConnectionAgreement { MaxStreamLength = -2 });
        Assert.True(agreement.CheckStreamLength(long.MaxValue));
    }

    [Fact]
    public void NodeDefaultAgreementIsNotTheSharedDefault()
        => Assert.NotSame(ConnectionAgreement.Default, this.fixture.NetUnit.NetBase.DefaultAgreement);

    [Fact]
    public void AliasMapsStayOneToOne()
    {
        var alias = new Alias();
        var first = new Identifier(1);
        var second = new Identifier(2);

        alias.Add("a", first);
        alias.Add("a", second); // Moves the alias.
        Assert.False(alias.TryGetAliasFromIdentifier(first, out _));
        Assert.True(alias.TryGetIdentifierFromAlias("a", out var identifier));
        Assert.Equal(second, identifier);
        Assert.False(alias.Remove(first)); // A stale entry would remove the alias of the second identifier.
        Assert.True(alias.TryGetIdentifierFromAlias("a", out _));

        var third = new Identifier(3);
        var fourth = new Identifier(4);
        alias.TryAdd("c", third);
        alias.TryAdd("c", fourth); // The alias is taken; neither direction may change.
        Assert.False(alias.TryGetAliasFromIdentifier(fourth, out _));
        Assert.True(alias.TryGetIdentifierFromAlias("c", out identifier));
        Assert.Equal(third, identifier);

        var key1 = SeedKey.NewSignature().GetSignaturePublicKey();
        var key2 = SeedKey.NewSignature().GetSignaturePublicKey();
        alias.Add("k", key1);
        alias.Add("k", key2);
        Assert.False(alias.TryGetAliasFromPublicKey(key1, out _));
        Assert.True(alias.TryGetPublicKeyFromAlias("k", out var publicKey));
        Assert.Equal(key2, publicKey);
    }

    [Fact]
    public void LowOrderPublicKeyFailsTheHandshakeWithoutThrowing()
    {
        var weakKey = new EncryptionPublicKey(1, 0, 0, 0); // A low-order X25519 point.
        using var seedKey = SeedKey.NewEncryption();
        Assert.False(seedKey.TryDeriveKeyMaterial(weakKey, new byte[CryptoBox.SharedSecretSize]));

        var netTerminal = this.fixture.NetUnit.NetTerminal;
        var terminal = new ConnectionTerminal(this.fixture.NetUnit.ServiceProvider, netTerminal);
        var address = new NetAddress(IPAddress.Parse("10.1.2.5"), 12345);
        var node = new NetNode(address, weakKey);
        Assert.True(netTerminal.NetStats.TryCreateEndpoint(ref address, EndpointResolution.PreferIpv6, out var endpoint));
        var request = new ConnectPacket(seedKey.GetEncryptionPublicKey(), weakKey.GetHashCode(), default);
        var response = new ConnectPacketResponse(new ConnectionAgreement(), endpoint);
        Assert.Null(terminal.PrepareClientSide(node, endpoint, seedKey, weakKey, request, response));
    }

    [Fact]
    public void UnverifiedSourceNodeIsNotAddedToActiveNodes()
    {
        var netTerminal = this.fixture.NetUnit.NetTerminal;
        var nodeControl = netTerminal.NetStats.NodeControl;
        Assert.False(nodeControl.HasSufficientActiveNodes);

        var terminal = new ConnectionTerminal(this.fixture.NetUnit.ServiceProvider, netTerminal);
        using var clientKey = SeedKey.NewEncryption();
        var foreignNode = CreatePublicNode(1); // Neither the sender's key nor its address.
        Assert.True(foreignNode.Validate());
        var endpoint = new NetEndpoint(0, new IPEndPoint(IPAddress.Parse("8.8.8.8"), 5000));
        var request = new ConnectPacket(clientKey.GetEncryptionPublicKey(), netTerminal.NodePublicKey.GetHashCode(), foreignNode);
        var response = new ConnectPacketResponse(netTerminal.NetBase.DefaultAgreement, endpoint);

        var count = nodeControl.CountActive;
        Assert.True(terminal.PrepareServerSide(endpoint, request, response, 0));
        Assert.Equal(count, nodeControl.CountActive);
    }

    [Fact]
    public void FullLifelineListStillPrunesOfflineNodes()
    {
        var nodeControl = new NodeControl(this.fixture.NetUnit.NetBase);
        var nodes = Enumerable.Range(1, NodeControl.MaxLifelineNodes).Select(CreatePublicNode).ToArray();
        foreach (var x in nodes)
        {
            Assert.True(nodeControl.TryAddActiveNode(x));
        }

        nodeControl.MaintainLifelineNode(null); // Active -> Lifeline
        Assert.Equal(NodeControl.MaxLifelineNodes, nodeControl.CountLinfelineOnline);

        foreach (var x in nodes.Take(NodeControl.SufficientLifelineNodes))
        {
            nodeControl.ReportLifelineNodeConnection(x, ConnectionResult.Failure);
        }

        Assert.Equal(NodeControl.SufficientLifelineNodes, nodeControl.CountLinfelineOffline);

        // An active node that does not fit into the full lifeline list must not stop the pruning of offline nodes.
        nodeControl.ReportLifelineNodeConnection(CreatePublicNode(1000), ConnectionResult.Success);
        nodeControl.MaintainLifelineNode(null);
        Assert.Equal(NodeControl.SufficientLifelineNodes, nodeControl.CountLinfelineOnline + nodeControl.CountLinfelineOffline);
    }

    [Fact]
    public async Task ResponderFailureWithStructResponseIsReported()
    {
        this.fixture.NetUnit.Responders.Register(new FailingStructResponder());
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);

        var result = await connection.SendAndReceive<short, int>(1, 0, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(NetResult.InvalidData, result.Result);
    }

    [Fact]
    public async Task ThrowingAsyncResponderRepliesWithError()
    {
        this.fixture.NetUnit.Responders.Register(new ThrowingAsyncResponder());
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);

        var result = await connection.SendAndReceive<ushort, int>(1, 0, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(NetResult.UnknownError, result.Result);
    }

    [Fact]
    public async Task OversizedResponderResponseReportsBlockSizeLimit()
    {
        this.fixture.NetUnit.Responders.Register(new OversizedResponder());
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);

        var result = await connection.SendAndReceive<sbyte, byte[]>(1, 0, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(NetResult.BlockSizeLimit, result.Result);
    }

    [Fact]
    public async Task OversizedRpcResponseReportsBlockSizeLimit()
    {
        this.fixture.NetUnit.Services.EnableNetService<IAuditService>();
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var service = connection.GetService<IAuditService>();

        var result = await service.Allocate(NetFixture.MaxBlockSize + 1).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(NetResult.BlockSizeLimit, result.Result);

        result = await service.Allocate(10).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(NetResult.Success, result.Result);
        Assert.Equal(10, result.Value!.Length);
    }

    [Fact]
    public async Task ExternallyDisposedStreamIsNotReportedAsCompleted()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, null);
        transmission.SetState_ReceivingStream(100);
        var stream = new ReceiveStream(transmission, 0, 100);

        transmission.DisposeTransmission(); // For example, idle cleanup or connection termination.
        var result = await stream.Receive(new byte[10], TestContext.Current.CancellationToken);
        Assert.Equal(NetResult.Closed, result.Result);
    }

    [Fact]
    public async Task CompletedStreamStaysCompleted()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        using var transmission = new ReceiveTransmission(connection, uint.MaxValue, null, null);
        transmission.SetState_ReceivingStream(2);
        var stream = new ReceiveStream(transmission, 0, 2);
        var packet = BytePool.Default.Rent(14).AsMemory(0, 14);
        try
        {
            packet.Span.Clear();
            packet.Span[12] = 1;
            packet.Span[13] = 2;
            transmission.ProcessReceive_Gene(DataControl.Valid, 0, packet);

            var result = await stream.Receive(new byte[10], TestContext.Current.CancellationToken);
            Assert.Equal((NetResult.Completed, 2), result);
            result = await stream.Receive(new byte[10], TestContext.Current.CancellationToken);
            Assert.Equal((NetResult.Completed, 0), result);
        }
        finally
        {
            packet.Return();
        }
    }

    [Fact]
    public void KnockKeepsSlowStreamAlive()
    {
        var connection = this.CreateUnconnectedConnection();
        try
        {
            var stale = Mics.FastSystem - Mics.FromSeconds(10);

            // Receiver: the sender knocks because the local reader has not freed the window yet.
            var receiver = connection.TryCreateReceiveTransmission(1, null)!;
            receiver.SetState_ReceivingStream(100);
            receiver.ReceivedOrDisposedMics = stale;
            connection.LastEventMics = stale;
            Knock(receiver.TransmissionId);
            Assert.True(receiver.ReceivedOrDisposedMics > stale);
            Assert.True(connection.LastEventMics > stale);

            // A reader that stopped consuming must not keep the stream alive indefinitely.
            receiver.LastReadMics = Mics.FastSystem - NetConstants.MaxStreamReadStallMics - Mics.FromSeconds(1);
            receiver.ReceivedOrDisposedMics = stale;
            connection.LastEventMics = stale;
            Knock(receiver.TransmissionId);
            Assert.Equal(stale, receiver.ReceivedOrDisposedMics);
            Assert.Equal(stale, connection.LastEventMics);
            receiver.Dispose();

            // Sender: the peer answers the knock with a live window, so the stream is not idle.
            var sender = connection.TryCreateSendTransmission()!;
            Assert.Equal(NetResult.Success, sender.SendStream(100));
            sender.AckedMics = stale;
            connection.LastEventMics = stale;
            var response = BytePool.Default.Rent(sizeof(uint) + sizeof(int)).AsMemory(0, sizeof(uint) + sizeof(int));
            BitConverter.TryWriteBytes(response.Span, sender.TransmissionId);
            BitConverter.TryWriteBytes(response.Span.Slice(sizeof(uint)), 2);
            connection.ProcessReceive_KnockResponse(connection.DestinationEndpoint, response);
            response.Return();
            Assert.True(sender.AckedMics > stale);
            Assert.True(connection.LastEventMics > stale);
            sender.Dispose();
        }
        finally
        {// Also removes the connection's congestion control from the shared terminal.
            connection.CloseAllTransmission();
            connection.ChangeStateInternal(Connection.State.Disposed);
        }

        void Knock(uint transmissionId)
        {
            var knock = BytePool.Default.Rent(sizeof(uint)).AsMemory(0, sizeof(uint));
            BitConverter.TryWriteBytes(knock.Span, transmissionId);
            connection.ProcessReceive_Knock(connection.DestinationEndpoint, knock);
            knock.Return();
        }
    }

    [Fact]
    public async Task ResponderSuccessWithoutValueIsReported()
    {
        this.fixture.NetUnit.Responders.Register(new EmptySuccessResponder());
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);

        var result = await connection.SendAndReceive<long, string>(1, 0, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(NetResult.Success, result.Result);
        Assert.Null(result.Value);
    }

    [Fact]
    public void FailedSendIsRecorded()
    {
        using var client = this.CreateUnconnectedConnection(new ConnectionAgreement { MaxBlockSize = 1_000 });
        using var server = new ServerConnection(client);
        var context = new TransmissionContext(server, 42, 1, 0, default);

        Assert.Equal(NetResult.BlockSizeLimit, context.SendAndForget(new byte[2_000]));
        Assert.False(context.IsSent); // The unsent transmission was released, so an error reply can still be sent.
        Assert.Equal(NetResult.BlockSizeLimit, context.Result); // A fallback reply reports the failure instead of success.
        Assert.Equal(0, server.SendTransmissionsCount);
    }

    [Fact]
    public async Task ClosingDoesNotRunTokenCallbacksUnderTheConnectionLock()
    {
        var (terminal, connection, connections) = this.CreateTrackedConnection("10.1.2.6");
        var token = connection.CancellationToken;
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = token.Register(() =>
        {// Dispose takes the connection lock; running this inline under that lock would deadlock.
            connection.Dispose();
            disposed.TrySetResult();
        });

        await Task.Run(() => terminal.CloseInternal(connection, false), TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(token.IsCancellationRequested);

        using (connections.LockObject.EnterScope())
        {
            connection.ChangeStateInternal(Connection.State.Disposed);
            connection.Goshujin = null;
        }

        Assert.True(connection.CancellationToken.IsCancellationRequested); // Still readable after disposal.
    }

    [Fact]
    public async Task ForcedCloseKeepsReferencesOfReusedConnection()
    {
        var (terminal, connection, _) = this.CreateTrackedConnection("10.1.2.7");
        terminal.CloseInternal(connection, false); // For example, a Close frame from the peer.
        Assert.True(connection.IsClosed);

        var reused = await terminal.Connect(connection.DestinationNode, Connection.ConnectMode.ReuseOnly);
        Assert.NotNull(reused);
        Assert.Same(connection, reused);
        Assert.True(reused.IsOpen);

        connection.Dispose(); // The first holder releases its reference.
        Assert.True(reused.IsOpen);
        reused.Dispose();
        Assert.False(reused.IsOpen);
    }

    [Theory]
    [InlineData("::ffff:192.168.100.200")]
    [InlineData("::1.2.3.4")]
    [InlineData("fe80::5efe:10.0.0.1")]
    [InlineData("2001:db8::1")]
    [InlineData("2001:db8:1:2:3:4:5:6")]
    public void NetAddressStringLengthMatchesFormattedLength(string ipv6)
    {
        var address = new NetAddress(IPAddress.Parse("1.2.3.4"), IPAddress.Parse(ipv6), 1234);
        Span<char> buffer = stackalloc char[NetAddress.MaxStringLength];
        Assert.True(address.TryFormat(buffer, out var written));
        Assert.Equal(written, address.GetStringLength());
    }

    [Fact]
    public void NetNodeObjectEqualityMatchesTypedEquality()
    {
        var address = new NetAddress(IPAddress.Parse("8.8.8.8"), 5000);
        object first = new NetNode(address, Alternative.PublicKey);
        object second = new NetNode(address, Alternative.PublicKey);
        Assert.True(first.Equals(second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void MicsConversionsAreExact()
    {
        Assert.Equal(1_000_000, Mics.FromTimeSpan(TimeSpan.FromSeconds(1)));
        Assert.Equal(TimeSpan.FromSeconds(1), 1_000_000L.MicsToTimeSpan());

        var before = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMicrosecond;
        var now = Mics.GetUtcNow();
        var after = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMicrosecond;
        Assert.InRange(now, before, after);
    }

    [Fact]
    public async Task CanceledSendWaitReturnsCanceled()
    {
        using var connection = this.CreateUnconnectedConnection();
        using var transmission = new SendTransmission(connection, 42);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal(NetResult.Canceled, await transmission.Wait(new TaskCompletionSource<NetResult>().Task, -1, cancellation.Token));
    }

    [Fact]
    public void IdFileLoggerCanBeResolved()
    {// Resolve the constructor from DI, as the registered open generic does, but log into a temporary directory.
        var directory = Path.Combine(AppContext.BaseDirectory, $"logs-{Guid.NewGuid():N}");
        var options = new IdFileLoggerOptions { FilePath = Path.Combine(directory, "Log.txt") };
        try
        {
            Assert.NotNull(ActivatorUtilities.CreateInstance<IdFileLoggerFactory<IdFileLoggerOptions>>(this.fixture.NetUnit.ServiceProvider, options));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
            }
        }
    }

    private static NetNode CreatePublicNode(int index)
        => new(new NetAddress(new IPAddress(new byte[] { 8, 8, (byte)(index >> 8), (byte)index }), IPAddress.Parse($"2001:4860::{index:x}"), 5000), SeedKey.NewEncryption().GetEncryptionPublicKey());

    private ClientConnection CreateUnconnectedConnection(ConnectionAgreement? agreement = null)
    {
        var terminal = this.fixture.NetUnit.NetTerminal;
        var connection = new ClientConnection(terminal.PacketTerminal, terminal.ConnectionTerminal, 12345, Alternative.NetNode, new(0, new IPEndPoint(IPAddress.Loopback, 12345)));
        connection.Initialize(agreement ?? new ConnectionAgreement { MaxStreamLength = 1000, TransmissionTimeout = TimeSpan.FromSeconds(30) }, new byte[Connection.EmbryoSize]);
        return connection;
    }

    private (ConnectionTerminal Terminal, ClientConnection Connection, ClientConnection.GoshujinClass Connections) CreateTrackedConnection(string ipAddress)
    {
        var netTerminal = this.fixture.NetUnit.NetTerminal;
        var terminal = new ConnectionTerminal(this.fixture.NetUnit.ServiceProvider, netTerminal);
        var address = new NetAddress(IPAddress.Parse(ipAddress), 12345);
        var node = new NetNode(address, Alternative.PublicKey);
        Assert.True(netTerminal.NetStats.TryCreateEndpoint(ref address, EndpointResolution.PreferIpv6, out var endpoint));
        var seedKey = SeedKey.NewEncryption();
        var request = new ConnectPacket(seedKey.GetEncryptionPublicKey(), node.PublicKey.GetHashCode(), default);
        var response = new ConnectPacketResponse(new ConnectionAgreement(), endpoint);
        var connection = terminal.PrepareClientSide(node, endpoint, seedKey, node.PublicKey, request, response)!;
        var connections = (ClientConnection.GoshujinClass)typeof(ConnectionTerminal).GetField("clientConnections", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(terminal)!;
        using (connections.LockObject.EnterScope())
        {
            connection.IncrementOpenCount();
            connection.Goshujin = connections;
        }

        return (terminal, connection, connections);
    }

    private sealed class FailingStructResponder : SyncResponder<short, int>
    {
        public override NetResultAndValue<int> RespondSync(short value)
            => new(NetResult.InvalidData);
    }

    private sealed class EmptySuccessResponder : SyncResponder<long, string>
    {
        public override NetResultAndValue<string> RespondSync(long value)
            => new(NetResult.Success);
    }

    private sealed class ThrowingAsyncResponder : AsyncResponder<ushort, int>
    {
        public override NetResultAndValue<int> RespondAsync(ushort value)
            => throw new InvalidOperationException("Expected responder failure.");
    }

    private sealed class OversizedResponder : SyncResponder<sbyte, byte[]>
    {
        public override NetResultAndValue<byte[]> RespondSync(sbyte value)
            => new(new byte[NetFixture.MaxBlockSize + 1]);
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using Arc.Crypto;
using Netsphere;
using Netsphere.Crypto;
using Netsphere.Packet;
using Netsphere.Relay;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class RelayTest
{
    public RelayTest(NetFixture netFixture)
    {
        this.NetFixture = netFixture;

        this.dataArray = new byte[this.dataLength.Length][];
        for (var i = 0; i < this.dataLength.Length; i++)
        {
            var r = new Xoshiro256StarStar((ulong)this.dataLength[i]);
            this.dataArray[i] = new byte[this.dataLength[i]];
            r.NextBytes(this.dataArray[i]);
        }
    }

    private readonly int[] dataLength = [0, 1, 10, 111, 300, 1_000, 1_372, 1_373, 1_400, 3_000, 10_000, 100_000, 1_000_000, 1_500_000, 2_000_000,];
    private readonly byte[][] dataArray;

    [Fact]
    public async Task TestOutgoing()
    {
        var xo = new Xoshiro256StarStar(123);
        this.NetControl.Responders.Register(Netsphere.Responder.MemoryResponder.Instance);

        var netTerminal = this.NetControl.NetTerminal;
        var seedKey = SeedKey.NewSignature();
        if (netTerminal.RelayControl is CertificateRelayControl rc)
        {
            rc.SetCertificatePublicKey(seedKey.GetSignaturePublicKey());
        }

        var netNode = (await netTerminal.UnsafeGetNetNode(Alternative.NetAddress))!;
        netNode.IsNotNull();

        using (var relayConnection = (await netTerminal.ConnectForRelay(netNode, false, 0))!)
        {
            relayConnection.IsNotNull();

            var block = netTerminal.OutgoingCircuit.NewAssignRelayBlock();
            var token = new CertificateToken<AssignRelayBlock>(block);
            relayConnection.SignWithSalt(token, seedKey);
            var r = await relayConnection.SendAndReceive<CertificateToken<AssignRelayBlock>, AssignRelayResponse>(token);
            r.IsSuccess.IsTrue();
            r.Value.IsNotNull();

            var result = await netTerminal.OutgoingCircuit.AddRelay(block, r.Value!, relayConnection);
            result.Is(RelayResult.Success);
        }

        using (var relayConnection = (await netTerminal.ConnectForRelay(netNode, false, 1))!)
        {
            relayConnection.IsNotNull();

            var block = netTerminal.OutgoingCircuit.NewAssignRelayBlock();
            var token = new CertificateToken<AssignRelayBlock>(block);
            relayConnection.SignWithSalt(token, seedKey);
            var r = await relayConnection.SendAndReceive<CertificateToken<AssignRelayBlock>, AssignRelayResponse>(token);
            r.IsSuccess.IsTrue();
            r.Value.IsNotNull();

            var result = await netTerminal.OutgoingCircuit.AddRelay(block, r.Value!, relayConnection);
            result.Is(RelayResult.Success);
        }

        using (var connection = (await this.NetControl.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.ReuseIfAvailable, 2))!)
        {
            connection.IsNotNull();
            var basicService = connection.GetService<IBasicService>();
            var task = await basicService.SendInt(1).ResponseAsync;
            task.Result.Is(NetResult.Success);

            var task2 = await basicService.IncrementInt(2).ResponseAsync;
            task2.Result.Is(NetResult.Success);
            task2.Value.Is(3);

            task2 = await basicService.SumInt(3, 4).ResponseAsync;
            task2.Result.Is(NetResult.Success);
            task2.Value.Is(7);

            for (var i = 0; i < 10_000; i += 1_000)
            {
                var array = new byte[i];
                xo.NextBytes(array);
                var memory = await connection.SendAndReceive<Memory<byte>, Memory<byte>>(array.AsMemory());
                memory.Value.Span.SequenceEqual(array).IsTrue();
            }

            var r = await basicService.TestResult().ResponseAsync;
            r.Result.Is(NetResult.InvalidOperation);

            var r2 = await basicService.TestResult2().ResponseAsync;
            r2.Result.Is(NetResult.StreamLengthLimit);
            r2.Value.Is(NetResult.StreamLengthLimit);
        }

        var st = await netTerminal.OutgoingCircuit.UnsafeDetailedToString();

        using (var connection = (await this.NetControl.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse, 2))!)
        {
            var service = connection.GetService<IStreamService>();
            service.IsNotNull();
            if (service is null)
            {
                return;
            }

            await this.TestPutAndGetHash(service);
        }

        await netTerminal.OutgoingCircuit.Close();
    }

    // [Fact]
#pragma warning disable xUnit1013 // Public method should be marked as test
    public async Task TestIncoming()
#pragma warning restore xUnit1013 // Public method should be marked as test
    {
        var xo = new Xoshiro256StarStar(123);
        this.NetControl.Responders.Register(Netsphere.Responder.MemoryResponder.Instance);

        var netTerminal = this.NetControl.NetTerminal;
        var alternative = this.NetControl.Alternative!;
        var seedKey = SeedKey.NewSignature();
        if (netTerminal.RelayControl is CertificateRelayControl rc)
        {
            rc.SetCertificatePublicKey(seedKey.GetSignaturePublicKey());
        }

        // alternative.IncomingCircuit.AllowUnknownIncoming = true;
        alternative.IncomingCircuit.AllowOpenSesami = true;
        alternative.IncomingCircuit.AllowUnknownIncoming = true;
        var netNode = (await netTerminal.UnsafeGetNetNode(Alternative.NetAddress))!;
        netNode.IsNotNull();

        using (var relayConnection = (await alternative.ConnectForRelay(netNode, true, 0))!)
        {
            relayConnection.IsNotNull();

            var block = alternative.IncomingCircuit.NewAssignRelayBlock();
            var token = new CertificateToken<AssignRelayBlock>(block);
            relayConnection.SignWithSalt(token, seedKey);
            var r = await relayConnection.SendAndReceive<CertificateToken<AssignRelayBlock>, AssignRelayResponse>(token);
            r.IsSuccess.IsTrue();
            r.Value.IsNotNull();

            var result = await alternative.IncomingCircuit.AddRelay(block, r.Value!, relayConnection);
            result.Is(RelayResult.Success);
        }

        alternative.IncomingCircuit.TryGetOutermostAddress(out var netAddress).IsTrue();
        var peerNode = new NetNode(netAddress, netNode.PublicKey);

        // var rr = await netTerminal.PacketTerminal.SendAndReceive<PingPacket, PingPacketResponse>(peerNode.Address, new("test"));

        // var r2 = await this.NetControl.NetTerminal.PacketTerminal.SendAndReceive<PingPacket, PingPacketResponse>(Alternative.NetAddress, new());
        this.NetControl.NetStats.SetOwnNetNodeForTest(Alternative.NetAddress, alternative.NodePublicKey);
        NetAddress.SkipValidation = true;
        using (var connection = (await netTerminal.Connect(peerNode))!)
        {
            NetAddress.SkipValidation = false;
            connection.IsNotNull();

            var basicService = connection.GetService<IBasicService>();
            var task = await basicService.SendInt(1).ResponseAsync;
            task.Result.Is(NetResult.Success);
        }

        await alternative.IncomingCircuit.Close();
    }

    public NetFixture NetFixture { get; }

    public NetControl NetControl => this.NetFixture.NetControl;

    private async Task TestPutAndGetHash(IStreamService service)
    {
        var buffer = new byte[12_345];
        for (var i = 1; i < this.dataLength.Length; i++)
        {
            var stream = await service.PutAndGetHash(this.dataLength[i]);
            stream.IsNotNull();
            if (stream is null)
            {
                break;
            }

            var memory = this.dataArray[i].AsMemory();
            while (!memory.IsEmpty)
            {
                var length = Math.Min(buffer.Length, memory.Length);
                memory.Slice(0, length).CopyTo(buffer);
                memory = memory.Slice(length);

                var r = await stream.Send(buffer.AsMemory(0, length));
                r.Is(NetResult.Success);
            }

            var r2 = await stream.CompleteSendAndReceive();
            r2.Value.Is(FarmHash.Hash64(this.dataArray[i]));
        }
    }
}

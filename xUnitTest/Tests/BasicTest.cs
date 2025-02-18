// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Crypto;
using Netsphere;
using Netsphere.Packet;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class BasicTest
{
    public BasicTest(NetFixture netFixture)
    {
        this.NetFixture = netFixture;
    }

    [Fact]
    public async Task Test1()
    {
        var xo = new Xoshiro256StarStar(123);
        this.NetControl.Responders.Register(Netsphere.Responder.MemoryResponder.Instance);

        var p = new PingPacket("test56789");
        var result = await this.NetControl.NetTerminal.PacketTerminal.SendAndReceive<PingPacket, PingPacketResponse>(Alternative.NetAddress, p);
        result.Result.Is(NetResult.Success);

        using (var connection = (await this.NetControl.NetTerminal.Connect(Alternative.NetNode))!)
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

            var basicTaskService = connection.GetService<IBasicTaskService>();
            await basicTaskService.SendInt(1);
            (await basicTaskService.IncrementInt(2)).Is(3);
            (await basicTaskService.SumInt(2, 3)).Is(5);
            await basicTaskService.TestResult();
            (await basicTaskService.TestResult2()).Is(NetResult.StreamLengthLimit);
        }
    }

    public NetFixture NetFixture { get; }

    public NetControl NetControl => this.NetFixture.NetControl;
}

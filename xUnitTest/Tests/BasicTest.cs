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
        this.NetUnit.Responders.Register(Netsphere.Responder.MemoryResponder.Instance);

        var p = new PingPacket("test56789");
        var result = await this.NetUnit.NetTerminal.PacketTerminal.SendAndReceive<PingPacket, PingPacketResponse>(Alternative.NetAddress, p, 0, TestContext.Current.CancellationToken);
        result.Result.Is(NetResult.Success);

        using (var connection = (await this.NetUnit.NetTerminal.Connect(Alternative.NetNode))!)
        {
            connection.IsNotNull();
            var basicService = connection.GetService<IBasicService>();
            var task = await basicService.SendInt(1);
            task.Is(NetResult.Success);

            var task2 = await basicService.IncrementInt(2);
            task2.Is(3);

            task2 = await basicService.SumInt(3, 4);
            task2.Is(7);

            for (var i = 0; i < 10_000; i += 1_000)
            {
                var array = new byte[i];
                xo.NextBytes(array);
                var memory = await connection.SendAndReceive<Memory<byte>, Memory<byte>>(array.AsMemory(), 0, TestContext.Current.CancellationToken);
                memory.Value.Span.SequenceEqual(array).IsTrue();
            }

            var r = await basicService.TestResult();
            r.Is(NetResult.InvalidOperation);

            var r2 = await basicService.TestResult2();
            r2.Is(NetResult.StreamLengthLimit);

            var basicTaskService = connection.GetService<IBasicTaskService>();
            await basicTaskService.SendInt(1);
            (await basicTaskService.IncrementInt(2)).Is(3);
            (await basicTaskService.SumInt(2, 3)).Is(5);
            await basicTaskService.TestResult();
            (await basicTaskService.TestResult2()).Is(NetResult.StreamLengthLimit);

            var resultAndValue = await basicService.TestResult3(42);
            resultAndValue.Result.Is(NetResult.Completed);
            resultAndValue.Value.Is(42);

            var netUnion = new NetUnion<int, int>(2, default);
            basicService.SendInt2(netUnion);
        }
    }

    public NetFixture NetFixture { get; }

    public NetUnit NetUnit => this.NetFixture.NetUnit;
}

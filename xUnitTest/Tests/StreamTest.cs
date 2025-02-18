// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Crypto;
using Netsphere;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class StreamTest
{
    private readonly int[] dataLength = [0, 1, 10, 111, 300, 1_000, 1_372, 1_373, 1_400, 3_000, 10_000, 100_000, 1_000_000, 1_500_000, 2_000_000,];
    private readonly byte[][] dataArray;
    private readonly int maxLength;

    public StreamTest(NetFixture netFixture)
    {
        this.netFixture = netFixture;

        this.dataArray = new byte[this.dataLength.Length][];
        for (var i = 0; i < this.dataLength.Length; i++)
        {
            var r = new Xoshiro256StarStar((ulong)this.dataLength[i]);
            this.dataArray[i] = new byte[this.dataLength[i]];
            r.NextBytes(this.dataArray[i]);
        }

        this.maxLength = this.dataLength[this.dataLength.Length - 1];
    }

    private readonly NetFixture netFixture;

    [Fact]
    public async Task Test1()
    {
        using (var connection = await this.netFixture.NetControl.NetTerminal.Connect(Alternative.NetNode))
        {
            connection.IsNotNull();
            if (connection is null)
            {
                return;
            }

            var service = connection.GetService<IStreamService>();
            service.IsNotNull();
            if (service is null)
            {
                return;
            }

            await this.TestPingPing(service);
            await this.TestGetHash(service);
            await this.TestGet(service);
            await this.TestGet2(service);
            await this.TestPutAndGetHash(service);
            await this.TestPut2(service);
        }
    }

    private async Task TestPingPing(IStreamService service)
    {
        for (var i = 0; i < this.dataLength.Length; i++)
        {
            if (this.dataArray[i].Length <= NetFixture.MaxBlockSize)
            {
                var r = await service.Pingpong(this.dataArray[i]).ResponseAsync;
                r.Value!.SequenceEqual(this.dataArray[i]).IsTrue();
            }
        }
    }

    private async Task TestGetHash(IStreamService service)
    {
        for (var i = 0; i < this.dataLength.Length; i++)
        {
            if (this.dataArray[i].Length <= NetFixture.MaxBlockSize)
            {
                var r = await service.GetHash(this.dataArray[i]).ResponseAsync;
                r.Value.Is(FarmHash.Hash64(this.dataArray[i]));
            }
        }
    }

    private async Task TestGet(IStreamService service)
    {
        var buffer = new byte[this.maxLength];
        for (var i = 0; i < this.dataLength.Length; i++)
        {
            var stream = await service.Get("test", this.dataLength[i]);
            stream.IsNotNull();
            if (stream is null)
            {
                break;
            }

            var memory = buffer.AsMemory();
            var written = 0;
            while (true)
            {
                var r = await stream.Receive(memory);
                if (r.Result == NetResult.Success)
                {
                    memory = memory.Slice(r.Written);
                    written += r.Written;
                }
                else if (r.Result == NetResult.Completed)
                {
                    written += r.Written;
                    buffer.AsSpan(0, written).SequenceEqual(this.dataArray[i]).IsTrue();
                    break;
                }
                else
                {
                    throw new Exception();
                }
            }
        }
    }

    private async Task TestGet2(IStreamService service)
    {
        var buffer = new byte[10_000];
        for (var i = 0; i < this.dataLength.Length; i++)
        {
            if (this.dataLength[i] > 10_000)
            {
                break;
            }

            var stream = await service.Get("test", this.dataLength[i]);
            stream.IsNotNull();
            if (stream is null)
            {
                break;
            }

            var memory = buffer.AsMemory();
            var written = 0;
            while (true)
            {
                var r = await stream.Receive(memory.Slice(0, 4));
                if (r.Result == NetResult.Success)
                {
                    memory = memory.Slice(r.Written);
                    written += r.Written;
                }
                else if (r.Result == NetResult.Completed)
                {
                    written += r.Written;
                    buffer.AsSpan(0, written).SequenceEqual(this.dataArray[i]).IsTrue();
                    break;
                }
                else
                {
                    throw new Exception();
                }
            }
        }
    }

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

    private async Task TestPut2(IStreamService service)
    {
        for (var i = 0; i < this.dataLength.Length; i++)
        {
            var hash = FarmHash.Hash64(this.dataArray[i]);
            var sendStream = await service.Put2(hash, this.dataLength[i]);
            sendStream.IsNotNull();
            if (sendStream is null)
            {
                break;
            }

            var result = await sendStream.Send(this.dataArray[i]);
            result.Is(NetResult.Success);
            var resultValue = await sendStream.CompleteSendAndReceive();
            resultValue.Result.Is(NetResult.Success);
            resultValue.Value.Is(NetResult.Success);
        }
    }
}

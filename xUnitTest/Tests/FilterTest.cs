// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class FilterTest
{
    public FilterTest(NetFixture netFixture)
    {
        this.NetFixture = netFixture;
    }

    [Fact]
    public async Task Test1()
    {
        using (var connection = (await this.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse))!)
        {
            connection.IsNotNull();

            var service = connection.GetService<IFilterTestService>();
            var r = await service.NoFilter(1);
            r.Is(1);

            r = await service.Increment(2);
            r.Is(3);

            r = await service.Multiply2(3);
            r.Is(6);

            r = await service.Multiply3(3);
            r.Is(9);

            r = await service.IncrementAndMultiply2(4);
            r.Is(10);

            r = await service.Multiply2AndIncrement(5);
            r.Is(11);
        }
    }

    [Fact]
    public async Task Test1Reuse()
    {
        using (var connection = await this.NetUnit.NetTerminal.Connect(Alternative.NetNode))
        {
            if (connection is null)
            {
                return;
            }

            var service = connection.GetService<IFilterTestService>();
            var r = await service.NoFilter(1);
            r.Is(1);

            r = await service.Increment(2);
            r.Is(3);

            r = await service.Multiply2(3);
            r.Is(6);

            r = await service.Multiply3(3);
            r.Is(9);

            r = await service.IncrementAndMultiply2(4);
            r.Is(10);

            r = await service.Multiply2AndIncrement(5);
            r.Is(11);
        }
    }

    public NetFixture NetFixture { get; }

    public NetUnit NetUnit => this.NetFixture.NetUnit;
}

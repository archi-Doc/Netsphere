// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Crypto;
using Netsphere;
using Netsphere.Packet;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class BidirectionalTest
{
    public BidirectionalTest(NetFixture netFixture)
    {
        this.NetFixture = netFixture;
    }

    [Fact]
    public async Task Test1()
    {
        using (var connection = (await this.NetUnit.NetTerminal.Connect(Alternative.NetNode))!)
        {
            connection.IsNotNull();

            var serverConnection = connection.PrepareBidirectionalConnection();
            // serverConnection.GetContext().EnableNetService<IBidirectionalService>();

            var service = connection.GetService<IBidirectionalService>();
            (await service.Connect()).Is(NetResult.Success);

            var result = await IBidirectionalServiceImpl.Result.Task.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        }
    }

    public NetFixture NetFixture { get; }

    public NetUnit NetUnit => this.NetFixture.NetUnit;
}

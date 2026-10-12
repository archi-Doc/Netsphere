// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class GeneratorSignatureReviewTest
{
    private readonly NetFixture fixture;

    public GeneratorSignatureReviewTest(NetFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task NamedAndLongTupleParametersPreserveTheirSignatures()
    {
        this.fixture.NetUnit.Services.EnableNetService<IGeneratorSignatureReviewService>();
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);

        var service = connection.GetService<IGeneratorSignatureReviewService>();
        Assert.Equal((42, "tuple"), await service.Echo((42, "tuple")).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal((43, (string?)null), await service.Echo((43, null)).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal((1, 2, 3, 4, 5, 6, 7, 8), await service.EchoLong((1, 2, 3, 4, 5, 6, 7, 8)).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }
}

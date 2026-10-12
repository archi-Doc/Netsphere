// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class GeneratorHelperReviewTest
{
    private readonly NetFixture fixture;

    public GeneratorHelperReviewTest(NetFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task DefaultInterfaceMethodsCanUseLocalHelpers()
    {
        this.fixture.NetUnit.Services.EnableNetService<IGeneratorHelperReviewService>();
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);

        var service = connection.GetService<IGeneratorHelperReviewService>();
        Assert.Equal(15, await service.Calculate(7).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(23, await service.CalculateDirect(7).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }
}

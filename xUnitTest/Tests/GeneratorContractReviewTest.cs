// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class GeneratorContractReviewTest
{
    private readonly NetFixture fixture;

    public GeneratorContractReviewTest(NetFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task DuplicateInheritedContractsDispatchToTheirOwnImplementationsAndFilters()
    {
        this.fixture.NetUnit.Services.EnableNetService<IGeneratorContractReviewService>();
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var service = connection.GetService<IGeneratorContractReviewService>();
        var left = (ILeftGeneratorContract)service;
        var right = (IRightGeneratorContract)service;

        await Task.WhenAll(Enumerable.Range(0, 16).Select(async value =>
        {
            Assert.Equal(value + 11, await left.Echo(value));
            Assert.Equal((value * 2) + 20, await right.Echo(value));
        })).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(0, left.Status);
        Assert.Null(right.Status);
    }

    [Fact]
    public async Task ClassAndMethodFiltersRemainActiveAcrossInheritanceAndOverrides()
    {
        this.fixture.NetUnit.Services.EnableNetService<IInheritedFilterReviewService>();
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        var service = connection.GetService<IInheritedFilterReviewService>();

        Assert.Equal(8, await service.Inherited(3).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(26, await service.Overridden(3).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Equal(8, await service.ExplicitFilter(3).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }
}

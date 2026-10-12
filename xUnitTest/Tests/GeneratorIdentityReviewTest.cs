// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Crypto;
using Netsphere;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class GeneratorIdentityReviewTest
{
    private readonly NetFixture fixture;

    public GeneratorIdentityReviewTest(NetFixture fixture) => this.fixture = fixture;

    [Fact]
    public void ExplicitAndAutomaticServiceIdsMatchGeneratedMetadata()
    {
        CheckId<IConstructorIdentityReviewService>(0x7fc4e101);
        CheckId<INamedIdentityReviewService>(0x7fc4e102);
        CheckId<IOverriddenIdentityReviewService>(0x7fc4e104);
        CheckId<IAutomaticIdentityReviewService>((uint)FarmHash.Hash64(typeof(IAutomaticIdentityReviewService).FullName!));
        CheckId<GeneratorIdentityReviewContracts.INestedService>((uint)FarmHash.Hash64("xUnitTest.NetsphereTest.GeneratorIdentityReviewContracts.INestedService"));

        static void CheckId<TService>(uint expected)
            where TService : INetService
        {
            Assert.Equal(expected, StaticNetService.GetServiceId<TService>());
            Assert.Equal(expected, StaticNetService.GetServiceId(typeof(TService)));
            Assert.True(StaticNetService.ServiceInfoTable.TryGetValue(typeof(TService), out var serviceInfo));
            Assert.Equal(expected, serviceInfo.ServiceId);
        }
    }

    [Fact]
    public async Task ExplicitAndNestedServiceIdsDispatchToTheirHandlers()
    {
        this.fixture.NetUnit.Services.EnableNetService<IConstructorIdentityReviewService>();
        this.fixture.NetUnit.Services.EnableNetService<INamedIdentityReviewService>();
        this.fixture.NetUnit.Services.EnableNetService<IOverriddenIdentityReviewService>();
        this.fixture.NetUnit.Services.EnableNetService<IAutomaticIdentityReviewService>();
        this.fixture.NetUnit.Services.EnableNetService<GeneratorIdentityReviewContracts.INestedService>();
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);

        var responses = await Task.WhenAll(
            connection.GetService<IConstructorIdentityReviewService>().Echo(11),
            connection.GetService<INamedIdentityReviewService>().Echo(22),
            connection.GetService<IOverriddenIdentityReviewService>().Echo(33),
            connection.GetService<IAutomaticIdentityReviewService>().Echo(44),
            connection.GetService<GeneratorIdentityReviewContracts.INestedService>().Echo(55))
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(new[] { 11, 22, 33, 44, 55 }, responses);
    }
}

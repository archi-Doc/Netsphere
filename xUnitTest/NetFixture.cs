// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Threading;
using Arc.Unit;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;
using Netsphere.Relay;
using Xunit;

namespace xUnitTest.NetsphereTest;

[CollectionDefinition(NetFixtureCollection.Name)]
public class NetFixtureCollection : ICollectionFixture<NetFixture>
{
    public const string Name = "NetFixture";
}

public class NetFixture : IDisposable
{
    public const int MaxBlockSize = 1_000_000;
    public const long MaxStreamLength = 4_000_000;
    public const int StreamBufferSize = 1_000_000;
    public const long MinimumConnectionRetentionMics = 1_000_000_000_000;

    public NetFixture()
    {
        var builder = new NetUnit.Builder()
            .Configure(context =>
            {
                context.AddSingleton<IRelayControl, CertificateRelayControl>();

                // NetService
                context.AddSingleton<BasicServiceImpl>();
                // context.AddSingleton<BasicTaskServiceImpl>();

                // ServiceFilter
                context.AddSingleton<NullFilter>();
            })
            .ConfigureNetsphere(context =>
            {
                context.AddNetService<IBasicService, BasicServiceImpl>();
                context.AddNetService<IBasicTaskService, BasicTaskServiceImpl>();
                context.AddNetService<IFilterTestService, FilterTestServiceImpl>();
                context.AddNetService<IStreamService, StreamServiceImpl>();
            });

        var options = new NetOptions();
        options.EnableAlternative = true;
        options.EnablePing = true;
        options.EnableServer = true;
        options.NodeName = "Test";

        this.unit = builder.Build();
        this.unit.Run(options, true).Wait();

        this.NetUnit = this.unit.Context.ServiceProvider.GetRequiredService<NetUnit>();
        this.NetUnit.NetBase.DefaultTransmissionTimeout = TimeSpan.FromSeconds(1_000);
        this.NetUnit.NetBase.DefaultAgreement.MaxBlockSize = MaxBlockSize;
        this.NetUnit.NetBase.DefaultAgreement.MaxStreamLength = MaxStreamLength;
        this.NetUnit.NetBase.DefaultAgreement.StreamBufferSize = StreamBufferSize;
        this.NetUnit.NetBase.DefaultAgreement.MinimumConnectionRetentionMics = MinimumConnectionRetentionMics;
    }

    public void Dispose()
    {
        this.unit.Context.SendTerminateAsync(new()).Wait();
    }

    public NetUnit NetUnit { get; }

    private NetUnit.Unit unit;
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

extern alias DataServerAssembly;

using Arc.Collections;
using Arc.Unit;
using DataServerAssembly::RemoteDataServer;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;
using Netsphere.Core;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class RemoteDataReviewTest
{
    private readonly NetFixture fixture;

    public RemoteDataReviewTest(NetFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task UploadsToTheSameIdentifierUseIndependentTemporaryFiles()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"uploads-{Guid.NewGuid():N}");
        var control = this.CreateControl(directory);
        var path = Path.Combine(directory, "data");
        var unrelated = path + ".tmp";
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        using var server = new ServerConnection(connection);
        using var first = new ReceiveTransmission(server, 700, null, null);
        using var second = new ReceiveTransmission(server, 701, null, null);
        first.SetState_ReceivingStream(4);
        second.SetState_ReceivingStream(4);
        var firstContext = new TransmissionContext(server, 700, 0, 0, default);
        var secondContext = new TransmissionContext(server, 701, 0, 0, default);
        Assert.True(firstContext.CreateReceiveStream(first, 4));
        Assert.True(secondContext.CreateReceiveStream(second, 4));
        Task? firstUpload = null;
        try
        {
            await File.WriteAllTextAsync(unrelated, "keep", TestContext.Current.CancellationToken);
            firstUpload = Upload(control, firstContext, "data");
            Assert.False(firstUpload.IsCompleted);

            Complete(second, [5, 6, 7, 8]);
            await Upload(control, secondContext, "data").WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(NetResult.Success, secondContext.Result);
            Assert.Equal(new byte[] { 5, 6, 7, 8 }, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));

            Complete(first, [1, 2, 3, 4]);
            await firstUpload.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(NetResult.Success, firstContext.Result);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Equal("keep", await File.ReadAllTextAsync(unrelated, TestContext.Current.CancellationToken));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        }
        finally
        {
            first.Dispose();
            if (firstUpload is not null)
            {
                await firstUpload.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            }

            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../outside")]
    [InlineData("invalid\0name")]
    public async Task InvalidIdentifiersReturnAResultInsteadOfThrowing(string identifier)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"uploads-{Guid.NewGuid():N}");
        var control = this.CreateControl(directory);
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode, Connection.ConnectMode.NoReuse);
        Assert.NotNull(connection);
        using var server = new ServerConnection(connection);
        var context = new TransmissionContext(server, 702, 0, 0, default);
        var previous = TransmissionContext.AsyncLocal.Value;
        try
        {
            TransmissionContext.AsyncLocal.Value = context;
            Assert.Null(await control.Get(identifier));
            Assert.Equal(NetResult.NotFound, context.Result);
            Assert.Null(await control.Put(identifier, 0));
            Assert.Equal(NetResult.InvalidOperation, context.Result);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            TransmissionContext.AsyncLocal.Value = previous;
            Directory.Delete(directory);
        }
    }

    private static async Task Upload(RemoteDataControl control, TransmissionContext context, string identifier)
    {
        var previous = TransmissionContext.AsyncLocal.Value;
        try
        {
            TransmissionContext.AsyncLocal.Value = context;
            await control.Put(identifier, 4);
        }
        finally
        {
            TransmissionContext.AsyncLocal.Value = previous;
        }
    }

    private static void Complete(ReceiveTransmission transmission, byte[] data)
    {
        var packet = BytePool.Default.Rent(12 + data.Length).AsMemory(0, 12 + data.Length);
        try
        {
            packet.Span.Clear();
            data.CopyTo(packet.Span.Slice(12));
            transmission.ProcessReceive_Gene(DataControl.Valid, 0, packet);
        }
        finally
        {
            packet.Return();
        }
    }

    private RemoteDataControl CreateControl(string directory)
    {
        var options = this.fixture.NetUnit.ServiceProvider.GetRequiredService<UnitOptions>();
        var control = new RemoteDataControl(options, this.fixture.NetUnit.ServiceProvider.GetRequiredService<ILogger<RemoteDataControl>>());
        control.Initialize(directory);
        return control;
    }
}

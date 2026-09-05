// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Unit;
using Microsoft.Extensions.DependencyInjection;
using Netsphere.Logging;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class IdFileLoggerTest
{
    private readonly NetFixture fixture;

    public IdFileLoggerTest(NetFixture fixture)
        => this.fixture = fixture;

    [Theory]
    [InlineData("log", ".txt")]
    [InlineData("event", "")]
    public async Task StartupClearsOnlyMatchingLogIds(string prefix, string extension)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, $"logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var log = Path.Combine(directory, prefix + "0001" + extension);
        var unrelated = Path.Combine(directory, prefix + "notes" + extension);
        try
        {
            await File.WriteAllTextAsync(log, "old log", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(unrelated, "keep", TestContext.Current.CancellationToken);
            var logUnit = this.fixture.NetUnit.ServiceProvider.GetRequiredService<LogUnit>();
            using var worker = new IdFileLoggerWorker(this.fixture.NetUnit.NetTerminal.ExecutionGroup, logUnit.RootLogService, new IdFileLoggerOptions
            {
                Path = Path.Combine(directory, prefix + extension),
                ClearLogsAtStartup = true,
            });
            await worker.Sync();
            Assert.False(File.Exists(log));
            Assert.True(File.Exists(unrelated));
        }
        finally
        {
            File.Delete(log);
            File.Delete(unrelated);
            Directory.Delete(directory);
        }
    }
}

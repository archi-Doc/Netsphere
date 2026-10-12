// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

extern alias RunnerAssembly;

using Netsphere;
using RunnerAssembly::Netsphere.Runner;
using Xunit;

namespace xUnitTest.NetsphereTest;

[Collection(NetFixtureCollection.Name)]
public class RunnerHelperTest
{
    private readonly NetFixture fixture;

    public RunnerHelperTest(NetFixture fixture)
        => this.fixture = fixture;

    [Fact]
    public async Task DispatchDrainsLargeOutputAndPreservesQuotes()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        var path = Path.Combine(AppContext.BaseDirectory, $"runner output {Guid.NewGuid():N}.txt");
        const string expected = "value with \"double quotes\"";
        var command = OperatingSystem.IsWindows()
            ? $"'x' * 200000; [System.IO.File]::WriteAllText('{path.Replace("'", "''")}', '{expected}')"
            : $"head -c 200000 /dev/zero; printf '%s' '{expected}' > '{path.Replace("'", "'\"'\"'")}'";
        try
        {
            Assert.True(await RunnerHelper.DispatchCommand(connection.Logger, command).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken));
            Assert.Equal(expected, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(7, false)]
    public async Task DispatchReportsTheProcessExitStatus(int exitCode, bool expected)
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        var succeeded = await RunnerHelper.DispatchCommand(connection.Logger, $"exit {exitCode}")
            .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        Assert.Equal(expected, succeeded);
    }

    [Fact]
    public async Task QuotedArgumentsPreserveShellMetacharacters()
    {
        using var connection = await this.fixture.NetUnit.NetTerminal.Connect(Alternative.NetNode);
        Assert.NotNull(connection);
        var path = Path.Combine(AppContext.BaseDirectory, $"runner's output {Guid.NewGuid():N}.txt");
        const string expected = "literal 'quotes' \"double\" $value $(expression) `backtick`";
        var valueArgument = RunnerHelper.QuoteArgument(expected);
        var pathArgument = RunnerHelper.QuoteArgument(path);
        var command = OperatingSystem.IsWindows()
            ? $"[System.IO.File]::WriteAllText({pathArgument}, {valueArgument})"
            : $"printf '%s' {valueArgument} > {pathArgument}";
        try
        {
            Assert.True(await RunnerHelper.DispatchCommand(connection.Logger, command)
                .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken));
            Assert.Equal(expected, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

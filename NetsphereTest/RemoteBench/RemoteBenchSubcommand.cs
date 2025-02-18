// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Unit;
using Lp.NetServices;
using Netsphere.Crypto;
using Netsphere.Misc;
using Netsphere.Packet;
using SimpleCommandLine;

namespace NetsphereTest;

[SimpleCommand("remotebench")]
public class RemoteBenchSubcommand : ISimpleCommandAsync<RemoteBenchOptions>
{
    public RemoteBenchSubcommand(ILogger<RemoteBenchSubcommand> logger, NetControl netControl, NtpCorrection ntpCorrection)
    {
        this.logger = logger;
        this.netControl = netControl;
        this.ntpCorrection = ntpCorrection;
    }

    public async Task RunAsync(RemoteBenchOptions options, string[] args)
    {
        if (!NetNode.TryParse(options.Node, out var node, out _))
        {// NetNode.TryParseNetNode(this.logger, options.Node, out var node)
            if (!NetAddress.TryParse(this.logger, options.Node, out var address))
            {
                return;
            }

            node = await this.netControl.NetTerminal.UnsafeGetNetNode(address);
            if (node is null)
            {
                return;
            }
        }

        /*await Console.Out.WriteLineAsync("Wait about 3 seconds for the execution environment to stabilize.");
        try
        {
            await Task.Delay(3_000, ThreadCore.Root.CancellationToken);
        }
        catch
        {
            return;
        }*/

        // await this.TestPingpong(node);

        var connection = await this.netControl.NetTerminal.Connect(node);
        if (connection is null)
        {
            return;
        }

        var seedKey = SeedKey.NewSignature();
        var agreement = connection.Agreement with { EnableBidirectionalConnection = true, MinimumConnectionRetentionMics = Mics.FromMinutes(5), };
        var token = new CertificateToken<ConnectionAgreement>(agreement);
        connection.SignWithSalt(token, seedKey);
        connection.ValidateAndVerifyWithSalt(token);

        // var r = await connection.UpdateAgreement(token);
        // await Console.Out.WriteLineAsync($"{r}: {connection.Agreement}");

        // var r = await connection.ConnectBidirectionally(token);
        // await Console.Out.WriteLineAsync($"{r}: {connection.Agreement}");

        var service = connection.GetService<IRemoteBenchHost>();

        // var r = await service.UpdateAgreement(token);
        // await Console.Out.WriteLineAsync($"{r}: {connection.Agreement}");

        if (await service.ConnectBidirectionally(token) == NetResult.Success)
        {
            this.logger.TryGet()?.Log($"Register: Success");
        }
        else
        {
            this.logger.TryGet()?.Log($"Register: Failure");
            return;
        }

        var serverConnection = connection.BidirectionalConnection;
        if (serverConnection is null)
        {
            return;
        }

        var context = serverConnection.GetContext<TestConnectionContext>();

        try
        {
            await ThreadCore.Root.Delay(TimeSpan.FromMinutes(5)).WaitAsync(connection.CancellationToken);
        }
        catch
        {
        }

        /*while (true)
        {
            this.logger.TryGet()?.Log($"Waiting...");
            if (await this.remoteBenchBroker.Wait() == false)
            {
                Console.WriteLine($"Exit");
                break;
            }

            this.logger.TryGet()?.Log($"Benchmark {node.ToString()}, Total/Concurrent: {this.remoteBenchBroker.Total}/{this.remoteBenchBroker.Concurrent}");
            await this.remoteBenchBroker.Process(netControl.NetTerminal, node);
        }*/
    }

    private async Task TestPingpong(NetNode node)
    {
        const int N = 100;

        using (var connection = await this.netControl.NetTerminal.Connect(node))
        {
            if (connection is null)
            {
                return;
            }

            var sw = Stopwatch.StartNew();
            var service = connection.GetService<IRemoteBenchHost>();
            for (var i = 0; i < N; i++)
            {
                await service.Pingpong([0, 1, 2,]);
            }

            sw.Stop();

            this.logger.TryGet()?.Log($"Pingpong x {N} {sw.ElapsedMilliseconds} ms");
        }
    }

    private readonly NetControl netControl;
    private readonly NtpCorrection ntpCorrection;
    private readonly ILogger logger;
}

public record RemoteBenchOptions
{
    [SimpleOption("Node", Description = "Node address", Required = true)]
    public string Node { get; init; } = string.Empty;

    public override string ToString() => $"{this.Node}";
}

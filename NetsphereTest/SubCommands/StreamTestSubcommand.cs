// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Crypto;
using Arc.Unit;
using Lp.NetServices;
using SimpleCommandLine;

namespace NetsphereTest;

[SimpleCommand("stream")]
public class StreamTestSubcommand : ISimpleCommandAsync<StreamTestOptions>
{
    public StreamTestSubcommand(ILogger<StreamTestSubcommand> logger, NetTerminal netTerminal)
    {
        this.logger = logger;
        this.netTerminal = netTerminal;
    }

    public async Task RunAsync(StreamTestOptions options, string[] args)
    {
        var data = new byte[10_000_000];
        RandomVault.Xoshiro.NextBytes(data);
        var hash = FarmHash.Hash64(data);

        var r = await NetHelper.TryGetStreamService<IRemoteBenchHost>(this.netTerminal, options.Node, options.RemotePrivateKey, 100_000_000);
        if (r.Connection is null ||
            r.Service is null)
        {
            return;
        }

        try
        {
            this.logger.TryGet()?.Log($"IRemoteBenchHost.GetHash()");

            var sendStream = await r.Service.GetHash(data.Length);
            if (sendStream is null)
            {
                this.logger.TryGet(LogLevel.Error)?.Log($"No stream");
                return;
            }

            var result = await sendStream.Send(data);
            await Console.Out.WriteLineAsync(result.ToString());
            // result = await sendStream.Send(data);
            // await Console.Out.WriteLineAsync(result.ToString());
            var result2 = await sendStream.CompleteSendAndReceive();
            this.logger.TryGet(LogLevel.Information)?.Log((result2.Value == hash).ToString());
            if (result2.Result != NetResult.Success)
            {
                this.logger.TryGet(LogLevel.Error)?.Log(result2.Result.ToString());
            }
        }
        finally
        {
            r.Connection.Close();
        }
    }

    private readonly NetTerminal netTerminal;
    private readonly ILogger logger;
}

public record StreamTestOptions
{
    [SimpleOption("Node", Description = "Node address")]
    public string Node { get; init; } = string.Empty;

    [SimpleOption("RemotePrivatekey", Description = "Remote private key")]
    public string RemotePrivateKey { get; init; } = string.Empty;
}

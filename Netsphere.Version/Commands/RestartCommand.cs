// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;
using Netsphere.Interfaces;
using Netsphere.Packet;

namespace Lp.Subcommands;

[SimpleCommand("restart")]
public class RestartCommand : ISimpleCommandAsync<RestartOptions>
{
    private const int WaitIntervalInSeconds = 20;
    private const int PingIntervalInSeconds = 1;
    private const int PingRetries = 7;

    public RestartCommand(ILogger<RestartCommand> logger, NetTerminal terminal)
    {
        this.logger = logger;
        this.netTerminal = terminal;
    }

    public async Task RunAsync(RestartOptions options, string[] args)
    {
        options.Prepare();
        this.logger.TryGet()?.Log($"{options.ToString()}");

        if (options.RemoteSeedKey is not { } seedKey)
        {
            this.logger.TryGet(LogLevel.Fatal)?.Log($"Could not parse remote secret key");
            return;
        }

        var list = options.RunnerNode.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var nodeList = new List<NetNode>();
        foreach (var x in list)
        {
            if (NetNode.TryParseNetNode(this.logger, x, out var node))
            {
                nodeList.Add(node);
            }
        }

        var success = 0;
        await Parallel.ForEachAsync(nodeList, async (netNode, cancellationToken) =>
        {
            var endpointResolution = EndpointResolution.PreferIpv6;

            // Ping container
            var address = new NetAddress(netNode.Address, options.ContainerPort);
            if (options.IsValidContainerPort && await this.Ping(address, endpointResolution) == false)
            {// No ping
                if (address.IsValidIpv4AndIpv6)
                {
                    endpointResolution = EndpointResolution.Ipv4;
                    this.logger.TryGet()?.Log($"Ipv6 -> Ipv4");
                    if (await this.Ping(address, endpointResolution) == false)
                    {// No ping
                        return;
                    }
                }
            }

            // Restart
            using (var connection = await this.netTerminal.Connect(netNode, Connection.ConnectMode.ReuseIfAvailable, 0, endpointResolution))
            {
                if (connection == null)
                {
                    this.logger.TryGet()?.Log($"Could not connect {netNode.ToString()}");
                    return;
                }

                var token = AuthenticationToken.CreateAndSign(seedKey, connection);
                var result = await connection.SetAuthenticationToken(token).ConfigureAwait(false);
                if (result != NetResult.Success)
                {
                    return;
                }

                var service = connection.GetService<IRemoteControl>();
                result = await service.Restart();
                this.logger.TryGet()?.Log($"Restart({result}): {netNode.Address.ToString()}");
                if (result != NetResult.Success)
                {
                    return;
                }
            }

            if (!options.IsValidContainerPort)
            {
                success++;
                return;
            }

            // Wait
            // this.logger.TryGet()?.Log($"Waiting...");
            await Task.Delay(TimeSpan.FromSeconds(WaitIntervalInSeconds));

            // Ping container
            var sec = PingIntervalInSeconds;
            for (var i = 0; i < PingRetries; i++)
            {
                if (await this.Ping(address, endpointResolution))
                {
                    success++;
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(sec));
                sec *= 2;
            }
        });

        this.logger.TryGet()?.Log($"Restart Success/Total: {success}/{nodeList.Count}");
    }

    private async Task<bool> Ping(NetAddress address, EndpointResolution endpointResolution)
    {
        var r = await this.netTerminal.PacketTerminal.SendAndReceive<PingPacket, PingPacketResponse>(address, new(), 0, default, endpointResolution);
        this.logger.TryGet()?.Log($"Ping({r.Result}): {address.ToString()}");

        if (r.Result == NetResult.Success)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    private readonly ILogger logger;
    private readonly NetTerminal netTerminal;
}

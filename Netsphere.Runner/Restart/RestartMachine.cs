// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using Arc.Unit;
using BigMachines;
using Docker.DotNet;
using Docker.DotNet.Models;
using Netsphere.Packet;
using Netsphere.Stats;

namespace Netsphere.Runner;

[MachineObject(UseServiceProvider = true)]
public partial class RestartMachine : Machine
{
    private const int NoContainerRetries = 3;
    private const int CreateContainerInvervalInSeconds = 30;
    private const int CreateContainerRetries = 10;
    private const int CheckInvervalInSeconds = 10;
    private const int UnhealthyRetries = 3;
    private const int TerminatingInvervalInSeconds = 2;
    private const int TerminatingRetries = 30;

    public RestartMachine(ILogger<RestartMachine> logger, NetTerminal netTerminal)
    {
        this.logger = logger;
        this.netTerminal = netTerminal;
        this.options = default!;

        this.DefaultTimeout = TimeSpan.FromSeconds(CheckInvervalInSeconds);
    }

    protected override void OnCreate(object? createParam)
    {
        this.options = (RestartOptions)createParam!;
    }

    [StateMethod(0)]
    protected async Task<StateResult> Initial(StateParameter parameter)
    {
        if (!this.options.Check(this.logger))
        {
            return StateResult.Terminate;
        }

        this.dockerClient = await RunnerHelper.CreateDockerClient();
        if (this.dockerClient == null)
        {
            this.logger.TryGet(LogLevel.Fatal)?.Log($"Docker engine is not available");
            return StateResult.Terminate;
        }

        this.logger.TryGet()?.Log($"Netsphere.Runner (Restart)");
        this.logger.TryGet()?.Log($"{this.options.ToString()}");

        var address = await NetStatsHelper.GetOwnAddress((ushort)this.options.Port);
        if (address.IsValid)
        {
            var node = new NetNode(address, this.netTerminal.NetBase.NodePublicKey);
            this.logger.TryGet()?.Log($"{node.ToString()}");
        }

        this.logger.TryGet()?.Log($"Remote public key: {this.options.RemotePublicKey.ToString()}");

        var hostname = Environment.GetEnvironmentVariable("HOSTNAME");
        var containers = await this.dockerClient.Containers.ListContainersAsync(new ContainersListParameters { All = false });

        if (hostname is not null &&
            containers.FirstOrDefault(c => c.ID.StartsWith(hostname)) is { } myContainer)
        {
            Console.WriteLine($"My Container Name: {string.Join(", ", myContainer.Names)}");
            if (myContainer.Labels.TryGetValue("com.docker.compose.project", out var projectName))
            {
                Console.WriteLine($"Docker Compose Project Name: {projectName}");
            }
        }
        else
        {
            Console.WriteLine("Container not found.");
        }

        this.logger.TryGet()?.Log("Press Ctrl+C to exit, Ctrl+R to restart container, Ctrl+Q to stop container and exit");
        await Console.Out.WriteLineAsync();

        this.ChangeState(State.Main, true);
        return StateResult.Continue;
    }

    [StateMethod]
    protected async Task<StateResult> Main(StateParameter parameter)
    {
        if (this.dockerClient == null)
        {
            return StateResult.Terminate;
        }

        /*var containers = await this.docker.EnumerateContainersAsync2();
        foreach (var x in containers)
        {
            this.logger.TryGet()?.Log($"{x.Image} {x.State}");
        }*/

        this.TimeUntilRun = TimeSpan.FromSeconds(CreateContainerInvervalInSeconds);
        return StateResult.Continue;
    }

    private readonly ILogger logger;
    private readonly NetTerminal netTerminal;
    private RestartOptions options;
    private DockerClient? dockerClient;
}

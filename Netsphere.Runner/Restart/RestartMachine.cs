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
    private const int ListContainersLimit = 100;

    public RestartMachine(ILogger<RestartMachine> logger, NetTerminal netTerminal)
    {
        this.logger = logger;
        this.netTerminal = netTerminal;
        this.options = default!;

        this.DefaultTimeout = TimeSpan.FromSeconds(5);
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
            // Console.WriteLine($"My Container Name: {string.Join(", ", myContainer.Names)}");
            if (myContainer.Labels.TryGetValue("com.docker.compose.project", out var projectName))
            {
                this.projectName = projectName;
                this.logger.TryGet()?.Log($"Docker Compose Project Name: {projectName}");
            }
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

        var containerName = string.IsNullOrEmpty(this.projectName) ? this.options.Service : $"{this.projectName}-{this.options.Service}";
        Console.WriteLine($"Container name: {containerName}");
        var list = await this.dockerClient.Containers.ListContainersAsync(new() { Limit = ListContainersLimit, });
        foreach(var x in list)
        {// list.Where(x => x.Image.StartsWith(this.options.Image)
            this.logger.TryGet()?.Log($"{x.Image} {x.State}");
            foreach (var y in x.Names)
            {
                this.logger.TryGet()?.Log($"{y}");
            }
        }

        return StateResult.Continue;
    }

    private readonly ILogger logger;
    private readonly NetTerminal netTerminal;
    private RestartOptions options;
    private DockerClient? dockerClient;
    private string projectName = string.Empty;
}

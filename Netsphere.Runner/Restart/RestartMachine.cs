// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using Arc.Unit;
using BigMachines;
using Docker.DotNet;
using Docker.DotNet.Models;
using Netsphere.Stats;

namespace Netsphere.Runner;

[MachineObject(UseServiceProvider = true)]
public partial class RestartMachine : Machine
{
    private const int ListContainersLimit = 100;
    private const string ConfigFile = "/target.yml";

    private readonly ILogger logger;
    private readonly NetTerminal netTerminal;
    private RestartOptions options;
    private DockerClient? docker;
    private string projectName = string.Empty;

    // public string ContainerName => string.IsNullOrEmpty(this.projectName) ? $"/{this.options.Service}" : $"/{this.projectName}-{this.options.Service}";

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

        this.docker = await RunnerHelper.CreateDockerClient();
        if (this.docker == null)
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
        var containers = await this.docker.Containers.ListContainersAsync(new ContainersListParameters { All = false });
        if (hostname is not null &&
            containers.FirstOrDefault(c => c.ID.StartsWith(hostname)) is { } myContainer)
        {
            if (myContainer.Labels.TryGetValue("com.docker.compose.project", out var projectName))
            {
                this.projectName = projectName;
                this.logger.TryGet()?.Log($"Restart project name: {projectName}");
            }
        }

        // this.logger.TryGet()?.Log($"Target Container Name: {this.ContainerName}");

        Console.WriteLine();
        Console.WriteLine("Press Ctrl+C to exit, Ctrl+R to restart container, Ctrl+Q to stop container and exit");
        await Console.Out.WriteLineAsync();

        this.ChangeState(State.Main, true);
        return StateResult.Continue;
    }

    [StateMethod]
    protected async Task<StateResult> Main(StateParameter parameter)
    {
        if (this.docker == null)
        {
            return StateResult.Terminate;
        }

        /*var containerName = string.IsNullOrEmpty(this.projectName) ? $"/{this.options.Service}" : $"/{this.projectName}-{this.options.Service}";
        Console.WriteLine($"Container name: {containerName}");
        var list = await this.dockerClient.Containers.ListContainersAsync(new() { Limit = ListContainersLimit, });
        foreach(var x in list)
        {// list.Where(x => x.Image.StartsWith(this.options.Image)
            this.logger.TryGet()?.Log($"{x.Image} {x.State}");
            foreach (var y in x.Names)
            {
                this.logger.TryGet()?.Log($"{y}");
            }

            if (x.Names.Any(z => z.StartsWith(containerName)))
            {
                // await this.dockerClient.Containers.RestartContainerAsync(x.ID, new());
            }
        }*/

        return StateResult.Continue;
    }

    [CommandMethod]
    protected async Task<CommandResult> Restart()
    {
        this.logger.TryGet()?.Log("Restart");

        if (this.docker is null)
        {
            return CommandResult.Failure;
        }

        var projectName = this.projectName;
        if (string.IsNullOrEmpty(this.options.Project))
        {
            if (string.IsNullOrEmpty(this.options.Service))
            {
                this.logger.TryGet(LogLevel.Error)?.Log("Project and service are not specified.");
                return CommandResult.Failure;
            }
            else
            {// Service
            }
        }
        else
        {// Project
            projectName = this.options.Project;
        }

        var list = await this.docker.Containers.ListContainersAsync(new() { Limit = ListContainersLimit, });
        if (string.IsNullOrEmpty(projectName))
        {
            var r = list.FirstOrDefault(x => x.State == "running" && x.Names.Any(n => n.Contains($"-{this.options.Service}")));
            if (r is null ||
                !r.Labels.TryGetValue("com.docker.compose.project", out var name))
            {
                this.logger.TryGet(LogLevel.Error)?.Log("Project not found.");
                return CommandResult.Failure;
            }

            projectName = name;
        }

        // Prefix
        string prefix;
        if (string.IsNullOrEmpty(this.options.Service))
        {// Restart project
            prefix = $"/{projectName}";
        }
        else
        {// Restart service
            prefix = $"/{projectName}-{this.options.Service}";
        }

        var container = list.FirstOrDefault(x => x.State == "running" && x.Names.Any(n => n.StartsWith(prefix)));
        if (container is null)
        {
            this.logger.TryGet(LogLevel.Error)?.Log("Container not found.");
            return CommandResult.Failure;
        }

        if (!string.IsNullOrEmpty(this.projectName))
        {// Check configuration file
            foreach (var x in container.Mounts)
            {
                this.logger.TryGet()?.Log($"{x.Source} : {x.Destination}");
            }

            var mount = container.Mounts.FirstOrDefault(x => x.Destination == ConfigFile);
            var source = mount?.Source;
            this.logger.TryGet()?.Log($"Mount source: {source}");

            if (container.Labels.TryGetValue("com.docker.compose.project.config_files", out var config))
            {
                this.logger.TryGet()?.Log($"Config: {config}");
            }
        }

        if (string.IsNullOrEmpty(this.options.Service))
        {// Restart project
            // await this.RestarProject(container);
        }
        else
        {// Restart service
            await this.RestarService(container);
        }

        return CommandResult.Success;
    }

    private async Task RestarService(ContainerListResponse targetContainer)
    {
        if (this.docker is null)
        {
            return;
        }

        var inspect = await this.docker.Containers.InspectContainerAsync(targetContainer.ID);

        if (!inspect.Config.Labels.TryGetValue("com.docker.compose.project", out var projectName))
        {
            return;
        }

        this.logger.TryGet()?.Log($"Project: {projectName}");

        if (!this.TryGetConfigurationFile(inspect, out var configFile))
        {
            this.logger.TryGet(LogLevel.Error)?.Log($"Project: {projectName}");
            return;
        }

        this.logger.TryGet()?.Log($"Config: {configFile}");

        // Stop and remove container
        await RunnerHelper.DispatchCommand(this.logger, $"docker compose -p {projectName} rm -sf {this.options.Service}");
        Console.WriteLine("\r");

        // Create and start container
        await RunnerHelper.DispatchCommand(this.logger, $"docker compose -p {projectName} -f {configFile} up -d --build {this.options.Service}");
        Console.WriteLine("\r");

        this.logger.TryGet()?.Log($"Restart complete");
        Console.WriteLine();
    }

    private bool TryGetConfigurationFile(ContainerInspectResponse inspect, [MaybeNullWhen(false)] out string configurationFile)
    {
        if (inspect.Config.Labels.TryGetValue("com.docker.compose.project.config_files", out var path))
        {
            if (RunnerHelper.CanReadFile(path))
            {
                configurationFile = path;
                return true;
            }
        }

        if (RunnerHelper.CanReadFile(ConfigFile))
        {
            configurationFile = ConfigFile;
            return true;
        }

        configurationFile = default;
        return false;
    }

    private async Task RestartContainer(ContainerListResponse targetContainer)
    {
        if (this.docker is null)
        {
            return;
        }

        var inspect = await this.docker.Containers.InspectContainerAsync(targetContainer.ID);

        // Stop container
        await this.docker.Containers.StopContainerAsync(targetContainer.ID, new ContainerStopParameters());

        // Remove container
        await this.docker.Containers.RemoveContainerAsync(targetContainer.ID, new());

        // Create container
        var createParams = new CreateContainerParameters(inspect.Config);
        createParams.Name = targetContainer.Names.First().Trim('/');
        var created = await this.docker.Containers.CreateContainerAsync(createParams);

        // Start container
        await this.docker.Containers.StartContainerAsync(created.ID, new ContainerStartParameters());
    }
}

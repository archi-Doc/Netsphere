// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using Arc.Unit;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Netsphere.Runner;

internal class DockerRunner
{
    private const int ListContainersLimit = 100;

    public static async Task<DockerRunner?> Create(ILogger logger, RunnerOptions options)
    {
        var client = new DockerClientConfiguration().CreateClient();
        try
        {
            _ = await client.Containers.ListContainersAsync(new() { Limit = 10, });
        }
        catch
        {// No docker
            return null;
        }

        return new DockerRunner(client, logger, options);
    }

    private DockerRunner(DockerClient client, ILogger logger, RunnerOptions options)
    {
        this.client = client;
        this.logger = logger;
        this.options = options;
    }

    public async Task<IEnumerable<ContainerListResponse>> EnumerateContainersAsync()
    {
        var list = await this.client.Containers.ListContainersAsync(new() { Limit = ListContainersLimit, });
        return list.Where(x => x.Image.StartsWith(this.options.Image));
    }

    public async Task<int> CountContainersAsync()
    {
        var list = await this.client.Containers.ListContainersAsync(new() { Limit = ListContainersLimit, });
        return list.Count(x => x.Image.StartsWith(this.options.Image));
    }

    public async Task<(bool IsRunning, IPAddress? Address)> GetContainer()
    {
        var list = await this.client.Containers.ListContainersAsync(new() { Limit = ListContainersLimit, });
        foreach (var x in list)
        {
            if (x.Image.StartsWith(this.options.Image))
            {
                IPAddress.TryParse(x.NetworkSettings.Networks.FirstOrDefault().Value.IPAddress, out var address);
                return (true, address);
            }
        }

        return default;
    }

    public async Task RemoveAllContainers()
    {// Remove containers
        var array = (await this.EnumerateContainersAsync()).ToArray();
        foreach (var x in array)
        {
            try
            {
                await this.client.Containers.StopContainerAsync(x.ID, new());
            }
            catch
            {
            }

            try
            {
                await this.client.Containers.RemoveContainerAsync(x.ID, new());
                this.logger.TryGet()?.Log($"Container removed: {x.ID}");
                // this.logger.TryGet()?.Log("Success");
            }
            catch
            {
                // this.logger.TryGet()?.Log("Failure");
            }
        }
    }

    public async Task<bool> RunContainer()
    {
        // Create image
        this.logger.TryGet()?.Log($"Pull image: {this.options.Image}");

        string image = this.options.Image;
        string tag = string.Empty;
        var parts = image.Split(':');
        if (parts.Length == 2)
        {
            image = parts[0];
            tag = parts[1];
        }

        var progress = new Progress<JSONMessage>();
        try
        {
            await this.client.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = image,
                    Tag = tag,
                },
                null,
                progress);

            // this.logger.TryGet()?.Log("Success");
        }
        catch
        {
            // this.logger.TryGet()?.Log("Failure");
            return false;
        }

        // Create container
        this.logger.TryGet()?.Log($"Start container: {this.options.Image}");

        var command = $"docker run {this.options.DockerParameters} {this.options.Image} {this.options.ContainerParameters}"; // -i: key input, -t: , -d: leave the container running
        RunnerHelper.DispatchCommand(this.logger, command);

        /*try
        {
        // var exposedPort = this.information.DestinationPort.ToString() + "/udp";
            var containerResponse = await this.client.Containers.CreateContainerAsync(new()
            {// docker run -it --mount type=bind,source=$(pwd)/lp,destination=/lp --rm -p 49152:49152/udp
                Image = this.information.Image,
                // WorkingDir = "c:\\app\\docker", // this.information.Directory,
                AttachStdin = true,
                AttachStderr = true,
                AttachStdout = true,
                Tty = true,
                Cmd = new[] { "-rootdir \"/lp\" -ns [-port 49152 -test true -alternative false]" },
                ExposedPorts = new Dictionary<string, EmptyStruct> { { exposedPort, default } },
                HostConfig = new HostConfig
                {
                    Mounts = new Mount[]
                    {
                        // new Mount() { Type = "bind", Source = "/home/ubuntu/lp", Target = "/lp", },
                        new Mount() { Type = "bind", Source = "C:\\App\\docker", Target = "/lp", },
                    },

                    PortBindings = new Dictionary<string, IList<PortBinding>>
                    {
                        { exposedPort, new List<PortBinding> { new PortBinding { HostIP = "localhost", HostPort = this.information.TargetPort.ToString() + "/udp" } } },
                    },
                },
            });

            await this.client.Containers.StartContainerAsync(containerResponse.ID, new());
            this.logger.TryGet()?.Log($"Success: {containerResponse.ID}");
        }
        catch
        {
            this.logger.TryGet()?.Log("Failure");
            return false;
        }*/

        return true;
    }

    /*public async Task RestartContainer()
    {
        var array = (await this.EnumerateContainersAsync()).ToArray();
        foreach (var x in array)
        {
            if (x.State == "created" || x.State == "exited")
            {
                this.logger.TryGet()?.Log($"Restart container: {x.ID}");
                try
                {
                    await this.client.Containers.StartContainerAsync(x.ID, new());
                }
                catch
                {
                }
            }
        }
    }*/

    private readonly DockerClient client;
    private readonly ILogger logger;
    private readonly RunnerOptions options;
}

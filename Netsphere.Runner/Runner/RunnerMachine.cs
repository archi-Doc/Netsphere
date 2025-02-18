// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Net;
using Arc.Unit;
using BigMachines;
using Netsphere.Packet;
using Netsphere.Stats;

namespace Netsphere.Runner;

[MachineObject(UseServiceProvider = true)]
public partial class RunnerMachine : Machine
{
    private const int NoContainerRetries = 3;
    private const int CreateContainerInvervalInSeconds = 30;
    private const int CreateContainerRetries = 10;
    private const int CheckInvervalInSeconds = 10;
    private const int UnhealthyRetries = 3;
    private const int TerminatingInvervalInSeconds = 2;
    private const int TerminatingRetries = 30;

    public RunnerMachine(ILogger<RunnerMachine> logger, NetTerminal netTerminal, RunnerOptions options)
    {
        this.logger = logger;
        this.netTerminal = netTerminal;
        this.options = options;

        this.DefaultTimeout = TimeSpan.FromSeconds(CheckInvervalInSeconds);
    }

    [StateMethod(0)]
    protected async Task<StateResult> Initial(StateParameter parameter)
    {
        if (!this.options.Check(this.logger))
        {
            return StateResult.Terminate;
        }

        this.docker = await DockerRunner.Create(this.logger, this.options);
        if (this.docker == null)
        {
            this.logger.TryGet(LogLevel.Fatal)?.Log($"Docker engine is not available");
            return StateResult.Terminate;
        }

        this.logger.TryGet()?.Log($"Runner start");
        this.logger.TryGet()?.Log($"{this.options.ToString()}");

        var address = await NetStatsHelper.GetOwnAddress((ushort)this.options.Port);
        if (address.IsValid)
        {
            var node = new NetNode(address, this.netTerminal.NetBase.NodePublicKey);
            this.logger.TryGet()?.Log($"{node.ToString()}");
        }

        this.logger.TryGet()?.Log($"Remote public key: {this.options.RemotePublicKey.ToString()}");

        this.logger.TryGet()?.Log("Press Ctrl+C to exit, Ctrl+R to restart container, Ctrl+Q to stop container and exit");
        await Console.Out.WriteLineAsync();

        // Remove container
        // await this.docker.RemoveAllContainers();

        this.ChangeStateAndRunImmediately(State.NoContainer);
        return StateResult.Continue;
    }

    [StateMethod]
    protected async Task<StateResult> NoContainer(StateParameter parameter)
    {// No active containers: Start a container and wait for a specified period. If the container is created, transition to Running; if not, retry a set number of times.
        if (this.docker == null)
        {
            return StateResult.Terminate;
        }

        if (await this.docker.CountContainersAsync() > 0)
        {
            this.ChangeStateAndRunImmediately(State.Running);
            return StateResult.Continue;
        }

        if (this.retries++ > NoContainerRetries)
        {
            return StateResult.Terminate;
        }

        if (this.createContainerRetries++ > CreateContainerRetries)
        {
            return StateResult.Terminate;
        }

        this.logger.TryGet()?.Log($"Status({this.retries}): {this.GetState()} -> Create container");

        if (await this.docker.RunContainer() == false)
        {
            return StateResult.Terminate;
        }

        this.TimeUntilRun = TimeSpan.FromSeconds(CreateContainerInvervalInSeconds);
        return StateResult.Continue;
    }

    [StateMethod]
    protected async Task<StateResult> Running(StateParameter parameter)
    {// Container is running: Check the number of containers. If none exist, transition to NoContainer. If there are containers and the ContainerPort is active, proceed to CheckHealth.
        if (this.docker == null)
        {
            return StateResult.Terminate;
        }

        this.logger.TryGet()?.Log($"Running");

        if (await this.docker.CountContainersAsync() == 0)
        {// No container
            this.ChangeStateAndRunImmediately(State.NoContainer);
            return StateResult.Continue;
        }
        else if (this.options.ContainerPort != 0)
        {// Check health
            this.ChangeStateAndRunImmediately(State.CheckHealth);
            return StateResult.Continue;
        }

        this.TimeUntilRun = TimeSpan.FromSeconds(CheckInvervalInSeconds);
        return StateResult.Continue;
    }

    [StateMethod]
    protected async Task<StateResult> CheckHealth(StateParameter parameter)
    {
        if (this.docker == null)
        {
            return StateResult.Terminate;
        }

        if (this.options.ContainerPort == 0)
        {
            this.ChangeStateAndRunImmediately(State.Running);
            return StateResult.Continue;
        }

        var r = await this.docker.GetContainer();
        if (!r.IsRunning || r.Address is null)
        {
            this.ChangeStateAndRunImmediately(State.NoContainer);
            return StateResult.Continue;
        }

        var result = await this.Ping(r.Address);
        if (result == NetResult.Success)
        {// Healthy
            this.retries = 0;
            this.logger.TryGet()?.Log($"Status({this.retries}): Healthy");
        }
        else
        {// Unhealthy
            if (this.retries++ >= UnhealthyRetries)
            {
                this.logger.TryGet()?.Log($"Status({this.retries}): Unhealthy -> Restart");
                await this.docker.RemoveAllContainers();
                this.ChangeStateAndRunImmediately(State.NoContainer);
                return StateResult.Continue;
            }
            else
            {
                this.logger.TryGet()?.Log($"Status({this.retries}): Unhealthy");
            }
        }

        this.TimeUntilRun = TimeSpan.FromSeconds(CheckInvervalInSeconds);
        return StateResult.Continue;
    }

    [StateMethod]
    protected async Task<StateResult> Terminating(StateParameter parameter)
    {// Waiting for all containers to terminate.
        if (this.docker == null)
        {
            return StateResult.Terminate;
        }

        if (await this.docker.CountContainersAsync() == 0)
        {// No container
            this.ChangeStateAndRunImmediately(State.NoContainer);
            return StateResult.Continue;
        }

        if (this.retries++ > TerminatingRetries)
        {
            return StateResult.Terminate;
        }

        this.logger.TryGet()?.Log($"Status({this.retries}): {this.GetState()}");
        this.TimeUntilRun = TimeSpan.FromSeconds(TerminatingInvervalInSeconds);
        return StateResult.Continue;
    }

    [CommandMethod]
    protected async Task<CommandResult> Restart()
    {
        this.logger.TryGet()?.Log("Restart");

        var state = this.GetState();
        if (state == State.NoContainer ||
            state == State.Terminating)
        {
            return CommandResult.Success;
        }

        // Remove container
        if (this.docker != null)
        {
            await this.docker.RemoveAllContainers();
        }

        this.createContainerRetries = 0;
        this.ChangeState(State.Terminating);
        this.TimeUntilRun = TimeSpan.FromSeconds(TerminatingInvervalInSeconds);
        return CommandResult.Success;
    }

    [CommandMethod]
    protected async Task<CommandResult> StopAll()
    {
        this.logger.TryGet()?.Log("Stop all containers");

        // Remove container
        if (this.docker != null)
        {
            await this.docker.RemoveAllContainers();
        }

        return CommandResult.Success;
    }

    private void ChangeStateAndRunImmediately(State state)
    {
        this.retries = 0;
        this.ChangeState(state, true);
    }

    private async Task<NetResult> Ping(IPAddress addresss)
    {
        NetAddress netAddress;
        if (PathHelper.RunningInContainer)
        {// In container. Use Container address.
            netAddress = new NetAddress(addresss, this.options.ContainerPort);
        }
        else
        {// Non-container. Use Loopback address.
            netAddress = new NetAddress(IPAddress.Loopback, this.options.ContainerPort);
        }

        var r = await this.netTerminal.PacketTerminal.SendAndReceive<PingPacket, PingPacketResponse>(netAddress, new());
        // this.logger.TryGet()?.Log($"Ping: {r.Result}");

        return r.Result;
    }

    private readonly ILogger logger;
    private readonly NetTerminal netTerminal;
    private readonly RunnerOptions options;
    private DockerRunner? docker;
    private int retries;
    private int createContainerRetries;
}

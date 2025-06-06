﻿// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Unit;
using Netsphere.Crypto;
using Netsphere.Interfaces;

namespace Netsphere.Runner;

internal class RemoteControlBase
{
    public SignaturePublicKey RemotePublicKey { get; set; }
}

[NetServiceObject]
internal class RemoteControlAgent : IRemoteControl
{// Remote -> Netsphere.Runner
    public RemoteControlAgent(ILogger<RemoteControlAgent> logger, BigMachine bigMachine, RemoteControlBase remoteControlBase)
    {
        this.logger = logger;
        this.bigMachine = bigMachine;
        this.remoteControlBase = remoteControlBase;
    }

    /*public async NetTask<NetResult> Authenticate(AuthenticationToken token)
        => TransmissionContext.Current.ServerConnection.GetContext().Authenticate(token, this.runOptions.RemotePublicKey);*/

    public async NetTask<NetResult> Restart()
    {
        if (!TransmissionContext.Current.AuthenticationTokenEquals(this.remoteControlBase.RemotePublicKey))
        {
            return NetResult.NotAuthenticated;
        }

        var machine = this.bigMachine.RunMachine.GetOrCreate();
        _ = machine.Command.Restart();

        var machine2 = this.bigMachine.RestartMachine.GetOrCreate();
        _ = machine2.Command.Restart();

        return NetResult.Success;

        /*var address = this.information.TryGetDualAddress();
        if (!address.IsValid)
        {
            return NetResult.NoNodeInformation;
        }

        var netTerminal = this.netControl.NetTerminal;
        var netNode = await netTerminal.UnsafeGetNetNode(address);
        if (netNode is null)
        {
            return NetResult.NoNodeInformation;
        }

        using (var terminal = await netTerminal.Connect(netNode))
        {
            if (terminal is null)
            {
                return NetResult.NoNetwork;
            }

            var remoteControl = terminal.GetService<IRemoteControl>();
            var response = await remoteControl.Authenticate(this.token).ResponseAsync;
            this.logger.TryGet()?.Log($"RequestAuthorization: {response.Result}");
            if (!response.IsSuccess)
            {
                return NetResult.NotAuthorized;
            }

            var result = await remoteControl.Restart();
            this.logger.TryGet()?.Log($"Restart: {result}");
            if (result == NetResult.Success)
            {
                var machine = this.bigMachine.RunnerMachine.GetOrCreate();
                if (machine != null)
                {
                    _ = machine.Command.Restart();
                }
            }

            return result;
        }*/
    }

    private readonly ILogger logger;
    private readonly BigMachine bigMachine;
    private readonly RemoteControlBase remoteControlBase;
}

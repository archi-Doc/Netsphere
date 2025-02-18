// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Packet;
using Netsphere.Stats;

namespace Netsphere.Machines;

/*/// <summary>
/// Check essential nodes and determine MyStatus.ConnectionType.<br/>
/// 1: Connect and get nodes.<br/>
/// 2: Determine MyStatus.ConnectionType.<br/>
/// 3: Check essential nodes.
/// </summary>
[MachineObject(UseServiceProvider = true)]
public partial class EssentialAddressMachine : Machine
{
    public EssentialAddressMachine(ILogger<EssentialAddressMachine> logger, NetBase netBase, NetControl netControl, NetStats netStats)
        : base()
    {
        this.logger = logger;
        this.netBase = netBase;
        this.netControl = netControl;
        this.netStats = netStats;
        this.DefaultTimeout = TimeSpan.FromSeconds(1);
    }

    private readonly ILogger logger;
    private readonly NetControl netControl;
    private readonly NetBase netBase;
    private readonly NetStats netStats;

    [StateMethod(0)]
    protected async Task<StateResult> Initial(StateParameter parameter)
    {//
        this.logger.TryGet(LogLevel.Information)?.Log($"Essential net machine");

        if (!this.netStats.EssentialAddress.GetUncheckedNode(out var netAddress))
        {
            return StateResult.Terminate;
        }

        // var node = await this.netControl.NetTerminal.UnsafeGetNetNode(netAddress);
        var r = await this.netControl.NetTerminal.PacketTerminal.SendAndReceive<PingPacket, PingPacketResponse>(netAddress, new());

        if (r.Result == NetResult.Success && r.Value is { } value)
        {// Success
            this.netStats.EssentialAddress.Report(netAddress, ConnectionResult.Success);
        }
        else
        {
            this.netStats.EssentialAddress.Report(netAddress, ConnectionResult.Failure);
        }

        return StateResult.Continue;
    }

    [StateMethod(1)]
    protected async Task<StateResult> First(StateParameter parameter)
    {
        return StateResult.Terminate;
    }
}*/

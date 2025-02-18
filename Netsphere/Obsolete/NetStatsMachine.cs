// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Stats;

namespace Netsphere.Machines;

/*[MachineObject(UseServiceProvider = true)]
public partial class NetStatsMachine : Machine
{
    private const int NodeThreshold = 4;

    public NetStatsMachine(ILogger<NetStatsMachine> logger, NetControl netControl, NetStats statsData, NodeControl nodeControl)
    {
        this.logger = logger;
        this.netControl = netControl;
        this.netStats = statsData;
        this.nodeControl = nodeControl;

        this.DefaultTimeout = TimeSpan.FromSeconds(5);
    }

    [StateMethod(0)]
    protected async Task<StateResult> Unknown(StateParameter parameter)
    {
        this.logger.TryGet()?.Log("Unknown");

        this.netStats.UpdateStats();

        if (this.netStats.PublicIpv4Address.AddressState != PublicAddress.State.Unknown &&
            this.netStats.PublicIpv6Address.AddressState != PublicAddress.State.Unknown)
        {// Address has been fixed.
            if (this.netStats.PublicIpv4Address.AddressState == PublicAddress.State.Unavailable && this.netStats.PublicIpv6Address.AddressState == PublicAddress.State.Unavailable)
            {
                this.netStats.Reset();
            }
            else
            {
                this.ChangeState(State.AddressFixed, true);
                return StateResult.Continue;
            }
        }

        var tasks = new List<Task<AddressQueryResult>>();
        if (this.netStats.PublicIpv4Address.AddressState == PublicAddress.State.Unknown)
        {
            // if (this.nodeControl.CountIpv4 < NodeThreshold)
            {
                tasks.Add(NetStatsHelper.GetIcanhazipIPv4(this.CancellationToken));
            }
        }

        if (this.netStats.PublicIpv6Address.AddressState == PublicAddress.State.Unknown)
        {
            // if (this.netStats.EssentialNode.CountIpv6 < NodeThreshold)
            {
                tasks.Add(NetStatsHelper.GetIcanhazipIPv6(this.CancellationToken));
            }
        }

        var results = await Task.WhenAll(tasks);
        foreach (var x in results)
        {
            this.netStats.ReportAddress(x);
        }

        // Address has been fixed.
        this.ChangeState(State.AddressFixed, true);
        return StateResult.Continue;
    }

    [StateMethod]
    protected async Task<StateResult> AddressFixed(StateParameter parameter)
    {
        this.logger.TryGet()?.Log(this.netStats.GetOwnNetNode().ToString());

        return StateResult.Terminate;
    }

    private readonly ILogger logger;
    private readonly NetControl netControl;
    private readonly NetStats netStats;
    private readonly NodeControl nodeControl;
}*/

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Machines;

/*[MachineObject(UseServiceProvider = true)]
public partial class PublicIPMachine : Machine
{
    private const string Filename = "PublicIP.tinyhand";
    private const string IcanhazipUriIPv4 = "http://ipv4.icanhazip.com"; // "http://icanhazip.com"
    private const string IcanhazipUriIPv6 = "http://ipv6.icanhazip.com";
    private const string DynDnsUri = "http://checkip.dyndns.org";

    [TinyhandObject(ImplicitKeyAsName = true)]
    public partial class Data
    {
        public long Mics { get; set; }

        public IPAddress? IPAddress { get; set; }
    }

    public PublicIPMachine(ILogger<PublicIPMachine> logger, LpBase lpBase, NetControl netControl, Crystalizer crystalizer)
    {
        this.logger = logger;
        this.lpBase = lpBase;
        this.netControl = netControl;

        var configuration = new CrystalConfiguration() with
        {
            SaveFormat = SaveFormat.Utf8,
            FileConfiguration = new GlobalFileConfiguration(Filename),
            NumberOfFileHistories = 0,
        };

        this.crystal = crystalizer.GetOrCreateCrystal<Data>(configuration);

        // this.DefaultTimeout = TimeSpan.FromSeconds(5);
    }

    [StateMethod(0)]
    protected async Task<StateResult> Initial(StateParameter parameter)
    {
        if (this.crystal.Data.IPAddress is not null &&
            Mics.IsInPeriodToUtcNow(this.crystal.Data.Mics, Mics.FromMinutes(5)))
        {
            var nodeAddress = new NodeAddress(this.crystal.Data.IPAddress, (ushort)this.netControl.NetBase.NetsphereOptions.Port);
            this.netControl.NetStatus.ReportMyNodeAddress(nodeAddress);
            this.logger?.TryGet()?.Log($"{nodeAddress.ToString()} from file");
            return StateResult.Terminate;
        }

        if (await this.GetIcanhazipIPv4().ConfigureAwait(false) == true)
        {
            return StateResult.Terminate;
        }
        else if (await this.GetDynDns().ConfigureAwait(false) == true)
        {
            return StateResult.Terminate;
        }

        return StateResult.Terminate;
    }

    private async Task ReportIpAddress(IPAddress ipAddress, string uri)
    {
        var nodeAddress = new NodeAddress(ipAddress, (ushort)this.netControl.NetBase.NetsphereOptions.Port);
        this.netControl.NetStatus.ReportMyNodeAddress(nodeAddress);
        this.logger?.TryGet()?.Log($"{nodeAddress.ToString()} from {uri}");

        this.crystal.Data.Mics = Mics.GetUtcNow();
        this.crystal.Data.IPAddress = ipAddress;
        await this.crystal.Save().ConfigureAwait(false);
    }

    private async Task<bool> GetIcanhazipIPv4()
    {
        try
        {
            using (var httpClient = new HttpClient())
            {
                var result = await httpClient.GetStringAsync(IcanhazipUriIPv4, this.CancellationToken).ConfigureAwait(false);
                var ipString = result.Replace("\\r\\n", string.Empty).Replace("\\n", string.Empty).Trim();
                if (!IPAddress.TryParse(ipString, out var ipAddress))
                {
                    return false;
                }

                await this.ReportIpAddress(ipAddress, IcanhazipUriIPv4).ConfigureAwait(false);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> GetIcanhazipIPv6()
    {
        try
        {
            using (var httpClient = new HttpClient())
            {
                var result = await httpClient.GetStringAsync(IcanhazipUriIPv6, this.CancellationToken).ConfigureAwait(false);
                var ipString = result.Replace("\\r\\n", string.Empty).Replace("\\n", string.Empty).Trim();
                if (!IPAddress.TryParse(ipString, out var ipAddress))
                {
                    return false;
                }

                await this.ReportIpAddress(ipAddress, IcanhazipUriIPv6).ConfigureAwait(false);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> GetDynDns()
    {
        try
        {
            using (var httpClient = new HttpClient())
            {
                var result = await httpClient.GetStringAsync(DynDnsUri, this.CancellationToken).ConfigureAwait(false);

                var start = result.IndexOf(':');
                if (start < 0)
                {
                    return false;
                }

                var end = result.IndexOf('<', start + 1);
                if (end < 0)
                {
                    return false;
                }

                var ipString = result.Substring(start + 1, end - start - 1).Trim();
                if (!IPAddress.TryParse(ipString, out var ipAddress))
                {
                    return false;
                }

                await this.ReportIpAddress(ipAddress, DynDnsUri).ConfigureAwait(false);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private ILogger? logger;
    private NetControl netControl;
    private LpBase lpBase;
    private ICrystal<Data> crystal;
}*/

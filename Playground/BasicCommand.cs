// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Unit;
using CrossChannel;
using Netsphere;
using Netsphere.Crypto;
using Netsphere.Packet;
using Netsphere.Relay;
using SimpleCommandLine;

namespace Playground;

[SimpleCommand("basic")]
public class BasicCommand : ISimpleCommand<BasicCommandOptions>, IClockHandTarget
{
    public BasicCommand(ILogger<BasicCommand> logger, NetUnit netUnit, IRelayControl relayControl, IChannel<IClockHandTarget> clockHandChannel, ClockHand clockHand)
    {
        this.logger = logger;
        this.netUnit = netUnit;
        this.relayControl = relayControl;
        // Radio.Open<IClockHandService>(default);
        clockHand.SendSignal(ExecutionSignal.Start);
        // clockHand.Pause();
        clockHandChannel.Open(this, true);
    }

    public async Task Execute(BasicCommandOptions options, string[] args, CancellationToken cancellationToken)
    {
        /*var r = await NetStatsHelper.GetIcanhazipIPv4();
        var netAddress = new NetAddress(r.Address!, (ushort)this.netUnit.NetBase.NetOptions.Port);
        var netNode = new NetNode(netAddress, this.netUnit.NetBase.NodePublicKey);
        var st = netNode.ToString();
        options.Node = st;
        this.netUnit.NetStats.SetOwnNetNodeForTest(netAddress, this.netUnit.NetBase.NodePublicKey);*/

        /*if (!NetAddress.TryParse(this.logger, options.Node, out var address))
        {
            return;
        }*/

        var netNode = Alternative.NetNode;
        var netAddress = netNode.Address;

        var sw = Stopwatch.StartNew();
        var netTerminal = this.netUnit.NetTerminal;
        netTerminal.Services.EnableNetService<ITestService>();
        var packetTerminal = netTerminal.PacketTerminal;

        using (var connection = (await netTerminal.Connect(netNode)))
        {
            if (connection is null)
            {
                return;
            }

            var agreement = new ConnectionAgreement();
            agreement.MinimumConnectionRetentionMics = Mics.FromMinutes(1);
            agreement.TransmissionTimeout = TimeSpan.FromMinutes(1);
            connection.Agreement.AcceptAll(agreement);

            var service = connection.GetService<ITestService>();
            var re = await service.MethodA(3, default);
            if (re.IsFailure)
            {
                return;
            }

            Console.WriteLine(re.Value);
            await Task.Delay(11000, cancellationToken);

            re = await service.MethodA(13, default);
            if (re.IsFailure)
            {
                return;
            }

            var channel = new ResponseChannel<int>(static (result, value) =>
            {
                Console.WriteLine($"ResponseChannel: {value}");
            });
            service.MethodB(2, ref channel);

            var y = 3;
            service.MethodC(2, ref y, ref channel);

            await connection.WaitForReceiveCompletion();
            // await Task.Delay(100);
        }

        var length = AuthenticationToken.MaxStringLength;
        var p = new PingPacket("test56789");
        var result = await packetTerminal.SendAndReceive<PingPacket, PingPacketResponse>(netAddress, p, 0, default, EndpointResolution.NetAddress);
        Console.WriteLine(result);

        Mics.UpdateFastCorrected();
        var micsId = Mics.GetMicsId();
        Console.WriteLine(micsId);
        micsId = Mics.GetMicsId();
        Console.WriteLine(micsId);


        Console.WriteLine("ClockHand ->");

        try
        {
            await Task.Delay(100_000, cancellationToken);
        }
        catch
        {
        }
    }

    void IClockHandTarget.OnEverySecond()
    {
        this.logger.GetWriter()?.Write(Mics.GetCorrected().MicsToDateTimeString("yyyy-MM-dd HH:mm:ss.ffffff K"));
    }

    void IClockHandTarget.OnEveryMinute()
    {
        this.logger.GetWriter()?.Write("Minute");
    }

    private readonly NetUnit netUnit;
    private readonly ILogger logger;
    private readonly IRelayControl relayControl;
}

public record BasicCommandOptions
{
    // [SimpleOption("Node", Description = "Node address", Required = true)]
    // public string Node { get; set; } = string.Empty;
}

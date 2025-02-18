// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Unit;
using SimpleCommandLine;

namespace NetsphereTest;

[SimpleCommand("basic")]
public class BasicTestSubcommand : ISimpleCommandAsync<BasicTestOptions>
{
    public BasicTestSubcommand(ILogger<BasicTestSubcommand> logger, NetControl netControl)
    {
        this.logger = logger;
        this.NetControl = netControl;
    }

    public async Task RunAsync(BasicTestOptions options, string[] args)
    {
        if (!NetAddress.TryParse(this.logger, options.NetNode, out var address))
        {
            return;
        }

        var node = await this.NetControl.NetTerminal.UnsafeGetNetNode(address);
        if (node is null)
        {
            return;
        }

        this.logger.TryGet()?.Log($"SendData: {address.ToString()}");
        this.logger.TryGet()?.Log($"{Stopwatch.Frequency}");

        // var nodeInformation = NodeInformation.Alternative;
        using (var terminal = await this.NetControl.NetTerminal.Connect(node))
        {
            if (terminal is null)
            {
                return;
            }

            // terminal.SetMaximumResponseTime(1_000_000);

            var sw = Stopwatch.StartNew();
            /*var t = terminal.SendAndReceiveAsync<PacketPunch, PacketPunchResponse>(p);
            Logger.Priority.Information($"t: {t.Result}");
            Logger.Priority.Information($"{sw.ElapsedMilliseconds} ms, Resend: {terminal.ResendCount}");*/

            /*sw.Restart();
            var t5 = terminal.SendPacketAndReceiveAsync<TestPacket, TestPacket>(TestPacket.Create(11));
            Logger.Priority.Information($"t5: {t5.Result}");
            Logger.Priority.Information($"{sw.ElapsedMilliseconds} ms, Resend: {terminal.ResendCount}");

            var p2 = TestBlock.Create(4_000_00);
            Logger.Priority.Information($"p2 send: {p2}");
            sw.Restart();
            var t2 = await terminal.SendAndReceiveAsync<TestBlock, TestBlock>(p2);

            p2 = TestBlock.Create(2000);
            Logger.Priority.Information($"p2b send: {p2}");
            var t3 = await terminal.SendAndReceiveAsync<TestBlock, TestBlock>(p2);
            Logger.Priority.Information($"t2 received: {t2.Value}");
            Logger.Priority.Information($"t3 received: {t3.Value}");
            Logger.Priority.Information($"{sw.ElapsedMilliseconds} ms, Resend: {terminal.ResendCount}");*/

            /*var p4 = TestBlock.Create(4_000_000);
            Logger.Priority.Information($"4MB send: {p4}");
            sw.Restart();
            var t4 = await terminal.SendAndReceiveAsync<TestBlock, TestBlock>(p4, int.MaxValue);
            Logger.Priority.Information($"4MB received: {t4.Value}");
            Logger.Priority.Information($"{sw.ElapsedMilliseconds} ms, Resend: {terminal.ResendCount}");*/

            /*var p4 = TestBlock.Create(4000_000);
            Logger.Priority.Information($"4MB send: {p4}");
            sw.Restart();
            // var t4 = await terminal.SendAndReceiveAsync<TestBlock, TestBlock>(p4);
            // Logger.Priority.Information($"4MB received: {t4.Value}");
            var result = await terminal.SendAsync<TestBlock>(p4);
            Logger.Priority.Information(result.ToString());
            Logger.Priority.Information($"{sw.ElapsedMilliseconds} ms, Resend: {terminal.ResendCount}");*/
        }
    }

    public NetControl NetControl { get; set; }

    private ILogger<BasicTestSubcommand> logger;
}

public record BasicTestOptions
{
    [SimpleOption("Node", Description = "Node address", Required = true)]
    public string NetNode { get; init; } = string.Empty;

    public override string ToString() => $"{this.NetNode}";
}

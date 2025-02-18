// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Arc.Unit;
using Lp.NetServices;
using SimpleCommandLine;

namespace NetsphereTest;

[SimpleCommand("stress")]
public class StressSubcommand : ISimpleCommandAsync<StressOptions>
{
    public StressSubcommand(ILogger<StressSubcommand> logger, NetControl netControl)
    {
        this.logger = logger;
        this.NetControl = netControl;
    }

    public async Task RunAsync(StressOptions options, string[] args)
    {
        NetNode? node = Alternative.NetNode;
        if (!string.IsNullOrEmpty(options.Node))
        {
            if (!NetNode.TryParseNetNode(this.logger, options.Node, out node))
            {
                return;
            }
        }

        this.logger.TryGet()?.Log($"Stress: {node.ToString()}, Total/Concurrent: {options.Total}/{options.Concurrent}");

        await this.Stress1(node, options);
    }

    public NetControl NetControl { get; set; }

    private async Task Stress1(NetNode node, StressOptions options)
    {
        var data = new byte[100];
        int successCount = 0;
        int failureCount = 0;
        long totalLatency = 0;

        ThreadPool.GetMinThreads(out var workMin, out var ioMin);
        ThreadPool.SetMinThreads(1000, ioMin);

        var sw = Stopwatch.StartNew();
        /*Parallel.For(0, options.Concurrent, i =>
        {
            for (var j = 0; j < (options.Total / options.Concurrent); j++)
            {
                var sw2 = new Stopwatch();
                using (var terminal = this.NetControl.Terminal.Create(node))
                {
                    var service = terminal.GetService<IBenchmarkService>();
                    sw2.Restart();
                    var response = service.Pingpong(data).ResponseAsync;

                    if (response.Result.IsSuccess)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref failureCount);
                    }

                    sw2.Stop();
                    Interlocked.Add(ref totalLatency, sw2.ElapsedMilliseconds);
                }
            }
        });*/

        var array = new Task[options.Concurrent];
        for (int i = 0; i < options.Concurrent; i++)
        {
            array[i] = Task.Run(async () =>
            {
                for (var j = 0; j < (options.Total / options.Concurrent); j++)
                {
                    var sw2 = new Stopwatch();
                    using (var connection = await this.NetControl.NetTerminal.Connect(node, Connection.ConnectMode.NoReuse)) // Do not reuse the connection as it quickly reaches the transmission limit.
                    {
                        if (connection is null)
                        {
                            return;

                        }

                        var service = connection.GetService<IRemoteBenchHost>();
                        sw2.Restart();

                        var response = await service.Pingpong(data).ResponseAsync; // response.Result.IsSuccess is EVIL
                        if (response.IsSuccess)
                        {
                            Interlocked.Increment(ref successCount);
                        }
                        else
                        {
                            Interlocked.Increment(ref failureCount);
                        }

                        sw2.Stop();
                        Interlocked.Add(ref totalLatency, sw2.ElapsedMilliseconds);
                    }
                }
            });
        }

        await Task.WhenAll(array);

        ThreadPool.SetMinThreads(workMin, ioMin);

        sw.Stop();

        var totalCount = successCount + failureCount;
        if (totalCount == 0)
        {
            totalCount = 1;
        }

        var record = new RemoteBenchRecord()
        {
            SuccessCount = successCount,
            FailureCount = failureCount,
            Concurrent = options.Concurrent,
            ElapsedMilliseconds = sw.ElapsedMilliseconds,
            CountPerSecond = (int)((successCount + failureCount) * 1000 / sw.ElapsedMilliseconds),
            AverageLatency = (int)(totalLatency / totalCount),
        };

        using (var terminal = await this.NetControl.NetTerminal.Connect(node))
        {
            if (terminal is null)
            {
                return;

            }
            var service = terminal.GetService<IRemoteBenchHost>();
            await service.Report(record);
        }

        await Console.Out.WriteLineAsync(record.ToString());
    }

    private ILogger logger;
}

public record StressOptions
{
    [SimpleOption("Node", Description = "Node address")]
    public string Node { get; init; } = string.Empty;

    [SimpleOption("Total", Description = "")]
    public int Total { get; init; } = 1_000; // 1_000;

    [SimpleOption("Concurrent", Description = "")]
    public int Concurrent { get; init; } = 100; // 25;

    public override string ToString() => $"{this.Node}";
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Unit;
using Netsphere.Misc;
using SimpleCommandLine;
using ValueLink;

namespace NetsphereTest;

public partial class TaskParent
{
    [ValueLinkObject]
    public partial class Item
    {
        public Item(TaskParent taskParent, int id)
        {
            this.taskParent = taskParent;
            this.id = id;

            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        var ct = ThreadCore.Root.CancellationToken;
                        // await Task.WhenAny(this.pulseEvent.WaitAsync(ct), Task.Delay(100)).ConfigureAwait(false);

                        // await this.pulseEvent.WaitAsync(TimeSpan.FromMilliseconds(1000), ct).ConfigureAwait(false);
                        await this.pulseEvent.WaitAsync(ct).ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    if (this.close)
                    {
                        this.taskParent.Delete(id);
                        return;
                    }
                }
            });
        }

        public void Close()
        {
            this.close = true;
            this.mics = Mics.GetSystem();
            this.pulseEvent.Pulse();
        }

        private TaskParent taskParent;

        [Link(Type = ChainType.Unordered)]
        private int id;

        private AsyncPulseEvent pulseEvent = new();
        private bool close = false;
        internal long mics;
    }

    public void Add(int id)
    {
        lock (this.syncObject)
        {
            if (!this.goshujin.IdChain.ContainsKey(id))
            {
                this.goshujin.Add(new(this, id));
            }
        }
    }

    public void Delete(int id)
    {
        lock (this.syncObject)
        {
            if (this.goshujin.IdChain.TryGetValue(id, out var item))
            {
                this.latency += (Mics.GetSystem() - item.mics);
                item.Goshujin = null;
            }
        }
    }

    public async Task Run()
    {
        // Array
        int[] array;
        lock (this.syncObject)
        {
            array = this.goshujin.IdChain.Keys.ToArray();
        }

        // Shuffle
        var r = new Random(12);
        var n = array.Length;
        while (n > 1)
        {
            n--;
            var m = r.Next(n + 1);
            (array[m], array[n]) = (array[n], array[m]);
        }

        // Close
        this.count = array.Length;
        foreach (var x in array)
        {
            lock (this.syncObject)
            {
                if (this.goshujin.IdChain.TryGetValue(x, out var item))
                {
                    item.Close();
                }
            }
        }

        // Wait
        while (true)
        {
            lock (this.syncObject)
            {
                if (this.goshujin.IdChain.Count == 0)
                {

                    return;
                }
            }

            await Task.Delay(10);
        }
    }

    public long AverageLatency => this.latency / this.count;

    private object syncObject = new();
    private SemaphoreLock semaphore = new();
    private Item.GoshujinClass goshujin = new();
    private long latency;
    private int count = 1;
}

/*public partial class TaskParent
{
    [ValueLinkObject]
    public partial class Item
    {
        public Item(TaskParent taskParent, int id)
        {
            this.taskParent = taskParent;
            this.id = id;

            Task.Run(async () =>
            {
                while (true)
                {
                    var ct = ThreadCore.Root.CancellationToken;
                    // await Task.WhenAny(this.pulseEvent.WaitAsync(ct), Task.Delay(100)).ConfigureAwait(false);
                    await this.pulseEvent.WaitAsync(ct).ConfigureAwait(false);

                    if (this.close)
                    {
                        this.taskParent.Delete(id);
                        return;
                    }
                }
            });
        }

        public void Close()
        {
            this.close = true;
            this.pulseEvent.Pulse();
        }

        private TaskParent taskParent;

        [Link(Type = ChainType.Unordered)]
        private int id;

        private AsyncPulseEvent pulseEvent = new();
        private bool close = false;
    }

    public void Add(int id)
    {
        lock (this.syncObject)
        {
            this.list.Add(new Item(this, id));
            this.count++;
        }
    }

    public void Delete(int id)
    {
        lock (this.syncObject)
        {
            this.count--;
        }
    }

    public async Task Run()
    {
        // Array
        Item[] array;
        lock (this.syncObject)
        {
            array = this.list.ToArray();
        }

        // Close
        foreach (var x in array)
        {
            x.Close();
        }

        // Wait
        while (true)
        {
            lock (this.syncObject)
            {
                if (this.count == 0)
                {
                    return;
                }
            }

            await Task.Delay(10);
        }
    }

    private object syncObject = new();
    private List<Item> list = new();
    private int count;
}*/

[SimpleCommand("task")]
public class TaskScalingSubcommand : ISimpleCommandAsync<TaskScalingOptions>
{
    public TaskScalingSubcommand(ILogger<TaskScalingSubcommand> logger, NetControl netControl)
    {
        this.logger = logger;
        this.NetControl = netControl;
    }

    public async Task RunAsync(TaskScalingOptions options, string[] args)
    {
        await Console.Out.WriteLineAsync("Task scaling test");
        await Console.Out.WriteLineAsync();

        // ThreadPool.GetMaxThreads(out var workerThreads, out var completionPortThreads);
        // ThreadPool.SetMaxThreads(100, completionPortThreads);

        await this.Test(100);
        await this.Test(1_000);
        await this.Test(10_000);

        await this.Test(100_000);
        await this.Test(100_000);
        await this.Test(100_000);

        //await this.Test(300_000);

        // ThreadPool.SetMaxThreads(workerThreads, completionPortThreads);
    }

    public async Task Test(int count)
    {
        var bt = new BenchTimer();

        bt.Restart();

        var tp = new TaskParent();
        for (var i = 0; i < count; i++)
        {
            tp.Add(i);
        }

        await Console.Out.WriteLineAsync(bt.StopAndGetText("Create"));

        await Task.Delay(100);

        bt.Restart();
        await tp.Run();
        await Console.Out.WriteLineAsync(bt.StopAndGetText("Run"));
        await Console.Out.WriteLineAsync($"Latency {tp.AverageLatency / 1000d}");

        await Console.Out.WriteLineAsync();
    }

    public NetControl NetControl { get; set; }

    private ILogger logger;
}

public record TaskScalingOptions
{
    // [SimpleOption("node", Description = "Node address", Required = true)]
    // public string Node { get; init; } = string.Empty;

    // public override string ToString() => $"{this.Node}";
}

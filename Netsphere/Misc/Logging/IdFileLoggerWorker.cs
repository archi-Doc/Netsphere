// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Concurrent;
using System.Text;

namespace Netsphere.Logging;

internal partial class IdFileLoggerWorker : TaskCore
{
    private const int MaxFlush = 10_000;

    [ValueLinkObject]
    private partial class Stream
    {
        [Link(Type = ChainType.QueueList, Name = "LimitQueue", Primary = true)]
        public Stream(IdFileLoggerWorker worker, long id)
        {
            this.worker = worker;
            this.Id = id;
            this.FilePath = this.worker.basePath + this.Id.ToString("X4") + this.worker.baseExtension;
        }

        public void Enqueue(IdFileLoggerWork work)
            => this.queue.Enqueue(work);

        public async Task Flush()
        {
            if (this.queue.Count == 0)
            {
                return;
            }

            var sb = new StringBuilder();
            while (this.queue.TryDequeue(out var work))
            {
                this.worker.formatter.Format(sb, work.LogEvent);
                sb.Append(Environment.NewLine);
            }

            try
            {
                if (Path.GetDirectoryName(this.FilePath) is { } directory)
                {
                    Directory.CreateDirectory(directory);
                }

                await File.AppendAllTextAsync(this.FilePath, sb.ToString()).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        public void DeleteFile()
        {
            try
            {
                File.Delete(this.FilePath);
            }
            catch
            {
            }
        }

        [Link(Type = ChainType.Ordered)]
        public long Id { get; private set; }

        public string FilePath { get; private set; }

        private IdFileLoggerWorker worker;
        private Queue<IdFileLoggerWork> queue = new();
    }

    public IdFileLoggerWorker(UnitCore core, UnitLogger unitLogger, IdFileLoggerOptions options)
        : base(core, Process, false)
    {
        this.logger = unitLogger.GetLogger<IdFileLoggerWorker>();
        this.options = options;
        this.formatter = new(options.Formatter);

        var fileName = Path.GetFileName(options.Path);
        var fullPath = options.Path;
        var idx = fileName.LastIndexOf('.');
        if (idx >= 0)
        {
            idx += fullPath.Length - fileName.Length;
            this.basePath = fullPath.Substring(0, idx);
            this.baseExtension = fullPath.Substring(idx);
        }
        else
        {
            this.basePath = fullPath;
            this.baseExtension = string.Empty;
        }

        this.baseFile = Path.GetFileName(this.basePath);
    }

    public static async Task Process(object? obj)
    {
        var worker = (IdFileLoggerWorker)obj!;

        await worker.Sync().ConfigureAwait(false);
        while (worker.Sleep(1000))
        {
            await worker.Flush(false).ConfigureAwait(false);
        }

        await worker.Flush(false).ConfigureAwait(false);
    }

    public void Add(IdFileLoggerWork work)
    {
        this.queue.Enqueue(work);
    }

    public async Task Sync()
    {
        var directory = Path.GetDirectoryName(this.basePath);
        if (directory == null)
        {
            return;
        }

        await this.semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var x in Directory.EnumerateFiles(directory, this.baseFile + "*" + this.baseExtension, SearchOption.TopDirectoryOnly))
            {
                var idLength = x.Length - (directory.Length + 1 + this.baseExtension.Length);
                if (idLength < 0)
                {
                    continue;
                }

                var idString = x.Substring(directory.Length + 1, idLength);
                if (long.TryParse(idString, System.Globalization.NumberStyles.HexNumber, null, out var id))
                {
                    var stream = this.goshujin.IdChain.FindFirst(id);
                    if (stream == null)
                    {// New
                        stream = new Stream(this, id);
                        this.goshujin.Add(stream);
                    }
                }
            }

            var capacity = this.options.ClearLogsAtStartup ? 0 : this.options.MaxStreamCapacity;
            this.LimitStream(capacity);
        }
        catch
        {
        }
        finally
        {
            this.semaphore.Release();
        }
    }

    public async Task<int> Flush(bool terminate)
    {
        await this.semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            var count = 0;
            while (count < MaxFlush && this.queue.TryDequeue(out var work))
            {
                count++;

                var id = work.LogEvent.EventId;
                var stream = this.goshujin.IdChain.FindFirst(id);
                if (stream == null)
                {// New
                    stream = new Stream(this, id);
                    this.goshujin.Add(stream);
                }

                stream.Enqueue(work);
            }

            // Limit stream
            this.LimitStream(this.options.MaxStreamCapacity);

            // Flush
            foreach (var x in this.goshujin)
            {
                await x.Flush().ConfigureAwait(false);
            }

            if (terminate)
            {
                this.Terminate();
            }
            else
            {// Limit log capacity
                /*this.limitLogCount += count;
                var now = DateTime.UtcNow;
                if (now - this.limitLogTime > TimeSpan.FromMinutes(10) ||
                    this.limitLogCount >= LimitLogThreshold)
                {
                    this.limitLogTime = now;
                    this.limitLogCount = 0;

                    this.LimitLogs();
                }*/
            }

            return count;
        }
        finally
        {
            this.semaphore.Release();
        }
    }

    private void LimitStream(int capacity)
    {
        while (this.goshujin.LimitQueueChain.Count > capacity)
        {
            var stream = this.goshujin.LimitQueueChain.Peek();
            stream.DeleteFile();
            stream.Goshujin = null;
        }
    }

    /*private string GetCurrentPath()
        => this.basePath + DateTime.Now.ToString("yyyyMMdd") + this.baseExtension;

    private void LimitLogs()
    {
        var currentPath = this.GetCurrentPath();
        var directory = Path.GetDirectoryName(currentPath);
        var file = Path.GetFileName(currentPath);
        if (directory == null || file == null)
        {
            return;
        }

        long capacity = 0;
        SortedDictionary<string, long> pathToSize = new();
        try
        {
            foreach (var x in Directory.EnumerateFiles(directory, this.baseFile + "*" + this.baseExtension, SearchOption.TopDirectoryOnly))
            {
                if (x.Length == currentPath.Length)
                {
                    try
                    {
                        var size = new FileInfo(x).Length;
                        pathToSize.Add(x, size);
                        capacity += size;
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
            return;
        }

        // this.logger?.TryGet()?.Log($"Limit logs {capacity}/{this.maxCapacity} {directory}");
        foreach (var x in pathToSize)
        {
            if (capacity < this.maxCapacity)
            {
                break;
            }

            try
            {
                File.Delete(x.Key);
                this.logger?.TryGet()?.Log($"Deleted: {x.Key}");
            }
            catch
            {
            }

            capacity -= x.Value;
        }
    }*/

    public int Count => this.queue.Count;

    private ILogger<IdFileLoggerWorker>? logger;
    private IdFileLoggerOptions options;
    private string basePath;
    private string baseFile;
    private string baseExtension;
    private SimpleLogFormatter formatter;

    private SemaphoreSlim semaphore = new(1, 1);
    private ConcurrentQueue<IdFileLoggerWork> queue = new();
    private Stream.GoshujinClass goshujin = new();
    // private DateTime limitLogTime;
    // private int limitLogCount = 0;
}

internal class IdFileLoggerWork
{
    public IdFileLoggerWork(LogEvent logEvent)
    {
        this.LogEvent = logEvent;
    }

    public LogEvent LogEvent { get; }
}

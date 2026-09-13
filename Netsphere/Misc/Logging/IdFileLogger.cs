// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Logging;

/// <summary>
/// Buffers log events and writes them to files grouped by identifier.
/// </summary>
/// <typeparam name="TOption">The logger options type.</typeparam>
public class IdFileLogger<TOption> : BufferedLogOutput
    where TOption : IdFileLoggerOptions
{
    public IdFileLogger(ExecutionGroup parent, LogUnit logUnit, ILogService logService, TOption options)
        : base(logUnit)
    {
        if (string.IsNullOrEmpty(Path.GetDirectoryName(options.FilePath)))
        {
            options = options with
            {
                FilePath = Path.Combine(Directory.GetCurrentDirectory(), options.FilePath),
            };
        }

        this.worker = new(parent, logService, options);
        this.options = options;
        this.worker.SendSignal(ExecutionSignal.Start);
    }

    public override void Output(LogEvent logEvent)
    {
        if (this.options.MaxQueueLength <= 0 || this.worker.Count < this.options.MaxQueueLength)
        {
            this.worker.Add(new(logEvent));
        }
    }

    public override Task<int> FlushAsync(bool terminate) => this.worker.Flush(terminate);

    private IdFileLoggerWorker worker;
    private TOption options;
}

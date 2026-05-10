// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Logging;

public class IdFileLogger<TOption> : BufferedLogOutput
    where TOption : IdFileLoggerOptions
{
    public IdFileLogger(ExecutionGroup parent, LogUnit logUnit, ILogService logService, TOption options)
        : base(logUnit)
    {
        if (string.IsNullOrEmpty(Path.GetDirectoryName(options.Path)))
        {
            options = options with
            {
                Path = Path.Combine(Directory.GetCurrentDirectory(), options.Path),
            };
        }

        this.worker = new(parent, logService, options);
        this.options = options;
        this.worker.SendSignal(ExecutionSignal.Start);
    }

    public override void Output(LogEvent logEvent)
    {
        if (this.options.MaxQueue <= 0 || this.worker.Count < this.options.MaxQueue)
        {
            this.worker.Add(new(logEvent));
        }
    }

    public override Task<int> Flush(bool terminate) => this.worker.Flush(terminate);

    private IdFileLoggerWorker worker;
    private TOption options;
}

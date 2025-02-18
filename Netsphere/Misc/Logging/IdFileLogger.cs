// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Logging;

public class IdFileLogger<TOption> : BufferedLogOutput
    where TOption : IdFileLoggerOptions
{
    public IdFileLogger(UnitCore core, UnitLogger unitLogger, TOption options)
        : base(unitLogger)
    {
        if (string.IsNullOrEmpty(Path.GetDirectoryName(options.Path)))
        {
            options.Path = Path.Combine(Directory.GetCurrentDirectory(), options.Path);
        }

        this.worker = new(core, unitLogger, options);
        this.options = options;
        this.worker.Start();
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

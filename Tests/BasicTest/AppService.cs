// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.IO;
using System.Threading;
using Serilog;

namespace BasicTest
{
    public interface IAppService
    {
        void EnterCommand(string directory);

        void ExitCommand();

        void Terminate();

        CancellationToken CancellationToken { get; }

        ManualResetEvent TerminatedEvent { get; }

        bool SafeKeyAvailable { get; }
    }

    public class AppService : IAppService
    {
        public void EnterCommand(string directory)
        {
            // Logger: Debug, Information, Warning, Error, Fatal
            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(directory, "logs", "log.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                buffered: true,
                flushToDiskInterval: TimeSpan.FromMilliseconds(1000))
            .CreateLogger();

            Log.Information(string.Empty);
        }

        public void ExitCommand()
        {
            Log.Information("terminated");
            Log.CloseAndFlush();
        }

        public void Terminate()
        {
            this.cancellationTokenSource.Cancel();
        }

        public bool SafeKeyAvailable
        {
            get
            {
                try
                {
                    return Console.KeyAvailable;
                }
                catch
                {
                    return false;
                }
            }
        }

        public CancellationToken CancellationToken => this.cancellationTokenSource.Token;

        public ManualResetEvent TerminatedEvent { get; } = new(false);

        private CancellationTokenSource cancellationTokenSource = new();
    }
}

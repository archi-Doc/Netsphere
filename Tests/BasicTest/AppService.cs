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

        // void Terminate();

        bool SafeKeyAvailable { get; }
    }

    public class AppService : IAppService
    {
        public void EnterCommand(string directory)
        {
            // Logger: Verbose, Debug, Information, Warning, Error, Fatal
            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information)
            .WriteTo.File(
                Path.Combine(directory, "logs", "log.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                fileSizeLimitBytes: 1024 * 1024,
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
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.IO;
using System.Threading.Tasks;
using ConsoleAppFramework;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BasicTest
{
    internal class Program : ConsoleAppBase
    {
        public static async Task Main(string[] args)
        {
            await Host.CreateDefaultBuilder().RunConsoleAppFrameworkAsync<Program>(args);
        }

        public async Task Run(
            [Option("local port number to transfer packets")] int port,
            [Option("true if the node is receiver")] bool receiver = false,
            [Option("mode(transfer)")] string mode = "transfer",
            [Option("base directory for storing application data")] string dir = "")
        {
            this.InitializeLogger(dir);

            Log.Information("start");

            Console.WriteLine($"port: {port}");

            await Task.Delay(2000, this.Context.CancellationToken);

            Log.Information("fin");
        }

        private void InitializeLogger(string dir)
        {
            // Logger: Debug, Information, Warning, Error, Fatal
            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(dir, "logs", "log.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                buffered: true,
                flushToDiskInterval: TimeSpan.FromMilliseconds(1000))
            .CreateLogger();
        }
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ConsoleAppFramework;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BasicTest
{
    internal class Options
    {
        public int Port { get; set; }

        public bool Receiver { get; set; }
    }

    internal class Program : ConsoleAppBase
    {
        private Dictionary<string, Func<Options, Task>> modeToFunc = new()
        {
            { "transfer", Transfer },
            { "timer", Timer },
        };

        public static async Task Main(string[] args)
        {
            await Host.CreateDefaultBuilder().RunConsoleAppFrameworkAsync<Program>(args);
        }

        public async Task Run(
            [Option("local port number to transfer packets")] int port,
            [Option("true if the node is receiver")] bool receiver = false,
            [Option("mode(transfer)")] string mode = "timer",
            [Option("base directory for storing application data")] string dir = "")
        {
            var options = new Options()
            {
                Port = port,
                Receiver = receiver,
            };

            this.InitializeLogger(dir);

            if (this.modeToFunc.TryGetValue(mode, out var action))
            {
                Log.Information($"mode: {mode}");
                await action(options);
            }
            else
            {
                Log.Error($"mode: {mode} not found.");
            }

            Log.Information("fin");
            Log.CloseAndFlush();
        }

        private static async Task Timer(Options option)
        {
            var sw = new Stopwatch();
            sw.Start();

            Log.Information($"ticks: {sw.ElapsedTicks:#,0}");
            Log.Information($"high resolution: {Stopwatch.IsHighResolution}");
            Log.Information($"frequency: {Stopwatch.Frequency:#,0}");

            var et = sw.ElapsedTicks;
            var et2 = sw.ElapsedTicks;
            Log.Information($"ticks: {et:#,0}");
            Log.Information($"ticks: {et2:#,0}");

            Log.Information($"ticks: {sw.ElapsedTicks:#,0}");
            Log.Information($"ticks: {sw.ElapsedTicks:#,0}");
            Log.Information($"ticks: {sw.ElapsedTicks:#,0}");

            Log.Information($"delay: 0");
            Log.Information($"ticks: {sw.ElapsedTicks:#,0}");

            await Task.Delay(1);
            Log.Information($"delay: 1");
            Log.Information($"ticks: {sw.ElapsedTicks:#,0}");

            await Task.Delay(10);
            Log.Information($"delay: 10");
            Log.Information($"ticks: {sw.ElapsedTicks:#,0}");

            // await Task.Delay(2000, this.Context.CancellationToken);
        }

        private static async Task Transfer(Options option)
        {
            Log.Information($"port: {option.Port}");

            // await Task.Delay(2000, this.Context.CancellationToken);
        }

        private void InitializeLogger(string dir)
        {
            // Logger: Debug, Information, Warning, Error, Fatal
            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
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

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
            [Option("p", "The local port number to receive packets.")] int port)
        {
            // Logger: Debug, Information, Warning, Error, Fatal
            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
               /* Path.Combine("logs", "log.txt")*/"/logs/log.txt",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                buffered: true,
                flushToDiskInterval: TimeSpan.FromMilliseconds(1000))
            .CreateLogger();

            Log.Information("test");

            Console.WriteLine($"port: {port}");

            await Task.Delay(3000, this.Context.CancellationToken);

            Console.WriteLine("fin.");
        }
    }
}

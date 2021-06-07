// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Threading.Tasks;
using ConsoleAppFramework;
using Microsoft.Extensions.Hosting;

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
            Console.WriteLine($"port: {port}");

            await Task.Delay(10000, this.Context.CancellationToken);

            Console.WriteLine("fin.");
        }
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ConsoleAppFramework;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BasicTest
{
    internal static class App
    {
        public static bool Run { get; set; } = true;

        public static bool SafeKeyAvailable
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

    internal class Options
    {
        public int Port { get; set; }

        public bool Receiver { get; set; }

        public string Target { get; set; } = string.Empty;

        public int TargetPort { get; set; }

        public int TestN { get; set; }
    }

    internal class Program : ConsoleAppBase
    {
        private Dictionary<string, Func<Options, Task>> modeToFunc;
        private UdpClient udpPort = default!;

        public Program()
        {
            this.modeToFunc = new Dictionary<string, Func<Options, Task>>()
            {
                { "transfer", this.Transfer },
                { "receive", this.Receive },
                { "udpmss", this.UdpMSS },
            };
        }

        public static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                Log.Information("exit (ProcessExit)");
                App.Run = false;
            };

            Console.CancelKeyPress += (s, e) =>
            {
                Log.Information("exit (Ctrl+C)");
                App.Run = false;
            };

            await Host.CreateDefaultBuilder().RunConsoleAppFrameworkAsync<Program>(args);
        }

        public async Task Run(
            [Option("p", "local port number to transfer packets")] int port,
            [Option("r", "true if the node is receiver")] bool receiver = false,
            [Option("target")] string target = "",
            [Option("targetport")] int targetport = 1000,
            [Option("m", "mode(transfer)")] string mode = "receive",
            [Option("d", "base directory for storing application data")] string dir = "",
            [Option("n", "test N")] int n = 0)
        {
            var options = new Options()
            {
                Port = port,
                Receiver = receiver,
                Target = target,
                TargetPort = targetport,
                TestN = n,
            };

            this.StartCommand(dir);

            this.udpPort = new UdpClient(options.Port);

            try
            {
                const int SIO_UDP_CONNRESET = -1744830452;
                this.udpPort.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch
            {
            }

            if (this.modeToFunc.TryGetValue(mode, out var action))
            {
                Log.Information($"mode: {mode}");
                await action(options);
            }
            else
            {
                Log.Error($"mode: {mode} not found.");
            }

            this.EndCommand();
        }

        [Command("timer")]
        public async Task Timer([Option("d", "base directory for storing application data")] string dir = "")
        {
            this.StartCommand(dir);

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

            await Task.Delay(20000, this.Context.CancellationToken);

            this.EndCommand();
        }

        private async Task Receive(Options option)
        {
            Log.Information($"receive port: {option.Port}");
            Log.Warning("any key to exit");

            // var t = Task.Run(this.ReceiveAction);
            var t = new Thread(this.ReceiveAction);
            t.Priority = ThreadPriority.Highest;
            t.Start();
            t.Join();

            return;
        }

        private void ReceiveAction()
        {
            while (true)
            {
                if (!App.Run)
                {
                    break;
                }
                else if (App.SafeKeyAvailable)
                {
                    break;
                }
                else if (this.udpPort.Available == 0)
                {
                    Thread.Sleep(10);
                    continue;
                }

                IPEndPoint remoteEP = default!;
                var bytes = this.udpPort.Receive(ref remoteEP);
                var text = $"Received: {bytes.Length}";
                if (bytes.Length >= sizeof(int))
                {
                    text += $", First data: {BitConverter.ToInt32(bytes)}";
                }

                Console.WriteLine(text);
                Console.WriteLine($"Sender address: {remoteEP.Address}, port: {remoteEP.Port}");

                // var rcvMsg = System.Text.Encoding.UTF8.GetString(bytes);
                // Console.WriteLine("受信したデータ:{0}", rcvMsg);
            }
        }

        /*private void ReceiveAction()
        {
            var t = this.udpPort.ReceiveAsync();
            while (true)
            {
                // var remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                t.Wait(10);

                if (this.Context.CancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (t.IsCompleted)
                {
                    var text = $"Received: {t.Result.Buffer.Length}";
                    if (t.Result.Buffer.Length >= sizeof(int))
                    {
                        var b = t.Result.Buffer;
                        text += $", First data: {BitConverter.ToInt32(b)}";
                    }

                    Console.WriteLine(text);
                    Console.WriteLine($"Sender address: {t.Result.RemoteEndPoint.Address}, port: {t.Result.RemoteEndPoint.Port}");

                    t = this.udpPort.ReceiveAsync();
                }

                // var rcvMsg = System.Text.Encoding.UTF8.GetString(bytes);
                // Console.WriteLine("受信したデータ:{0}", rcvMsg);
            }
        }*/

        private async Task Transfer(Options option)
        {
            Log.Information($"port: {option.Port}");

            // await Task.Delay(2000, this.Context.CancellationToken);
        }

        private async Task UdpMSS(Options option)
        {
            var length = option.TestN;
            var bytes = new byte[length];

            for (var n = 0; n < 4; n++)
            {
                var message = $"Send {n}";
                Console.WriteLine(message);

                bytes[0] = (byte)n;

                this.udpPort.Send(bytes, bytes.Length, option.Target, option.TargetPort);

                await Task.Delay(1000);
            }
        }

        private void StartCommand(string dir)
        {
            this.InitializeLogger(dir);
        }

        private void EndCommand()
        {
            Log.Information("terminated");
            Log.CloseAndFlush();
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

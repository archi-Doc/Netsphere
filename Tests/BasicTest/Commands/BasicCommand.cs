// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SimpleCommandLine;

namespace BasicTest
{
    public class BasicOptions
    {
        [SimpleOption("directory", null, "base directory for storing application data")]
        public string Directory { get; } = string.Empty;

        [SimpleOption("mode", null, "mode(receive, transfer)")]
        public string Mode { get; } = "receive";

        [SimpleOption("port", null, "local port number to transfer packets")]
        public int Port { get; } = 2000;

        [SimpleOption("targetip", null, "target ip address", Required = true)]
        public string TargetIp { get; } = string.Empty;

        [SimpleOption("targetport", null, "target port number")]
        public int TargetPort { get; } = 1000;

        [SimpleOption("receiver", null, "true if the node is receiver")]
        public bool Receiveer { get; } = true;

        [SimpleOption("n", null, "test N")]
        public int N { get; } = 4;
    }

    [SimpleCommand("basic")]
    public class BasicCommand : ISimpleCommandAsync<BasicOptions>
    {
        public BasicCommand(IAppService appService)
        {
            this.AppService = appService;

            this.modeToFunc = new Dictionary<string, Func<BasicOptions, Task>>()
            {
                { "transfer", this.Transfer },
                { "receive", this.Receive },
                { "udpmss", this.UdpMSS },
            };
        }

        public async Task Run(BasicOptions option, string[] args)
        {
            this.AppService.EnterCommand(option.Directory);

            this.udpPort = new UdpClient(option.Port);

            try
            {
                const int SIO_UDP_CONNRESET = -1744830452;
                this.udpPort.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch
            {
            }

            if (this.modeToFunc.TryGetValue(option.Mode, out var action))
            {
                Log.Information($"mode: {option.Mode}");
                await action(option);
            }
            else
            {
                Log.Error($"mode: {option.Mode} not found.");
            }

            this.AppService.ExitCommand();
        }

        private async Task Receive(BasicOptions option)
        {
            Log.Information($"receive port: {option.Port}");
            Log.Warning("any key to exit");

            var c = new ThreadCore(ThreadCore.Root, this.ReceiveAction);
            c.Thread.Priority = ThreadPriority.AboveNormal;
            await c.WaitForTermination(false, -1);
            // c.Thread.Join();

            return;
        }

        private void ReceiveAction(object? param)
        {
            var core = (ThreadCore)param!;
            while (true)
            {
                if (core.IsTerminated)
                {
                    break;
                }
                else if (this.AppService.SafeKeyAvailable)
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

        private async Task Transfer(BasicOptions option)
        {
            Log.Information($"port: {option.Port}");

            // await Task.Delay(2000, this.Context.CancellationToken);
        }

        private async Task UdpMSS(BasicOptions option)
        {
            var length = option.N;
            var bytes = new byte[length];

            for (var n = 0; n < 4; n++)
            {
                var message = $"Send {n}";
                Console.WriteLine(message);

                bytes[0] = (byte)n;

                this.udpPort.Send(bytes, bytes.Length, option.TargetIp, option.TargetPort);

                await Task.Delay(1000);
            }
        }

        public IAppService AppService { get; }

        private Dictionary<string, Func<BasicOptions, Task>> modeToFunc;
        private UdpClient udpPort = default!;
    }
}

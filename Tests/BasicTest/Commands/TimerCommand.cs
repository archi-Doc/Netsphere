// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using SimpleCommandLine;

namespace BasicTest
{
    public class TimerOptions : BaseOptions
    {
        /*[SimpleOption("directory", null, "base directory for storing application data")]
        public string Directory { get; set; } = string.Empty;*/
    }

    [SimpleCommand("timer")]
    public class TimerCommand : ISimpleCommandAsync<TimerOptions>
    {
        public TimerCommand(IAppService appService)
        {
            this.AppService = appService;
        }

        public async Task Run(TimerOptions option, string[] args)
        {
            this.AppService.EnterCommand(option.Directory);

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

            await Task.Delay(2000);

            this.AppService.ExitCommand();
        }

        public IAppService AppService { get; }
    }
}

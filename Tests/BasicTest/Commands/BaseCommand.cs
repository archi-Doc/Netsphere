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
    public class BaseOptions
    {
        [SimpleOption("directory", null, "base directory for storing application data")]
        public string Directory { get; set; } = string.Empty;
    }

    [SimpleCommand("base")]
    public class BaseCommand : ISimpleCommandAsync<TimerOptions>
    {
        public BaseCommand(IAppService appService)
        {
            this.AppService = appService;
        }

        public async Task Run(TimerOptions option, string[] args)
        {
            this.AppService.EnterCommand(option.Directory);

            Log.Information("template command");

            this.AppService.ExitCommand();
        }

        public IAppService AppService { get; }
    }
}

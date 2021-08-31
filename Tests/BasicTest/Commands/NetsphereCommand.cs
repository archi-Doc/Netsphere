// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LP.Netsphere;
using Serilog;
using SimpleCommandLine;

namespace BasicTest
{
    public class NetsphereCommandOptions : BaseOptions
    {
        [SimpleOption("netsphere", "ns")]
        public NetsphereOptions NetsphereOptions { get; set; } = new();
    }

    [SimpleCommand("ns", "Netsphere test")]
    public class NetsphereCommand : ISimpleCommandAsync<NetsphereCommandOptions>
    {
        public NetsphereCommand(IAppService appService)
        {
            this.AppService = appService;
        }

        public async Task Run(NetsphereCommandOptions option, string[] args)
        {
            this.AppService.EnterCommand(option.Directory);

            Log.Information("Netsphere test");

            this.AppService.ExitCommand();
        }

        public IAppService AppService { get; }
    }
}

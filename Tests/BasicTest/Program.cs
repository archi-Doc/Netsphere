// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using Serilog;
using SimpleCommandLine;

namespace BasicTest
{
    internal class Program
    {
        public static Container Container { get; } = new();

        public static async Task Main(string[] args)
        {
            // Simple Commands
            var commandTypes = new Type[]
            {
                typeof(TemplateCommand),
                typeof(TimerCommand),
                typeof(BasicCommand),
            };

            // DI Container
            Container.Register<IAppService, AppService>(Reuse.Singleton);
            foreach (var x in commandTypes)
            {
                Container.Register(x, Reuse.Singleton);
            }

            Container.ValidateAndThrow();

            AppDomain.CurrentDomain.ProcessExit += async (s, e) =>
            {// Console window closing or process terminated.
                // Log.Information("exit (ProcessExit)");
                ThreadCore.Root.Terminate();
                ThreadCore.Root.TerminationEvent.WaitOne(2000);
            };

            Console.CancelKeyPress += (s, e) =>
            {// Ctrl+C pressed
                // Log.Information("exit (Ctrl+C)");
                e.Cancel = true;
                ThreadCore.Root.Terminate();
            };

            var parserOptions = SimpleParserOptions.Standard with
            {
                ServiceProvider = Container,
                RequireStrictCommandName = true,
                RequireStrictOptionName = false
            };

            await SimpleParser.ParseAndRunAsync(commandTypes, args, parserOptions);
            await ThreadCore.Root.WaitForTermination(true, 2000);
            ThreadCore.Root.TerminationEvent.Set(); // App terminated.
        }
    }
}

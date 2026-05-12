// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

global using Arc.Threading;
global using Tinyhand;
using Arc;
using Arc.Unit;
using CrystalData;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;
using Netsphere.Crypto;
using Netsphere.Misc;
using Netsphere.Relay;
using SimpleCommandLine;

namespace Playground;

[NetService]
public interface ITestService : INetService
{
    Task<NetResultAndValue<int>> MethodA(int x, CancellationToken cancellationToken);

    void MethodB(int x, ref ResponseChannel<int> channel);

    void MethodC(in int x, ref int y, ref ResponseChannel<int> channel);

    Task MethodD(CancellationToken cancellationToken);

    public int X { get; set; }
}

[NetObject]
public class TestServiceImpl : ITestService
{
    public int X { get; set; }

    Task<NetResultAndValue<int>> ITestService.MethodA(int x, CancellationToken cancellationToken)
    {
        var agreement = new ConnectionAgreement();
        agreement.MinimumConnectionRetentionMics = Mics.FromMinutes(1);
        agreement.TransmissionTimeout = TimeSpan.FromMinutes(1);
        TransmissionContext.Current.ServerConnection.Agreement.AcceptAll(agreement);

        return Task.FromResult(new NetResultAndValue<int>(x + 1));
    }

    void ITestService.MethodB(int x, ref ResponseChannel<int> channel)
    {
        Thread.Sleep(1);
        channel.SetResponse(x + 111);
    }

    void ITestService.MethodC(in int x, ref int y, ref ResponseChannel<int> channel)
    {
        Thread.Sleep(1);
        channel.SetResponse(x + y);
    }

    Task ITestService.MethodD(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public class Program
{
    private static ExecutionRoot? root;

    public static async Task Main()
    {
        AppCloseHandler.Set(() =>
        {// Closing the console window or terminating the process.
            root?.RequestTermination(); // Send a termination signal to the root.
            root?.WaitForTermination(TimeSpan.FromSeconds(2)).Wait();
        });

        Console.CancelKeyPress += (s, e) =>
        {// Ctrl+C pressed.
            e.Cancel = true;
            root?.RequestTermination(); // Send a termination signal to the root.
        };

        var builder = new NetUnit.Builder()
            .Configure(context =>
            {
                context.AddSingleton<IRelayControl, CertificateRelayControl>();
                context.AddSingleton<BigMachine>();

                // Command
                context.AddCommand(typeof(BasicCommand));
                context.AddCommand(typeof(NtpCommand));

                context.AddLoggerResolver(context =>
                {// Logger
                    if (context.LogLevel == LogLevel.Debug)
                    {
                        context.SetOutput<FileLogger<FileLoggerOptions>>();
                        return;
                    }

                    context.SetOutput<ConsoleAndFileLogger>();
                });
            })
             .ConfigureNetsphere(context =>
             {// Register the services provided by the server.
                 context.AddNetService<ITestService, TestServiceImpl>();
             })
             .PostConfigure(context =>
             {
                 // FileLoggerOptions
                 var logfile = "Logs/Debug.txt";
                 var fileLoggerOptions = context.GetOptions<FileLoggerOptions>();
                 context.SetOptions(fileLoggerOptions with
                 {
                     Path = Path.Combine(context.DataDirectory, logfile),
                     MaxLogCapacity = 1,
                     Formatter = fileLoggerOptions.Formatter with { TimestampFormat = "yyyy-MM-dd HH:mm:ss.ffffff K", },
                     ClearLogsAtStartup = true,
                     MaxQueue = 100_000,
                 });

                 // NetsphereOptions
                 context.SetOptions(context.GetOptions<NetOptions>() with
                 {
                     // NodeName = "test",
                     // EnablePing = true,
                     EnableServer = true,
                     EnableAlternative = true,
                 });
             });

        var crystalBuilder = new CrystalUnit.Builder();
        crystalBuilder.ConfigureCrystal(context =>
        {
            context.AddCrystal<Netsphere.Misc.NtpCorrection>(new CrystalConfiguration() with
            {
                SaveFormat = SaveFormat.Utf8,
                NumberOfFileHistories = 0,
                FileConfiguration = new GlobalFileConfiguration(Netsphere.Misc.NtpCorrection.Filename),
            });
        });

        builder.AddBuilder(crystalBuilder);

        // Netsphere
        var unit = builder.Build();
        root = unit.Context.Root;
        var options = unit.Context.ServiceProvider.GetRequiredService<NetOptions>();
        await Console.Out.WriteLineAsync($"Port: {options.Port.ToString()}");

        var netBase = unit.Context.ServiceProvider.GetRequiredService<NetBase>();
        if (BaseHelper.TryParseFromEnvironmentVariable<SeedKey>("nodesecretkey", out var seedKey))
        {
            netBase.SetNodeSeedKey(seedKey);
        }

        await unit.Run(options, true);

        TinyhandSerializer.ServiceProvider = unit.Context.ServiceProvider;
        var crystalControl = unit.Context.ServiceProvider.GetRequiredService<CrystalControl>();
        await crystalControl.PrepareAndLoad(false);

        var parserOptions = SimpleParserOptions.Standard with
        {
            ServiceProvider = unit.Context.ServiceProvider,
            RequireStrictCommandName = false,
            RequireStrictOptionName = false,
        };

        await SimpleParser.ParseAndExecute(unit.Context.Commands, SimpleParserHelper.GetCommandLineArguments(), parserOptions, root.CancellationToken); // Main process

        await crystalControl.StoreAndRip();
        await unit.Terminate();

        root.RequestTermination();
        if (unit.Context.ServiceProvider.GetService<LogUnit>() is { } unitLogger)
        {
            await unitLogger.FlushAndTerminate();
        }

        await root.WaitForTermination(); // Wait for the termination infinitely.
    }
}

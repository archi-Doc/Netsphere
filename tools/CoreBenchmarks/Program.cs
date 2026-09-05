// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Arc.Collections;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;
using Netsphere.Core;
using Netsphere.Packet;

var outputPath = Path.GetFullPath(args.Length == 0 ? "core-benchmark.json" : args[0]);
var console = Console.Out;
Console.SetOut(TextWriter.Null);
var results = new List<object>();
var address = new NetAddress(IPAddress.Parse("192.168.123.234"), 54321);
var characters = new char[NetAddress.MaxStringLength];
Measure("IPv4 TryFormat", 500_000, () => address.TryFormat(characters, out _));

var unit = new NetUnit.Builder().Build();
await unit.Run(new NetOptions { EnableAlternative = true, EnableServer = true }, true);
try
{
    var netUnit = unit.Context.ServiceProvider.GetRequiredService<NetUnit>();
    using var connection = await netUnit.NetTerminal.Connect(Alternative.NetNode);
    if (connection is null)
    {
        throw new InvalidOperationException("Could not create benchmark connection.");
    }

    var packet = new PingPacket("benchmark");
    Measure("Create ping packet", 30_000, () =>
    {
        PacketTerminal.CreatePacket(1, packet, out var memory);
        memory.Return();
    });

    var input = new byte[1024];
    var encrypted = new byte[1040];
    Measure("Encrypt 1024 bytes", 100_000, () => connection.Encrypt(1, 1, input, encrypted, out _));

    using var transmission = new SendTransmission(connection, uint.MaxValue);
    transmission.Dispose();
    Measure("Process duplicate burst ACK", 30_000, () => transmission.ProcessReceive_AckBlock(0, 0, Span<byte>.Empty, 0));
}
finally
{
    await unit.Terminate();
    Console.SetOut(console);
}

var json = JsonSerializer.Serialize(new { Runtime = Environment.Version.ToString(), OS = Environment.OSVersion.ToString(), Samples = 7, Results = results }, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(outputPath, json);
Console.WriteLine(json);

void Measure(string name, int iterations, Action action)
{
    for (var i = 0; i < 10_000; i++)
    {
        action();
    }

    var times = new double[7];
    var allocations = new double[7];
    for (var sample = 0; sample < times.Length; sample++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            action();
        }

        times[sample] = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
        allocations[sample] = (GC.GetAllocatedBytesForCurrentThread() - allocated) / (double)iterations;
    }

    Array.Sort(times);
    Array.Sort(allocations);
    results.Add(new { Name = name, Iterations = iterations, NanosecondsPerOperation = Math.Round(times[3], 2), BytesPerOperation = allocations[3] });
}

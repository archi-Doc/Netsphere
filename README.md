# Netsphere

![NuGet](https://img.shields.io/nuget/v/Netsphere)
![Build and Test](https://github.com/archi-Doc/Netsphere/workflows/Build%20and%20Test/badge.svg)

Netsphere is a UDP-based network library for C# with generated RPC, serialized blocks, and streams. It integrates [Tinyhand](https://github.com/archi-Doc/Tinyhand) serialization, encrypted connections, acknowledgments, retransmission, congestion control, and optional relay circuits.

## Contents

- [Requirements and installation](#requirements-and-installation)
- [Quick start](#quick-start)
- [Service contracts and results](#service-contracts-and-results)
- [Service lifetime and filters](#service-lifetime-and-filters)
- [Options and connection limits](#options-and-connection-limits)
- [Blocks and streams](#blocks-and-streams)
- [Memory ownership and concurrency](#memory-ownership-and-concurrency)
- [ResponseChannel](#responsechannel)
- [Identity, authentication, and relays](#identity-authentication-and-relays)
- [Supporting utilities](#supporting-utilities)
- [Troubleshooting](#troubleshooting)
- [Building and testing](#building-and-testing)
- [Performance measurements](#performance-measurements)

## Requirements and installation

Use the .NET 10 SDK and an editor with C# 14 support. The library targets `net10.0`; its project enables preview language features. Visual Studio is optional when using the command line.

Add Netsphere to the shared contract, client, and server projects:

```shell
dotnet add package Netsphere
```

The package includes the Netsphere source generator. When referencing this repository's projects directly, also reference `NetsphereGenerator` as an analyzer, as shown in [QuickStartServer.csproj](QuickStartServer/QuickStartServer.csproj).

## Quick start

Create a shared class library and two console applications targeting .NET 10. Reference the shared library from both applications. Start the server first, then pass its printed node address to the client.

### Shared contract

```csharp
using Netsphere;

namespace Example;

[NetService]
public interface ITestService : INetService
{
    Task<string?> DoubleString(string input);
    Task<int> Sum(int x, int y);
}
```

### Server

```csharp
using System.Net;
using Example;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;

var unit = new NetUnit.Builder()
    .ConfigureNetsphere(context =>
        context.AddNetService<ITestService, TestServiceAgent>())
    .Build();

await unit.Run(new NetOptions
{
    NodeName = "Example server",
    Port = 1981,
    EnableServer = true,
}, allowUnsafeConnection: false);

try
{
    var terminal = unit.Context.ServiceProvider.GetRequiredService<NetTerminal>();
    terminal.Services.EnableNetService<ITestService>();
    var netBase = unit.Context.ServiceProvider.GetRequiredService<NetBase>();
    var node = new NetNode(new NetAddress(IPAddress.Loopback, 1981), netBase.NodePublicKey);
    Console.WriteLine(node);
    Console.WriteLine("Press Enter to stop.");
    await Console.In.ReadLineAsync();
}
finally
{
    await unit.Terminate();
}

[NetObject]
public sealed class TestServiceAgent : ITestService
{
    public Task<string?> DoubleString(string input)
        => Task.FromResult<string?>(input + input);

    public Task<int> Sum(int x, int y)
        => Task.FromResult(x + y);
}
```

### Client

```csharp
using Example;
using Microsoft.Extensions.DependencyInjection;
using Netsphere;

if (args.Length != 1 || !NetNode.TryParse(args[0], out var node, out _))
{
    Console.WriteLine("Pass the node address printed by the server.");
    return;
}

var unit = new NetUnit.Builder().Build();
await unit.Run(new NetOptions(), allowUnsafeConnection: false);
try
{
    var terminal = unit.Context.ServiceProvider.GetRequiredService<NetTerminal>();
    using var connection = await terminal.Connect(node);
    if (connection is null)
    {
        Console.WriteLine("Could not connect.");
        return;
    }

    var service = connection.GetService<ITestService>();
    Console.WriteLine(await service.DoubleString("Nupo")); // NupoNupo
    Console.WriteLine(await service.Sum(1, 2)); // 3
}
finally
{
    await unit.Terminate();
}
```

Quote the printed node string when passing it on the command line:

```shell
dotnet run --project Client -- "<node address printed by the server>"
```

This example uses loopback and a newly generated key on each server run. For remote access, publish a reachable address with the server's public key. Configure `NodeSecretKey` from persistent private configuration when a stable identity is required.

## Service contracts and results

- Mark the shared interface with `[NetService]` and inherit `INetService`.
- Mark the implementation with `[NetObject]`, register it, and enable it through `NetTerminal.Services`.
- Use Tinyhand-serializable arguments and response values. Keep contract definitions consistent between client and server.
- Rebuild both endpoints when updating the generator. Generated wire formats, including specialized byte-memory handling, must match; compatibility with older generated proxies is not guaranteed.
- Methods return `Task`, `Task<T>`, or use the [ResponseChannel](#responsechannel) form. A cancellation token on a Task-based method must be the final parameter.
- Prefer `Task<NetResult>` or `Task<NetResultAndValue<T>>` when callers need an explicit status. A plain `Task<T>` does not expose transport status separately from its value.

Check `NetResult` before consuming a returned value or stream. Outcomes include `Success`, `Completed`, `Timeout`, `Closed`, `NoNetService`, and size-limit errors.

## Service lifetime and filters

Implementations are resolved lazily and cached for each server connection. Their actual lifetime also depends on the DI registration or factory; singleton registrations can share an instance across connections. Calls can overlap.

`TransmissionContext.Current` exposes the active request and its server connection while a handler runs. Enable or disable services for that connection through `TransmissionContext.Current.ServerConnection.GetContext()`. `NetTerminal.Services` sets the defaults for new connections.

Apply `[NetServiceFilter<TFilter>]` to an implementation class or method. Filters implement `IServiceFilter` and receive the request context and continuation. Class and method filters are combined in ascending `Order`; `Arguments` are passed to `SetArguments`. A filter with a parameterless constructor is created for each invocation. Other filters are resolved from DI and follow the registered lifetime. Shared DI instances must handle concurrent calls and argument initialization. Filters also run for `ResponseChannel` methods. See the [filter examples](xUnitTest/Services/IFilterTestService.cs).

## Options and connection limits

`NetOptions` configures the node. `Port = 0` lets the OS select a port. `EnableServer` defaults to `false`, `EnablePing` to `true`, and `EnableAlternative` to `false`. The alternative terminal is intended for diagnostics.

The initial `ConnectionAgreement` defaults are:

| Setting | Default | Meaning |
| --- | ---: | --- |
| `MaxTransmissions` | 4 | Concurrent transmissions per connection |
| `MaxBlockSize` | 4 MiB | Maximum serialized block size, including serialization headers |
| `MaxStreamLength` | 0 | Only empty streams allowed; `-1` removes the declared-length limit |
| `StreamBufferSize` | 8 MiB | Stream window size, rounded to packet capacity |
| `EnableBidirectionalConnection` | `false` | Whether the server may initiate calls back to the client |
| `TransmissionTimeout` | 4 seconds | Default transmission timeout |

Set server defaults before calling `unit.Run`, for example:

```csharp
var limits = unit.Context.ServiceProvider.GetRequiredService<NetBase>().DefaultAgreement;
limits.MaxStreamLength = 64L * 1024 * 1024;
```

Individual stream requests must still declare a nonnegative maximum length. `INetServiceWithUpdateAgreement` provides a signed update operation for applications that authorize agreement changes after connecting.

## Blocks and streams

For typed blocks outside RPC, register an `INetResponder` and use `ClientConnection.SendAndReceive<TSend, TReceive>`. `SyncResponder<TSend, TReceive>` runs inline; `AsyncResponder<TSend, TReceive>` runs its handler on the thread pool. Responder instances can serve concurrent connections.

| Stream direction | Service return type | Server operation |
| --- | --- | --- |
| Server to client | `Task<ReceiveStream?>` | `GetSendStream(...)`, then `Send` and `Complete` |
| Client to server | `Task<SendStream?>` | `GetReceiveStream()`, then `Receive` |
| Client to server with a response | `Task<SendStreamAndReceive<T>?>` | `GetReceiveStream<T>()`, then `Receive` and `SendAndDispose` |

Server operations use `TransmissionContext.Current`. For client-to-server streaming methods, the final request parameter is a `long` maximum stream length. The client sends chunks and calls `Complete` or `CompleteSendAndReceive`, according to the stream type. When receiving, consume the reported `Written` bytes, including those returned with `NetResult.Completed`. Stop using a stream after an error and pass cancellation tokens where appropriate.

See the complete [stream service](xUnitTest/Services/IStreamService.cs) and [client tests](xUnitTest/Tests/StreamTest.cs) for both directions and data verification.

## Memory ownership and concurrency

| Value | Lifetime and responsibility |
| --- | --- |
| RPC `byte[]`, `Memory<byte>`, or `ReadOnlyMemory<byte>` response | The client receives an independent copy that remains valid after the call. |
| RPC `Memory<byte>` or `ReadOnlyMemory<byte>` argument inside a handler | Borrowed request storage; do not retain it after the handler returns. Returning it or a slice as the response is supported. |
| RPC `BytePool.RentMemory` or `RentReadOnlyMemory` response | Ownership transfers to the client, which must return the lease after use. |
| Low-level `NetResponse` | Call `Return()` exactly once after consuming its buffer. |
| Stream send or receive buffer | Keep it available and do not modify send data until the operation finishes. |

Raw byte-array responses use no null marker: an empty payload is returned as `null`. Use a serialized wrapper when callers must distinguish an empty array from `null`.

Copying a pooled-memory struct does not acquire another lease. Use `IncrementAndShare()` when another owner needs its own reference, and return every acquired lease exactly once. Generated byte-memory responses reuse an exclusively owned request buffer when it fits; otherwise they copy before releasing the request.

Calls on a connection may overlap. Protect mutable service state, shared DI filters, and responder state. Stream operations serialize concurrent sends or reads, but their arrival order is not guaranteed; await each operation when ordering matters. A `TransmissionContext` belongs to one request and must not be mutated concurrently.

A generated method's trailing `CancellationToken` controls the client's local operation. It is not serialized or propagated to the server handler, which currently receives `default`. Cancellation or timeout does not prove that remote work stopped. Use an application-level cancellation protocol if the server must stop that work.

## ResponseChannel

Use `ResponseChannel<T>` for synchronous server handlers with callback delivery on the client. The method returns `void` and takes `ref ResponseChannel<T>` as its final parameter. The server calls `SetResponse`; use Task-based methods for asynchronous handlers.

```csharp
[NetService]
public interface ICallbackService : INetService
{
    void Sum(int x, int y, ref ResponseChannel<int> channel);
}

[NetObject]
public sealed class CallbackService : ICallbackService
{
    public void Sum(int x, int y, ref ResponseChannel<int> channel)
        => channel.SetResponse(x + y);
}
```

Register and enable this service as in the quick start. To await the callback itself:

```csharp
var received = new TaskCompletionSource<NetResultAndValue<int>>(
    TaskCreationOptions.RunContinuationsAsynchronously);
var channel = new ResponseChannel<int>((result, value) =>
    received.TrySetResult(new NetResultAndValue<int>(result, value)));
connection.GetService<ICallbackService>().Sum(1, 2, ref channel);
var response = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
Console.WriteLine($"{response.Result}: {response.Value}");
```

`WaitForReceiveCompletion` waits for the receive count to reach zero; it does not guarantee that a callback has finished. Callbacks may run on the receive path, on the thread pool during transmission closure, or during a local send failure. Keep them short and await a callback-owned signal when completion matters.

## Identity, authentication, and relays

`NetNode` combines an address with an encryption public key. Use a node whose public key you obtained through a trusted channel. `allowUnsafeConnection: false` disables address-only key discovery. RPC, block, and stream traffic uses encrypted connection packets; discovery and other control datagrams are separate packet types, so not every datagram is encrypted.

`AuthenticationToken`, `CertificateToken<T>`, `INetServiceWithAuthenticate`, and `INetServiceWithConnectBidirectionally` support application-defined authentication and bidirectional access. Encryption alone does not authorize a service call; implement the relevant server policy.

`SetAuthenticationToken` retains the first verified public-key identity on a connection. A different identity is rejected, including concurrent requests; use a new connection to change identities. Failed authentication is not cached as success.

Relay circuits are available through `NetTerminal.IncomingCircuit` and `OutgoingCircuit`. The default `NoRelayControl` disables relay allocation. Applications can supply an `IRelayControl`, such as `CertificateRelayControl`, and configure relay limits. See the [relay test](xUnitTest/Tests/RelayTest.cs).

Circuit additions are published after the preceding relay accepts setup. Failed setup leaves the existing circuit intact. Cleanup removes a closed hop and all outer hops that depend on it. Rejected source endpoints and failed authentication do not consume relay points.

This commented example is retained from the earlier README as a historical snippet:

```csharp
// using (var connection = await netUnit.NetTerminal.UnsafeConnect(new(IPAddress.Loopback, 1981)))
```

## Supporting utilities

- `Mics`, `MicsRange`, and `Time` provide microsecond clocks, timestamp ranges, and DateTime conversions. `Fast*` properties return cached timestamps.
- `NtpCorrection` and `NtpMachine` maintain network clock correction.
- `NetStats` and `NodeControl` track observed addresses and known nodes.
- `IdFileLogger<TOption>` writes buffered logs to files grouped by identifier.

## Troubleshooting

- **Connection is null:** check the address, public key, `EnableServer`, UDP reachability, and relay requirements.
- **Proxy creation fails:** ensure the source generator ran for the contract project and inspect build diagnostics.
- **`NoNetService`:** register and enable the interface before connecting. Existing connections retain their own service settings.
- **Size-limit errors:** check the agreed limits, serialization overhead, and declared stream length.
- **Unexpected shared state:** inspect service and filter lifetimes; handlers can run concurrently.

Dispose client connections and call `unit.Terminate()` during shutdown.

## Building and testing

From the repository root:

```shell
dotnet build Netsphere.slnx -c Release
dotnet test --project xUnitTest/xUnitTest.csproj -c Release
```

To collect coverage:

```shell
dotnet tool restore
dotnet build xUnitTest/xUnitTest.csproj -c Debug -t:Rebuild -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=obj/CoverageGenerated
dotnet tool run dotnet-coverage collect -f cobertura -o artifacts/coverage.xml dotnet xUnitTest/bin/Debug/net10.0/xUnitTest.dll
```

In PowerShell, summarize handwritten runtime code and generated RPC code separately:

```powershell
./tools/Summarize-Coverage.ps1 -Path artifacts/coverage.xml
./tools/Summarize-Coverage.ps1 -Path artifacts/coverage.xml -Module xUnitTest -SourceKind Generated -FilePattern '*gen.Netsphere.*.cs'
```

The script merges duplicate source lines from async state machines. These are line coverage measurements, not proof that every branch, race, or network-failure sequence was exercised. Generated code is covered in the consuming assembly; the generator itself executes during compilation.

## Performance measurements

Run the microbenchmarks in Release mode, separately from tests because both use the diagnostic terminal:

```shell
dotnet run --project tools/CoreBenchmarks/CoreBenchmarks.csproj -c Release -- artifacts/core-benchmark.json
```

The harness warms each case and reports the median of seven samples, with elapsed nanoseconds and allocated bytes per operation on the measuring thread. It covers formatting, queues, packet creation, encryption, transmission cleanup, relay cleanup, and response-buffer reuse. It does not measure allocations on other threads or end-to-end network throughput. Pool misses, payload sizes, filters, and asynchronous work can still allocate. See [PERFORMANCE_REVIEW.md](PERFORMANCE_REVIEW.md) for the measured changes and coverage gaps.

The repository also contains [QuickStartServer](QuickStartServer), [QuickStartClient](QuickStartClient), and [NetsphereTest](NetsphereTest) examples. Netsphere is licensed under the [MIT license](LICENSE).

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace xUnitTest.NetsphereTest;

[NetService]
public interface ITransportReviewService : INetService
{
    Task<int> Echo(int value);

    void Channel(ref ResponseChannel<int> channel);

    Task<string?> OptionalText();

#pragma warning disable NSG010 // Exercise mixed nullable and non-nullable return signatures.
    Task<string> RequiredText();
#pragma warning restore NSG010
}

[NetObject]
[NetServiceFilter<InjectedTransportFilter>]
public class TransportReviewService : ITransportReviewService
{
    public Task<int> Echo(int value) => Task.FromResult(value);

    public void Channel(ref ResponseChannel<int> channel) => channel.SetResponse(42);

    public Task<string?> OptionalText() => Task.FromResult<string?>(null);

    public Task<string> RequiredText() => Task.FromResult("required");
}

public class TransportFilterDependency
{
    public int Value => 42;
}

public class InjectedTransportFilter : IServiceFilter
{
    private readonly TransportFilterDependency dependency;

    public InjectedTransportFilter(TransportFilterDependency dependency) => this.dependency = dependency;

    public async Task Invoke(TransmissionContext context, Func<TransmissionContext, Task> invoker)
    {
        if (this.dependency.Value != 42)
        {
            throw new InvalidOperationException();
        }

        await Task.Yield();
        await invoker(context).ConfigureAwait(false);
    }
}

[NetService]
public interface IClassOrderedReviewService : INetService
{
    Task<int> Echo(int value);

    Task<int> WithReplacement(int value);
}

[NetObject]
[NetServiceFilter<IncrementIntFilter>(Order = 1)]
[NetServiceFilter<MultiplyIntFilter>(Order = 0)]
public class ClassOrderedReviewService : IClassOrderedReviewService
{
    public Task<int> Echo(int value) => Task.FromResult(value);

    [NetServiceFilter<ReplacementContextFilter>(Order = -1)]
    public Task<int> WithReplacement(int value) => Task.FromResult(value);
}

public class ReplacementContextFilter : IServiceFilter
{
    public async Task Invoke(TransmissionContext context, Func<TransmissionContext, Task> invoker)
    {
        if (!NetHelper.TrySerialize(41, out var memory))
        {
            throw new InvalidOperationException();
        }

        var replacement = new TransmissionContext(context.ServerConnection, context.TransmissionId, context.DataKind, context.DataId, memory);
        try
        {
            await invoker(replacement).ConfigureAwait(false);
            context.Return();
            context.RentMemory = replacement.RentMemory;
            replacement.RentMemory = default;
        }
        finally
        {
            replacement.Return();
        }
    }
}

[NetService]
public interface IFactoryFailureReviewService : INetService
{
    Task<int> Echo(int value);
}

[NetObject]
public class FactoryFailureReviewService : IFactoryFailureReviewService
{
    public FactoryFailureReviewService() => throw new InvalidOperationException("Expected service factory failure.");

    public Task<int> Echo(int value) => Task.FromResult(value);
}

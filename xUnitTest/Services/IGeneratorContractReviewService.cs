// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace xUnitTest.NetsphereTest;

public interface ILeftGeneratorContract : INetService
{
    Task<int> Echo(int value);

    int Status { get; }
}

public interface IRightGeneratorContract : INetService
{
    Task<int> Echo(int value);

    string? Status { get; }
}

[NetService]
public interface IGeneratorContractReviewService : ILeftGeneratorContract, IRightGeneratorContract
{
}

public class GeneratorContractReviewBase : ILeftGeneratorContract, IRightGeneratorContract
{
    int ILeftGeneratorContract.Status => 1;

    string? IRightGeneratorContract.Status => "right";

    [NetServiceFilter<IncrementIntFilter>]
    Task<int> ILeftGeneratorContract.Echo(int value) => Task.FromResult(value + 10);

    [NetServiceFilter<MultiplyIntFilter>]
    Task<int> IRightGeneratorContract.Echo(int value) => Task.FromResult(value + 20);
}

[NetObject]
public class GeneratorContractReviewService : GeneratorContractReviewBase, IGeneratorContractReviewService
{
}

[NetService]
public interface IInheritedFilterReviewService : INetService
{
    Task<int> Inherited(int value);

    Task<int> Overridden(int value);

    Task<int> ExplicitFilter(int value);
}

[NetServiceFilter<IncrementIntFilter>(Order = 0)]
public class InheritedFilterReviewBase : IInheritedFilterReviewService
{
    [NetServiceFilter<MultiplyIntFilter>(Order = 1)]
    public Task<int> Inherited(int value) => Task.FromResult(value);

    [NetServiceFilter<MultiplyIntFilter>(Order = 1)]
    public virtual Task<int> Overridden(int value) => Task.FromResult(value);

    [NetServiceFilter<ExplicitGeneratorReviewFilter>(Order = 1)]
    public Task<int> ExplicitFilter(int value) => Task.FromResult(value);
}

[NetObject]
public class InheritedFilterReviewService : InheritedFilterReviewBase
{
    [NetServiceFilter<ExplicitGeneratorReviewFilter>(Order = 2)]
    public override Task<int> Overridden(int value) => Task.FromResult(value + 10);
}

public class ExplicitGeneratorReviewFilter : IServiceFilter
{
    Task IServiceFilter.Invoke(TransmissionContext context, Func<TransmissionContext, Task> invoker)
        => new MultiplyIntFilter().Invoke(context, invoker);
}

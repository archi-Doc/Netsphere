// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace xUnitTest.NetsphereTest;

[NetService]
public interface IFilterTestService : INetService
{
    public Task<int> NoFilter(int x);

    public Task<int> Increment(int x);

    public Task<int> Multiply2(int x);

    public Task<int> Multiply3(int x);

    public Task<int> IncrementAndMultiply2(int x);

    public Task<int> Multiply2AndIncrement(int x);
}

[NetObject]
public class FilterTestServiceImpl : IFilterTestService
{
    public async Task<int> NoFilter(int x) => x;

    [NetServiceFilter<IncrementIntFilter>]
    public async Task<int> Increment(int x) => x;

    [NetServiceFilter<MultiplyIntFilter>]
    public async Task<int> Multiply2(int x) => x;

    [NetServiceFilter<MultiplyIntFilter>(Arguments = new object[] { 3, })]
    public async Task<int> Multiply3(int x) => x;

    [NetServiceFilter<IncrementIntFilter>]
    [NetServiceFilter<MultiplyIntFilter>]
    public async Task<int> IncrementAndMultiply2(int x) => x;

    [NetServiceFilter<IncrementIntFilter>(Order = 1)]
    [NetServiceFilter<MultiplyIntFilter>(Order = 0)]
    public async Task<int> Multiply2AndIncrement(int x) => x;
}

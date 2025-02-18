// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace xUnitTest.NetsphereTest;

[NetServiceInterface]
public interface IFilterTestService : INetService
{
    public NetTask<int> NoFilter(int x);

    public NetTask<int> Increment(int x);

    public NetTask<int> Multiply2(int x);

    public NetTask<int> Multiply3(int x);

    public NetTask<int> IncrementAndMultiply2(int x);

    public NetTask<int> Multiply2AndIncrement(int x);
}

[NetServiceObject]
public class FilterTestServiceImpl : IFilterTestService
{
    public async NetTask<int> NoFilter(int x) => x;

    [NetServiceFilter<IncrementIntFilter>]
    public async NetTask<int> Increment(int x) => x;

    [NetServiceFilter<MultiplyIntFilter>]
    public async NetTask<int> Multiply2(int x) => x;

    [NetServiceFilter<MultiplyIntFilter>(Arguments = new object[] { 3, })]
    public async NetTask<int> Multiply3(int x) => x;

    [NetServiceFilter<IncrementIntFilter>]
    [NetServiceFilter<MultiplyIntFilter>]
    public async NetTask<int> IncrementAndMultiply2(int x) => x;

    [NetServiceFilter<IncrementIntFilter>(Order = 1)]
    [NetServiceFilter<MultiplyIntFilter>(Order = 0)]
    public async NetTask<int> Multiply2AndIncrement(int x) => x;
}

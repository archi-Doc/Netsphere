// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace xUnitTest.NetsphereTest;

[NetServiceInterface]
public interface IBasicTaskService : INetService
{
    public Task SendInt(int x);

    public Task<int> IncrementInt(int x);

    public Task<int> SumInt(int x, int y);

    public Task TestResult();

    public Task<NetResult> TestResult2();
}

[NetServiceObject]
public class BasicTaskServiceImpl : IBasicTaskService
{
    public BasicTaskServiceImpl(NullFilter nullFilter)
    {
    }

    public async Task SendInt(int x)
    {
    }

    public async Task<int> IncrementInt(int x) => x + 1;

    public async Task<int> SumInt(int x, int y) => x + y;

    async Task IBasicTaskService.TestResult()
    {
        TransmissionContext.Current.Result = NetResult.InvalidOperation;
    }

    async Task<NetResult> IBasicTaskService.TestResult2()
    {
        TransmissionContext.Current.Result = NetResult.BlockSizeLimit;
        return NetResult.StreamLengthLimit;
    }
}

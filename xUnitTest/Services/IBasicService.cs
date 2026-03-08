// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace xUnitTest.NetsphereTest;

[NetService]
public interface IBasicService : INetService
{
    public Task<NetResult> SendInt(int x);

    public Task<int> IncrementInt(int x);

    public Task<int> SumInt(int x, int y);

    public Task<NetResult> TestResult();

    public Task<NetResult> TestResult2();

    public Task<NetResultAndValue<int>> TestResult3(int x);
}

[NetObject]
public class BasicServiceImpl : IBasicService
{
    public async Task<NetResult> SendInt(int x)
    {
        return NetResult.Success;
    }

    public async Task<int> IncrementInt(int x) => x + 1;

    public async Task<int> SumInt(int x, int y) => x + y;

    async Task<NetResult> IBasicService.TestResult()
    {
        var result = NetResult.InvalidOperation;
        TransmissionContext.Current.Result = result;
        return result;
    }

    async Task<NetResult> IBasicService.TestResult2()
    {
        TransmissionContext.Current.Result = NetResult.BlockSizeLimit;
        return NetResult.StreamLengthLimit;
    }

    async Task<NetResultAndValue<int>> IBasicService.TestResult3(int x)
    {
        return new(NetResult.Completed, x);
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace xUnitTest.NetsphereTest;

[NetServiceInterface]
public interface IBasicService : INetService
{
    public NetTask SendInt(int x);

    public NetTask<int> IncrementInt(int x);

    public NetTask<int> SumInt(int x, int y);

    public NetTask TestResult();

    public NetTask<NetResult> TestResult2();
}

[NetServiceObject]
public class BasicServiceImpl : IBasicService
{
    public async NetTask SendInt(int x)
    {
    }

    public async NetTask<int> IncrementInt(int x) => x + 1;

    public async NetTask<int> SumInt(int x, int y) => x + y;

    async NetTask IBasicService.TestResult()
    {
        TransmissionContext.Current.Result = NetResult.InvalidOperation;
    }

    async NetTask<NetResult> IBasicService.TestResult2()
    {
        TransmissionContext.Current.Result = NetResult.BlockSizeLimit;
        return NetResult.StreamLengthLimit;
    }
}

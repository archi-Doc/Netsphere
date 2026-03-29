// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace xUnitTest.NetsphereTest;

#pragma warning disable SA1502 // Element should not be on a single line

[NetService]
public interface IBasicService : INetService
{
    public Task<NetResult> SendInt(int x);

    public Task<int> IncrementInt(int x);

    public Task<int> SumInt(int x, int y);

    public Task<NetResult> TestResult();

    public Task<NetResult> TestResult2();

    public Task<NetResultAndValue<int>> TestResult3(int x);

    void SendInt2(Netsphere.NetUnion<int, int> union);

    public int TestProperty { get; protected set; }

    public string TestProperty2 { get; init; }

    protected int TestProperty3 { get; }
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

    public void SendInt2(NetUnion<int, int> netUnion)
    {
        netUnion.SetResponse(netUnion.SendValue + 4);
    }

    public int TestProperty { get => default!; set { } }

    public string TestProperty2 { get => default!; init { } }

    public int TestProperty3 { get; }
}

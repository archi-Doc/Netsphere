// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace xUnitTest.NetsphereTest;

[NetService]
public interface IBidirectionalService : INetService
{
    Task<NetResult> Connect();

    Task<NetResult> Set(int x);
}

[NetObject]
public class IBidirectionalServiceImpl : IBidirectionalService
{
    public static TaskCompletionSource<int> Result { get; } = new();

    async Task<NetResult> IBidirectionalService.Connect()
    {
        var clientConnection = TransmissionContext.Current.ServerConnection.PrepareBidirectionalConnection();
        _ = Task.Run(async () =>
        {
            var service = clientConnection.GetService<IBidirectionalService>();
            await service.Set(12);
        });

        return NetResult.Success;
    }

    async Task<NetResult> IBidirectionalService.Set(int x)
    {
        Result.SetResult(x);
        return NetResult.Success;
    }
}

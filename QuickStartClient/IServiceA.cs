// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

#pragma warning disable SA1204 // Static elements should appear before instance elements

namespace QuickStart;

[NetService]
public interface IServiceA : INetService
{
    Task<int> MethodA(int x, int y); // A standard method that uses Task.

    void MethodB(int x, int y, ref ResponseChannel<int> channel); // A method that uses ResponseChannel.
}

[NetObject]
public class ServiceAImpl : IServiceA
{
    public async Task<int> MethodA(int x, int y)
        => x + y;

    public void MethodB(int x, int y, ref ResponseChannel<int> channel)
        => channel.SetResponse(x + y); // Set the return value via the channel.
}

public static class ExampleA
{
    public static async Task ServiceA(ClientConnection clientConnection)
    {
        var service = clientConnection.GetService<IServiceA>();

        var result = await service.MethodA(1, 2); // 1 + 2 = 3

        // First, create a ResponseChannel(delegate) to handle the response from the server.
        var channel = new ResponseChannel<int>((result, value) => { Console.WriteLine(value); });

        // Call the method that takes a ResponseChannel as its last argument.
        service.MethodB(1, 2, ref channel);

        // Wait until receiving is complete. The delegate described above will be invoked while this method is running.
        await clientConnection.WaitForReceiveCompletion();
    }
}

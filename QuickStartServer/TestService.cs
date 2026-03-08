// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Crypto;
using Netsphere;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace QuickStart;

// Define an interface shared between the client and server.
[NetService] // Annotate NetService attribute.
public interface ITestService : INetService // An interface for NetService must inherit from INetService.
{
    Task<string?> DoubleString(string input); // Declare the service method.
    // Ensure that both arguments and return values are serializable by Tinyhand serializer, and the return type must be Task or Task<T> or Task or Task<TResult>.

    Task<int> Sum(int x, int y);

    Task<NetResultAndValue<int>> Random();

    Task<NetResult> Disable();
}

// On the server side, define a class that implements the interface and annotate it with NetObject attribute.
[NetObject] // Annotate NetObject attribute.
internal class TestServiceAgent : ITestService
{
    private readonly int number = RandomVault.Default.NextInt31();

    public TestServiceAgent()
    {
        Console.WriteLine("ctor");
    }

    async Task<string?> ITestService.DoubleString(string input)
        => input + input; // Simply repeat a string twice and return it.

    async Task<int> ITestService.Sum(int x, int y)
        => x + y;

    Task<NetResultAndValue<int>> ITestService.Random()
        => Task.FromResult(new NetResultAndValue<int>(this.number));

    async Task<NetResult> ITestService.Disable()
    {
        TransmissionContext.Current.ServerConnection.GetContext().DisableNetService<ITestService>();
        return NetResult.Success;
    }
}

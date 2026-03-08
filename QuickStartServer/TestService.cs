// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Crypto;
using Netsphere;

namespace QuickStart;

// Define an interface shared between the client and server.
[NetService] // Annotate NetService attribute.
public interface ITestService : INetService // An interface for NetService must inherit from INetService.
{
    Task<string?> DoubleString(string input); // Declare the service method.
    // Ensure that both arguments and return values are serializable by Tinyhand serializer, and the return type must be Task or Task<T> or Task or Task<TResult>.
}

// On the server side, define a class that implements the interface and annotate it with NetObject attribute.
[NetObject] // Annotate NetObject attribute.
internal class TestServiceAgent : ITestService, ITestService2
{
    private readonly int number = RandomVault.Default.NextInt31();

    async Task<string?> ITestService.DoubleString(string input)
        => input + input; // Simply repeat a string twice and return it.

    Task<int> ITestService2.Random()
        => Task.FromResult(this.number);
}

[NetService]
public interface ITestService2 : INetService
{
    Task<int> Random();
}

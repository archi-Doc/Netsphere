// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

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

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace xUnitTest.NetsphereTest;

[NetService]
public interface IGeneratorSignatureReviewService : INetService
{
    Task<(int Count, string? Text)> Echo((int Count, string? Text) value);

    Task<(int First, int Second, int Third, int Fourth, int Fifth, int Sixth, int Seventh, int Eighth)> EchoLong((int First, int Second, int Third, int Fourth, int Fifth, int Sixth, int Seventh, int Eighth) value);
}

[NetObject]
public class GeneratorSignatureReviewService : IGeneratorSignatureReviewService
{
    public Task<(int Count, string? Text)> Echo((int Count, string? Text) value) => Task.FromResult(value);

    public Task<(int First, int Second, int Third, int Fourth, int Fifth, int Sixth, int Seventh, int Eighth)> EchoLong((int First, int Second, int Third, int Fourth, int Fifth, int Sixth, int Seventh, int Eighth) value) => Task.FromResult(value);
}

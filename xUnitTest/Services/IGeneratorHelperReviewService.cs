// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace xUnitTest.NetsphereTest;

public interface IGeneratorHelperReviewBase : INetService
{
    static int Double(int value) => value * 2;

    Task<int> Calculate(int value) => Task.FromResult(this.AddOne(Double(value)));

    private int AddOne(int value) => value + 1;
}

[NetService]
public interface IGeneratorHelperReviewService : IGeneratorHelperReviewBase
{
    static int Triple(int value) => value * 3;

    Task<int> CalculateDirect(int value) => Task.FromResult(this.AddTwo(Triple(value)));

    private int AddTwo(int value) => value + 2;
}

[NetObject]
public class GeneratorHelperReviewService : IGeneratorHelperReviewService
{
}

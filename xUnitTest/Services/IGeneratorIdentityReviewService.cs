// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace xUnitTest.NetsphereTest;

public static class GeneratorIdentityReviewContracts
{
    [NetService]
    public interface INestedService : INetService
    {
        Task<int> Echo(int value);
    }

    [NetObject]
    public class NestedService : INestedService
    {
        public Task<int> Echo(int value) => Task.FromResult(value);
    }
}

[NetService(0x7fc4e101)]
public interface IConstructorIdentityReviewService : INetService
{
    Task<int> Echo(int value);
}

[NetObject]
public class ConstructorIdentityReviewService : IConstructorIdentityReviewService
{
    public Task<int> Echo(int value) => Task.FromResult(value);
}

[NetService(ServiceId = 0x7fc4e102)]
public interface INamedIdentityReviewService : INetService
{
    Task<int> Echo(int value);
}

[NetObject]
public class NamedIdentityReviewService : INamedIdentityReviewService
{
    public Task<int> Echo(int value) => Task.FromResult(value);
}

[NetService(0x7fc4e103, ServiceId = 0x7fc4e104)]
public interface IOverriddenIdentityReviewService : INetService
{
    Task<int> Echo(int value);
}

[NetObject]
public class OverriddenIdentityReviewService : IOverriddenIdentityReviewService
{
    public Task<int> Echo(int value) => Task.FromResult(value);
}

[NetService(0x7fc4e105, ServiceId = 0)]
public interface IAutomaticIdentityReviewService : INetService
{
    Task<int> Echo(int value);
}

[NetObject]
public class AutomaticIdentityReviewService : IAutomaticIdentityReviewService
{
    public Task<int> Echo(int value) => Task.FromResult(value);
}

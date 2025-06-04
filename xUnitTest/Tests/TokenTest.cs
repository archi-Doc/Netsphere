// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc;
using Netsphere;
using Netsphere.Crypto;
using Xunit;

namespace xUnitTest;

public class TokenTest
{
    [Fact]
    public void Test1()
    {
        var seedKey = SeedKey.NewSignature();
        var authenticationToken = AuthenticationToken.UnsafeConstructor();
        seedKey.SignWithSalt(authenticationToken, 123);

        var st = authenticationToken.ConvertToString();
        AuthenticationToken.TryParse(st, out var authenticationToken2, out _);
        authenticationToken.Equals(authenticationToken2).IsTrue();
    }
}

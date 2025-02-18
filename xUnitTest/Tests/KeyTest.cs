// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;
using Xunit;

namespace xUnitTest;

public class KeyTest
{
    [Fact]
    public void Test1()
    {
        var seedKey = SeedKey.New(KeyOrientation.Signature);
        var st = seedKey.UnsafeToString();
        SeedKey.TryParse(st, out var seedKey2);
        seedKey.Equals(seedKey2).IsTrue();

        seedKey = SeedKey.New(KeyOrientation.Encryption);
        st = seedKey.UnsafeToString();
        SeedKey.TryParse(st, out seedKey2);
        seedKey.Equals(seedKey2).IsTrue();

        SeedKey.TryParse("!!!FoZqwj1Bvy5dRNMLbtgLQDzdc3wOd2Sw75qm7ifev8vsY4JL!!!(s:cDlMibfEAW29DgjeRzxx7eqOw5KayiVVQEXlcryiTrI28xnW)", out seedKey).IsTrue();
        SeedKey.TryParse("!!!FoZqwj1Bvy5dRNMLbtgLQDzdc3wOd2Sw75qm7ifev8vsY4JL!!!(cDlMibfEAW29DgjeRzxx7eqOw5KayiVVQEXlcryiTrI28xnW)", out seedKey).IsFalse();
        SeedKey.TryParse("!!!FoZqwj1Bvy5dRNMLbtgLQDzdc3wOd2Sw75qm7ifev8vsY4JL!!!(e:cDlMibfEAW29DgjeRzxx7eqOw5KayiVVQEXlcryiTrI28xnW)", out seedKey).IsFalse();
        SeedKey.TryParse("!!!FoZqwj1Bvy5dRNMLbtgLQDzdc3wOd2Sw75qm7ifev8vsY4JL!!!", out seedKey).IsTrue();
    }
}

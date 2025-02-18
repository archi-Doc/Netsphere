// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Misc;

[TinyhandObject]
public partial class TestBlock : IEquatable<TestBlock>
{
    public const int DataMax = 4_000_000;

    public static TestBlock Create(int size = DataMax)
    {
        size = size < DataMax ? size : DataMax;

        var testBlock = new TestBlock();
        testBlock.N = 10;
        testBlock.Message = "Test message";
        testBlock.Data = new byte[size];
        for (var n = 0; n < testBlock.Data.Length; n++)
        {
            testBlock.Data[n] = (byte)n;
        }

        return testBlock;
    }

    [Key(0)]
    public int N { get; set; }

    [Key(1)]
    public string Message { get; set; } = string.Empty;

    [Key(2)]
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public override string ToString()
        => $"TestBlock: {this.N}, {this.Message}, Size:{this.Data.Length}, Hash:{Arc.Crypto.FarmHash.Hash64(this.Data).To4Hex()}";

    public bool Equals(TestBlock? other)
    {
        if (other is null)
        {
            return false;
        }

        return this.N == other.N &&
            this.Message == other.Message &&
            this.Data.SequenceEqual(other.Data);
    }
}

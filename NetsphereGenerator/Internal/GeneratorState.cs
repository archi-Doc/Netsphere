// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Visceral;

namespace Netsphere.Generator;

public class GeneratorState
{
    public int FormatterCount { get; set; } = 1;

    public List<string> ModuleInitializerClasses { get; } = new();

    public bool TryGetBlock(string blockKey, out GeneratorBlock block) => this.keyToBlock.TryGetValue(blockKey, out block);

    public bool TryCreateBlock(string blockKey, out GeneratorBlock block)
    {
        if (this.TryGetBlock(blockKey, out block))
        {// Already exists.
            return false;
        }

        // Create new block.
        block = new GeneratorBlock(blockKey, this.blockSerialNumber++);
        this.keyToBlock[blockKey] = block;
        return true;
    }

    public void FinalizeBlock(ScopingStringBuilder ssb)
    {
        foreach (var x in this.keyToBlock.Values)
        {
            ssb.Append(x.Ssb);
        }
    }

    private int blockSerialNumber;
    private Dictionary<string, GeneratorBlock> keyToBlock = new();
}

public class GeneratorBlock
{
    public string BlockKey { get; }

    public int SerialNumber { get; }

    public ScopingStringBuilder Ssb { get; }

    public GeneratorBlock(string blockKey, int serialNumber)
    {
        this.BlockKey = blockKey;
        this.SerialNumber = serialNumber;
        this.Ssb = new();
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Netsphere;

[TinyhandObject]
public partial record ConnectionAgreement
{
    public static readonly ConnectionAgreement Default = new();
    internal const ulong UpdateId = 0x54074a0294a59b25;
    internal const ulong BidirectionalId = 0x7432bf385bf192da;
    internal const ulong AuthenticationTokenId = 0xa0637663baed28e9;

    public ConnectionAgreement()
    {
        this.MaxTransmissions = 4; // 4 transmissions
        this.MaxBlockSize = 4 * 1024 * 1024; // 4MB
        this.MaxStreamLength = 0; // Disabled
        this.StreamBufferSize = 8 * 1024 * 1024; // 8MB
        this.EnableBidirectionalConnection = false; // Bidirectional communication is not allowed
        this.MinimumConnectionRetentionMics = Mics.FromSeconds(5); // 5 seconds
    }

    /// <summary>
    /// Gets or sets the maximum transmissions per connection.
    /// </summary>
    [Key(0)]
    public uint MaxTransmissions { get; set; }

    /// <summary>
    /// Gets or sets the maximum block size in bytes.
    /// </summary>
    [Key(1)]
    public int MaxBlockSize
    {
        get => this.maxBlockSize;
        set
        {
            this.maxBlockSize = value;
            var info = NetHelper.CalculateGene(this.maxBlockSize);
            this.MaxBlockGenes = info.NumberOfGenes;
        }
    }

    /// <summary>
    /// Gets or sets the maximum stream length in bytes.
    /// </summary>
    [Key(2)]
    public long MaxStreamLength
    {
        get => this.maxStreamLength;
        set
        {
            this.maxStreamLength = value;
            var info = NetHelper.CalculateGene(this.maxStreamLength);
            // this.MaxStreamGenes = info.NumberOfGenes;
        }
    }

    /// <summary>
    /// Gets or sets the stream buffer size in bytes.
    /// </summary>
    [Key(3)]
    public int StreamBufferSize
    {
        get => this.streamBufferSize;
        set
        {
            this.streamBufferSize = value;
            var info = NetHelper.CalculateGene(this.streamBufferSize);
            this.StreamBufferGenes = info.NumberOfGenes;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether to enable bidirectional connections.
    /// </summary>
    [Key(4)]
    public bool EnableBidirectionalConnection { get; set; }

    [Key(5)]
    public long MinimumConnectionRetentionMics { get; set; }

    [IgnoreMember]
    public int MaxBlockGenes { get; private set; }

    [IgnoreMember]
    public int StreamBufferGenes { get; private set; }

    private int maxBlockSize;
    private long maxStreamLength;
    private int streamBufferSize;

    public void AcceptAll(ConnectionAgreement? target)
    {
        if (target is null)
        {
            return;
        }

        this.MaxTransmissions = Math.Max(this.MaxTransmissions, target.MaxTransmissions);
        this.MaxBlockSize = Math.Max(this.MaxBlockSize, target.MaxBlockSize);

        if (target.MaxStreamLength == -1)
        {
            this.MaxStreamLength = -1;
        }
        else if (target.MaxStreamLength > this.MaxStreamLength)
        {
            this.MaxStreamLength = target.MaxStreamLength;
        }

        this.StreamBufferSize = Math.Max(this.StreamBufferSize, target.StreamBufferSize);
        this.EnableBidirectionalConnection |= target.EnableBidirectionalConnection;
        this.MinimumConnectionRetentionMics = Math.Max(this.MinimumConnectionRetentionMics, target.MinimumConnectionRetentionMics);
    }

    /// <summary>
    /// Determines whether the agreement is within the range compared to the target.<br/>
    /// Returns <see langword="true"/> if it is within the range.
    /// </summary>
    /// <param name="target">The comparand.</param>
    /// <returns><see langword="true"/>; The agreement is within the target.</returns>
    public bool IsInclusive(ConnectionAgreement target)
    {
        if (this.MaxTransmissions > target.MaxTransmissions)
        {
            return false;
        }
        else if (this.MaxBlockSize > target.MaxBlockSize)
        {
            return false;
        }
        else if (target.MaxStreamLength >= 0)
        {
            if (this.MaxStreamLength < 0)
            {
                return false;
            }
            else if (this.MaxStreamLength > target.MaxStreamLength)
            {
                return false;
            }
        }
        else if (this.StreamBufferSize > target.StreamBufferSize)
        {
            return false;
        }
        else if (this.EnableBidirectionalConnection && !target.EnableBidirectionalConnection)
        {
            return false;
        }
        else if (this.MinimumConnectionRetentionMics > target.MinimumConnectionRetentionMics)
        {
            return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CheckStreamLength(long maxStreamLength)
    {
        if (this.maxStreamLength < 0)
        {
            return true;
        }

        return this.maxStreamLength >= maxStreamLength;
    }
}

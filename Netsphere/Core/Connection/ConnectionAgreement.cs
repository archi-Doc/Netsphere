// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Netsphere;

/// <summary>
/// Defines the transmission limits and capabilities of a connection.
/// </summary>
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
        this.TransmissionTimeout = NetConstants.DefaultTransmissionTimeout; // 4 seconds
    }

    /// <summary>
    /// Gets or sets the maximum number of concurrent transmissions per connection.
    /// </summary>
    [Key(0)]
    public uint MaxTransmissions { get; set; }

    /// <summary>
    /// Gets or sets the maximum serialized block size in bytes, including serialization headers.
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
    /// Gets or sets the maximum stream length in bytes; negative values remove the length limit.
    /// </summary>
    /// <remarks>Zero permits only empty streams. Individual requests must declare a nonnegative length.</remarks>
    [Key(2)]
    public long MaxStreamLength
    {
        get => this.maxStreamLength;
        set
        {
            this.maxStreamLength = value;
            // this.MaxStreamGenes = info.NumberOfGenes;
        }
    }

    /// <summary>
    /// Gets or sets the stream window size in bytes, rounded to packet capacity.
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

    [Key(6)]
    public TimeSpan TransmissionTimeout { get; set; }

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
        else if (this.MaxStreamLength >= 0 && target.MaxStreamLength > this.MaxStreamLength)
        {
            this.MaxStreamLength = target.MaxStreamLength;
        }

        this.StreamBufferSize = Math.Max(this.StreamBufferSize, target.StreamBufferSize);
        this.EnableBidirectionalConnection |= target.EnableBidirectionalConnection;
        this.MinimumConnectionRetentionMics = Math.Max(this.MinimumConnectionRetentionMics, target.MinimumConnectionRetentionMics);
        this.TransmissionTimeout = this.TransmissionTimeout > target.TransmissionTimeout ? this.TransmissionTimeout : target.TransmissionTimeout;
    }

    /// <summary>
    /// Determines whether this agreement fits within the target's limits and capabilities.<br/>
    /// Returns <see langword="true"/> if it is within the range.
    /// </summary>
    /// <param name="target">The limits and capabilities to compare against.</param>
    /// <returns>True if the target permits this agreement; otherwise, false.</returns>
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

        if (this.StreamBufferSize > target.StreamBufferSize)
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
        else if (this.TransmissionTimeout > target.TransmissionTimeout)
        {
            return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CheckStreamLength(long maxStreamLength)
    {
        if (maxStreamLength < 0)
        {
            return false;
        }

        if (this.maxStreamLength < 0)
        {
            return true;
        }

        return this.maxStreamLength >= maxStreamLength;
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

[TinyhandObject]
public partial record RelayAgreement
{
    public RelayAgreement()
    {
        this.KnownMinimumRetentionMics = Mics.FromSeconds(5); // 5 seconds
    }

    [Key(0)]
    public RelayId OuterRelayId { get; set; }

    [Key(1)]
    public NetNode OuterNode { get; set; } = default!;

    [Key(2)]
    public long KnownMinimumRetentionMics { get; set; }

    [Key(3)]
    public int UnknownPacketsPerSecond { get; set; } = 0;
}

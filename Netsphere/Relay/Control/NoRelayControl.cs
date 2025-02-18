// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Relay;

public class NoRelayControl : IRelayControl
{
    public static readonly IRelayControl Instance = new NoRelayControl();

    public int MaxRelayExchanges
        => 0;

    public long DefaultRelayRetensionMics
        => 0;

    public long DefaultMaxRelayPoint
        => 0;

    public long DefaultRestrictedIntervalMics
        => 0;
}

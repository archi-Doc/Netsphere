// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Responder;

namespace Netsphere.Relay;

/// <summary>
/// Defines relay allocation policy and limits.
/// </summary>
public interface IRelayControl
{
    int MaxRelayExchanges { get; }

    long DefaultRelayRetensionMics { get; }

    long DefaultMaxRelayPoint { get; }

    long DefaultRestrictedIntervalMics { get; }

    void RegisterResponder(ResponderControl responders)
    {
    }
}

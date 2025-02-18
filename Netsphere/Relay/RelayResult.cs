// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Represents a result of relay operation.
/// </summary>
public enum RelayResult : byte
{
    Success,
    ConnectionFailure,
    SerialRelayLimit,
    RelayExchangeLimit,
    InvalidEndpoint,
    DuplicateEndpoint,
    DuplicateRelayId,
}

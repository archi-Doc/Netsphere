// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Represents a result of network transmission.
/// </summary>
public enum NetResult : byte
{
    Success,
    Completed,
    UnknownError,
    Timeout,
    Canceled,
    Closed,
    Refused,
    InvalidData,
    InvalidOperation,
    InvalidEndpoint,
    InvalidRelay,
    SerializationFailed,
    DeserializationFailed,
    BlockSizeLimit,
    StreamLengthLimit,
    NoNetwork,
    NoNetService,
    NoTransmission,
    NotReceived,
    NotAuthenticated,
    NotFound,
}

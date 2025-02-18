// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Represents a result of network transmission.
/// </summary>
public enum UpdateVersionResult : byte
{
    Success,
    DeserializationFailed,
    WrongVersionIdentifier,
    WrongPublicKey,
    WrongSignature,
    OldMics,
    FutureMics,
}

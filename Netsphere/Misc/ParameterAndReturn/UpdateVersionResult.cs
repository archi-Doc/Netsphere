// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Identifies the outcome of a version update request.
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

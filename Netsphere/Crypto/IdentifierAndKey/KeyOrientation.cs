// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Crypto;

/// <summary>
/// Specifies whether key material is intended for encryption or signing.
/// </summary>
public enum KeyOrientation
{
    NotSpecified,
    Encryption,
    Signature,
}

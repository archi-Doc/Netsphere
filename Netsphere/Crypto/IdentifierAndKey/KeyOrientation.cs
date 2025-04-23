// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Crypto;

/// <summary>
///  <see cref="SeedKey"/> is designed to be used for both Signature and Encryption (although this may not be recommended).<br/>
///  Specify the intended purpose the key.
/// </summary>
public enum KeyOrientation
{
    NotSpecified,
    Encryption,
    Signature,
}

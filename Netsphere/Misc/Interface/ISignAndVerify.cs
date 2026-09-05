// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere;

/// <summary>
/// Defines signing data, signature metadata, and validation for an object.
/// </summary>
public interface ISignAndVerify : IValidatable
{
    SignaturePublicKey PublicKey { get; set; }

    byte[] Signature { get; set; }

    long SignedMics { get; set; }

    ulong Salt { get; set; }
}

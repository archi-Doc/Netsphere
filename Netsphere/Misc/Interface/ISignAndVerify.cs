// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere;

/// <summary>
/// An interface for adding a signature to an object or authenticating a signature.
/// </summary>
public interface ISignAndVerify : IValidatable
{
    SignaturePublicKey PublicKey { get; set; }

    byte[] Signature { get; set; }

    long SignedMics { get; set; }

    ulong Salt { get; set; }
}

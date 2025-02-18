// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere;

/// <summary>
/// Validate that object members and verify that the signature is appropriate.
/// </summary>
public interface IVerifiable : IValidatable
{
    SignaturePublicKey PublicKey { get; }

    byte[] Signature { get; }
}

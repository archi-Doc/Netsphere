// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere;

/// <summary>
/// Defines content validation and signature verification.
/// </summary>
public interface IVerifiable : IValidatable
{
    SignaturePublicKey PublicKey { get; }

    byte[] Signature { get; }
}

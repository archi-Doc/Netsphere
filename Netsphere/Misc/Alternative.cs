// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere;

public static class Alternative
{
    public const string Name = "alternative";
    public const ushort Port = 49151;

    static Alternative()
    {
        SeedKey = SeedKey.NewSignature();
        PublicKey = SeedKey.GetEncryptionPublicKey();
        NetAddress = new(IPAddress.Loopback, Port);
        NetNode = new(NetAddress, PublicKey);
    }

    public static readonly SeedKey SeedKey;
    public static readonly EncryptionPublicKey PublicKey;
    public static readonly NetAddress NetAddress;
    public static readonly NetNode NetNode;
}

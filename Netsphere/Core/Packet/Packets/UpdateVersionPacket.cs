// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;
using Netsphere.Packet;
using Netsphere.Version;

namespace Netsphere.Relay;

[TinyhandObject]
public sealed partial class UpdateVersionPacket : IPacket
{
    public static PacketType PacketType => PacketType.UpdateVersion;

    public UpdateVersionPacket()
    {
    }

    public UpdateVersionPacket(CertificateToken<VersionInfo> token)
    {
        this.Token = token;
    }

    [Key(0)]
    public CertificateToken<VersionInfo>? Token { get; set; }
}

[TinyhandObject]
public sealed partial class UpdateVersionResponse : IPacket
{
    public static PacketType PacketType => PacketType.UpdateVersionResponse;

    public UpdateVersionResponse()
    {
    }

    public UpdateVersionResponse(UpdateVersionResult result)
    {
        this.Result = result;
    }

    [Key(0)]
    public UpdateVersionResult Result { get; set; }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Netsphere.Crypto;
using Netsphere.Packet;
using Netsphere.Version;

namespace Netsphere.Relay;

/// <summary>
/// Requests version information for a specified version kind.
/// </summary>
[TinyhandObject]
public sealed partial class GetVersionPacket : IPacket
{
    public static PacketType PacketType => PacketType.GetVersion;

    public GetVersionPacket()
    {
    }

    public GetVersionPacket(VersionInfo.Kind versionKind)
    {
        this.VersionKind = versionKind;
    }

    [Key(0)]
    public VersionInfo.Kind VersionKind { get; set; }
}

/// <summary>
/// Returns signed version information.
/// </summary>
[TinyhandObject]
public sealed partial class GetVersionResponse : IPacket
{
    public static PacketType PacketType => PacketType.GetVersionResponse;

    public GetVersionResponse()
    {
    }

    public GetVersionResponse(CertificateToken<VersionInfo> token)
    {
        this.Token = token;
    }

    [Key(0)]
    public CertificateToken<VersionInfo>? Token { get; set; }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Packet;

public enum PacketType : ushort
{
    // Packet types (0-127)
    Connect = 0,
    Ping,
    Punch,
    GetInformation,
    PingRelay,
    GetVersion,
    UpdateVersion,
    OpenSesami,

    // Response packet types (128-255)
    ConnectResponse = 128,
    PingResponse,
    PunchResponse,
    GetInformationResponse,
    PingRelayResponse,
    GetVersionResponse,
    UpdateVersionResponse,
    OpenSesamiResponse,

    // Protected types (256-383)
    Protected = 256,

    // Protected response types (384-512)
    ProtectedResponse = 384,
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Packet;

/// <summary>
/// Defines the type identifier of a Tinyhand-serializable datagram packet.
/// </summary>
/// <remarks>Packets must be Tinyhand-serializable, have a unique packet type, and fit within the datagram payload limit.</remarks>
public interface IPacket
{
    static abstract PacketType PacketType { get; }
}

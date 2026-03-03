// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Represents a network object that can respond to connection closure events.
/// </summary>
public interface INetObject
{
    /// <summary>
    /// Called when the network connection associated with this object is closed.
    /// </summary>
    void OnConnectionClosed();
}

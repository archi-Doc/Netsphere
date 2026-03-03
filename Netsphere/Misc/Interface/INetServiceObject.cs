// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// An attribute added to a NetServiceObject class that provides a NetService, allowing connection-related callback methods to be added.
/// </summary>
public interface INetServiceObject
{
    /// <summary>
    /// Called when the network connection associated with this object is closed.
    /// </summary>
    void OnConnectionClosed();
}

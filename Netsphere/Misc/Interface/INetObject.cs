// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Provides connection lifecycle callbacks for a network service implementation.
/// </summary>
public interface INetObject
{
    /// <summary>
    /// Called when the network connection associated with this object is closed.
    /// </summary>
    void OnConnectionClosed();
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Provides a cleanup callback for a network service implementation.
/// </summary>
public interface INetObject
{
    /// <summary>
    /// Called when this service is disabled or its connection context is disposed.
    /// </summary>
    /// <remarks>A shared implementation may receive this callback from multiple connections.</remarks>
    void OnConnectionClosed();
}

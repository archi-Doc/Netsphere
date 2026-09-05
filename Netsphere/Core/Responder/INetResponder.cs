// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Core;

/// <summary>
/// Handles a block or stream request identified by a data identifier.
/// </summary>
public interface INetResponder
{
    ulong DataId { get; }

    void Respond(TransmissionContext transmissionContext);
}

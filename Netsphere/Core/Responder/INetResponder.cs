// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Core;

public interface INetResponder
{
    ulong DataId { get; }

    void Respond(TransmissionContext transmissionContext);
}

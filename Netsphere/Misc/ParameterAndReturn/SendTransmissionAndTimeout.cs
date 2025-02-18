// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Core;

namespace Netsphere;

internal readonly struct SendTransmissionAndTimeout : IDisposable
{
    public SendTransmissionAndTimeout(SendTransmission? transmission, TimeSpan timeout)
    {
        this.Transmission = transmission;
        this.Timeout = timeout;
    }

    public void Dispose()
        => this.Transmission?.Dispose();

    public readonly SendTransmission? Transmission;
    public readonly TimeSpan Timeout;
}

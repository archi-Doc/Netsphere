// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Contains a transport result and an optional pooled response buffer.
/// </summary>
/// <remarks>Call <see cref="Return"/> exactly once after consuming the buffer. Copying this value does not acquire another lease; do not use any copy after returning it.</remarks>
public readonly record struct NetResponse
{
    public NetResponse(NetResult result, ulong dataId, long additional, BytePool.RentMemory received)
    {
        this.Result = result;
        this.DataId = dataId;
        this.Received = received;
        this.Additional = additional;
    }

    public NetResponse(NetResult result)
    {
        this.Result = result;
    }

    public bool IsFailure => this.Result != NetResult.Success;

    public bool IsSuccess => this.Result == NetResult.Success;

    public void Return() => this.Received.Return();

    public readonly NetResult Result;
    public readonly ulong DataId;
    public readonly long Additional; // ElapsedMics, MaxStreamLength
    public readonly BytePool.RentMemory Received;
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Core;

namespace Netsphere;

public class SendStreamAndReceive<TReceive> : SendStreamBase
{
    internal SendStreamAndReceive(SendTransmission sendTransmission, long maxLength, ulong dataId)
        : base(sendTransmission, maxLength, dataId)
    {
    }

    public Task<NetResultValue<TReceive>> CompleteSendAndReceive(CancellationToken cancellationToken = default)
        => this.InternalComplete<TReceive>(cancellationToken);
}

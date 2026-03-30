// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere.Internal;

public interface IClientConnectionInternal
{
    Task<(NetResult Result, ulong DataId, BytePool.RentMemory Value)> RpcSendAndReceive(BytePool.RentMemory data, ulong dataId);

    void RpcSendAndReceive2(BytePool.RentMemory data, ulong dataId, IResponseChannelInternal channel);

    Task<(NetResult Result, ReceiveStream? Stream)> RpcSendAndReceiveStream(BytePool.RentMemory data, ulong dataId);

    Task<NetResult> UpdateAgreement(ulong dataId, CertificateToken<ConnectionAgreement> a1);

    Task<NetResult> ConnectBidirectionally(ulong dataId, CertificateToken<ConnectionAgreement>? a1);
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Crypto;

namespace Netsphere.Internal;

public interface IClientConnectionInternal
{
    Task<(NetResult Result, ulong DataId, BytePool.RentMemory Value)> RpcSendAndReceive(BytePool.RentMemory data, ulong dataId);

    Task<(NetResult Result, ReceiveStream? Stream)> RpcSendAndReceiveStream(BytePool.RentMemory data, ulong dataId);

    Task<ServiceResponse<NetResult>> UpdateAgreement(ulong dataId, CertificateToken<ConnectionAgreement> a1);

    Task<ServiceResponse<NetResult>> ConnectBidirectionally(ulong dataId, CertificateToken<ConnectionAgreement>? a1);
}

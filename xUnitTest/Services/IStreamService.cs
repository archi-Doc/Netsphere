// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Crypto;
using Netsphere;
using Netsphere.Crypto;

namespace xUnitTest.NetsphereTest;

[NetServiceInterface]
public interface IStreamService : INetService, INetServiceAgreement
{
    NetTask<byte[]?> Pingpong(byte[] data);

    NetTask<ulong> GetHash(byte[] data);

    NetTask<ReceiveStream?> Get(string name, long length);

    NetTask<SendStreamAndReceive<NetResult>?> Put(long maxLength);

    NetTask<SendStreamAndReceive<ulong>?> PutAndGetHash(long maxLength);

    NetTask<SendStreamAndReceive<NetResult>?> Put2(ulong hash, long maxLength);
}

[NetServiceObject]
public class StreamServiceImpl : IStreamService
{
    public const long MaxStreamLength = 100_000_000;

    public async NetTask<byte[]?> Pingpong(byte[] data)
        => data;

    public async NetTask<ulong> GetHash(byte[] data)
        => Arc.Crypto.FarmHash.Hash64(data);

    public async NetTask<ReceiveStream?> Get(string name, long length)
    {
        length = Math.Min(length, MaxStreamLength);
        var r = new Xoshiro256StarStar((ulong)length);
        var buffer = new byte[length];
        r.NextBytes(buffer);

        var (_, stream) = TransmissionContext.Current.GetSendStream(length, Arc.Crypto.FarmHash.Hash64(buffer));

        if (stream is not null)
        {
            await stream.Send(buffer);
            await stream.Complete();
        }

        return default;
    }

    public async NetTask<SendStreamAndReceive<NetResult>?> Put(long maxLength)
    {
        return default;
    }

    public async NetTask<SendStreamAndReceive<ulong>?> PutAndGetHash(long maxLength)
    {
        var transmissionContext = TransmissionContext.Current;
        var stream = transmissionContext.GetReceiveStream<ulong>();

        var buffer = new byte[100_000];
        var hash = new FarmHash();
        hash.HashInitialize();
        long total = 0;

        while (true)
        {
            var r = await stream.Receive(buffer);
            if (r.Result == NetResult.Success ||
                r.Result == NetResult.Completed)
            {
                hash.HashUpdate(buffer.AsMemory(0, r.Written).Span);
                total += r.Written;
            }
            else
            {
                break;
            }

            if (r.Result == NetResult.Completed)
            {
                stream.SendAndDispose(BitConverter.ToUInt64(hash.HashFinal()));
                // transmissionContext.SendAndForget(BitConverter.ToUInt64(hash.HashFinal()));
                break;
            }
        }

        return default;
    }

    public async NetTask<NetResult> UpdateAgreement(CertificateToken<ConnectionAgreement> token)
        => NetResult.Success;

    async NetTask<SendStreamAndReceive<NetResult>?> IStreamService.Put2(ulong hash, long maxLength)
    {
        var transmissionContext = TransmissionContext.Current;
        var stream = transmissionContext.GetReceiveStream();

        // return default;

        var buffer = new byte[100];
        var farmHash = new FarmHash();
        farmHash.HashInitialize();
        long total = 0;

        while (true)
        {
            var r = await stream.Receive(buffer);
            if (r.Result == NetResult.Success ||
                r.Result == NetResult.Completed)
            {
                farmHash.HashUpdate(buffer.AsMemory(0, r.Written).Span);
                total += r.Written;
            }
            else
            {
                break;
            }

            if (r.Result == NetResult.Completed)
            {
                break;
            }
        }

        var hash2 = BitConverter.ToUInt64(farmHash.HashFinal());
        if (hash == hash2)
        {
            transmissionContext.Result = NetResult.Success;
        }

        return default;
    }
}

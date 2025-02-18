using Arc.Crypto;
using Netsphere.Crypto;

namespace Lp.NetServices;

[NetServiceObject]
public class RemoteBenchHostAgent : IRemoteBenchHost, IRemoteBenchService
{
    public RemoteBenchHostAgent()
    {
    }

    public async NetTask<NetResult> Start(int total, int concurrent)
    {
        return NetResult.Success;
    }

    public async NetTask Report(RemoteBenchRecord record)
    {
    }

    public async NetTask<byte[]?> Pingpong(byte[] data)
    {
        return data;
    }

    async NetTask<ulong> IRemoteBenchService.GetHash(byte[] data)
    {
        return default;
    }

    public async NetTask<SendStreamAndReceive<ulong>?> GetHash(long maxLength)
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
                break;
            }
        }

        return default;
    }

    public async NetTask<NetResult> ConnectBidirectionally(CertificateToken<ConnectionAgreement>? token)
    {
        var context = TransmissionContext.Current;
        if (token is null ||
           !context.ServerConnection.ValidateAndVerifyWithSalt(token))
        {
            return NetResult.NotAuthenticated;
        }

        var clientConnection = context.ServerConnection.PrepareBidirectionalConnection();
        var service = clientConnection.GetService<IRemoteBenchRunner>();
        if (service is not null)
        {
            var result = await service.Start(10_000, 20, default, default);
        }

        return NetResult.Success;
    }

    public async NetTask<NetResult> UpdateAgreement(CertificateToken<ConnectionAgreement> token)
    {
        if (!TransmissionContext.Current.ServerConnection.ValidateAndVerifyWithSalt(token))
        {
            return NetResult.NotAuthenticated;
        }

        return NetResult.Success;
    }
}

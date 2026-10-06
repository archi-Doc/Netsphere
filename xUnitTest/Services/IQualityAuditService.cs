// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;

namespace xUnitTest.NetsphereTest;

[NetService]
public interface IQualityAuditService : INetService
{
    Task<SendStreamAndReceive<NetResult>?> PutText(string text, long maxLength);

    void Channel(string text, ref ResponseChannel<int> channel);
}

[NetObject]
public class QualityAuditService : IQualityAuditService
{
    public async Task<SendStreamAndReceive<NetResult>?> PutText(string text, long maxLength)
    {
        var context = TransmissionContext.Current;
        var stream = context.GetReceiveStream();
        var buffer = new byte[100];
        while (true)
        {
            var r = await stream.Receive(buffer);
            if (r.Result == NetResult.Completed)
            {
                break;
            }
            else if (r.Result != NetResult.Success)
            {
                return default;
            }
        }

        context.Result = text.Length > 0 ? NetResult.Success : NetResult.InvalidData;
        return default;
    }

    public void Channel(string text, ref ResponseChannel<int> channel)
        => channel.SetResponse(text.Length);
}

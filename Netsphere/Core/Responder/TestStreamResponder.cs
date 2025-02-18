// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere.Core;

namespace Netsphere.Responder;

public class TestStreamResponder : INetResponder
{
    public const int MaxLength = 1024 * 1024 * 100;

    public static readonly INetResponder Instance = new TestStreamResponder();

    public ulong DataId
        => 123456789;

    public void Respond(TransmissionContext transmissionContext)
    {
        if (!TinyhandSerializer.TryDeserialize<int>(transmissionContext.RentMemory.Memory.Span, out var size))
        {
            transmissionContext.SendResultAndForget(NetResult.DeserializationFailed);
            transmissionContext.Return();
            return;
        }

        Task.Run(async () =>
        {
            size = Math.Min(size, MaxLength);
            var r = new Xoshiro256StarStar((ulong)size);
            var buffer = new byte[size];
            r.NextBytes(buffer);

            var (_, stream) = transmissionContext.GetSendStream(size, FarmHash.Hash64(buffer));
            if (stream is not null)
            {
                await stream.Send(buffer);
                await stream.Complete();
            }
        });
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Core;

public abstract class AsyncResponder<TSend, TReceive> : INetResponder
{
    public ulong DataId
        => NetHelper.GetDataId<TSend, TReceive>();

    public virtual NetResultValue<TReceive> RespondAsync(TSend value) => default;

    public void Respond(TransmissionContext transmissionContext)
    {
        if (!TinyhandSerializer.TryDeserialize<TSend>(transmissionContext.RentMemory.Memory.Span, out var t))
        {
            transmissionContext.Return();
            transmissionContext.SendResultAndForget(NetResult.DeserializationFailed);
            return;
        }

        transmissionContext.Return();

        _ = Task.Run(() =>
        {
            this.ServerConnection = transmissionContext.ServerConnection;
            var r = this.RespondAsync(t);
            if (r.Value is not null)
            {
                transmissionContext.SendAndForget(r.Value, this.DataId);
            }
            else
            {
                transmissionContext.SendResultAndForget(r.Result);
            }
        });
    }

    protected ServerConnection ServerConnection { get; private set; } = default!;
}

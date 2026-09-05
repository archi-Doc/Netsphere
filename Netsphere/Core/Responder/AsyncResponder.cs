// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere.Core;

public abstract class AsyncResponder<TSend, TReceive> : INetResponder
{
    public ulong DataId
        => NetHelper.GetDataId<TSend, TReceive>();

    public virtual NetResultAndValue<TReceive> RespondAsync(TSend value) => default;

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
            var previousContext = TransmissionContext.AsyncLocal.Value;
            TransmissionContext.AsyncLocal.Value = transmissionContext;
            try
            {
                var r = this.RespondAsync(t);
                if (r.Value is not null)
                {
                    transmissionContext.SendAndForget(r.Value, this.DataId);
                }
                else
                {
                    transmissionContext.SendResultAndForget(r.Result);
                }
            }
            finally
            {
                TransmissionContext.AsyncLocal.Value = previousContext;
            }
        });
    }

    protected ServerConnection ServerConnection => TransmissionContext.Current.ServerConnection;
}

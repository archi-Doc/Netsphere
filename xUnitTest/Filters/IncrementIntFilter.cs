// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Netsphere;
using Tinyhand;

namespace xUnitTest.NetsphereTest;

public class IncrementIntFilter : IServiceFilter
{
    public async Task Invoke(TransmissionContext context, Func<TransmissionContext, Task> invoker)
    {
        if (TinyhandSerializer.TryDeserialize<int>(context.RentMemory.Memory.Span, out var value))
        {
            if (NetHelper.TrySerialize(value + 1, out var rentMemory))
            {
                context.RentMemory.Return();
                context.RentMemory = rentMemory;
            }
        }

        await invoker(context).ConfigureAwait(false);
    }
}

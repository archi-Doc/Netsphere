// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

public interface IServiceFilterBase
{
    public void SetArguments(object[] args)
    {
    }
}

public interface IServiceFilter : IServiceFilterBase
{
    public Task Invoke(TransmissionContext context, Func<TransmissionContext, Task> invoker);
}

// Currently disabled.
/*public interface IServiceFilterSync : IServiceFilterBase
{
    public void Invoke(CallContext context, Action<CallContext> invoker);
}*/

// Currently disabled.
/*public interface IServiceFilter<TCallContext> : IServiceFilterBase
    where TCallContext : CallContext
{
    public Task Invoke(TCallContext context, Func<TCallContext, Task> invoker);
}*/

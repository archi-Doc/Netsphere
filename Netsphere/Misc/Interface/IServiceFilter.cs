// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Accepts arguments configured by a service filter attribute.
/// </summary>
public interface IServiceFilterBase
{
    public void SetArguments(object[] args)
    {
    }
}

/// <summary>
/// Intercepts network service calls, including ResponseChannel handlers.
/// </summary>
/// <remarks>Await the continuation before accessing the response. DI-provided instances may serve concurrent requests.</remarks>
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

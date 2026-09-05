// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Defines initialization shared by network service filters.
/// </summary>
public interface IServiceFilterBase
{
    public void SetArguments(object[] args)
    {
    }
}

/// <summary>
/// Intercepts asynchronous network service calls.
/// </summary>
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

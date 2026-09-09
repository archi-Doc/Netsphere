// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

/// <summary>
/// Applies a service filter to a network object or service method.
/// </summary>
/// <typeparam name="TFilter">The service filter type.</typeparam>
/// <remarks>A parameterless filter is created per invocation. Other filters are resolved from DI and follow its registration lifetime.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
public class NetServiceFilterAttribute<TFilter> : Attribute
    where TFilter : IServiceFilter
{
    /// <summary>
    /// Gets or sets the execution order. Lower values run first across class and method filters.
    /// </summary>
    public int Order { get; set; } = int.MaxValue;

    public object[] Arguments { get; set; } = Array.Empty<object>();

    public NetServiceFilterAttribute()
    {
    }
}

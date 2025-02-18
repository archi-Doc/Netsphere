// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
public class NetServiceFilterAttribute<TFilter> : Attribute
    where TFilter : IServiceFilter
{
    public int Order { get; set; } = int.MaxValue;

    public object[] Arguments { get; set; } = Array.Empty<object>();

    public NetServiceFilterAttribute()
    {
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

namespace Netsphere;

/// <summary>
/// Represents a net result and value.
/// </summary>
/// <typeparam name="TValue">The type of the value.</typeparam>
public readonly struct NetResultValue<TValue>
{
    public NetResultValue(NetResult result, TValue value)
    {
        this.Result = result;
        this.Value = value;
    }

    public NetResultValue(NetResult result)
    {
        this.Result = result;
        if (typeof(TValue) == typeof(NetResult))
        {
            this.Value = Unsafe.As<NetResult, TValue>(ref result);
        }
        else
        {
            this.Value = default;
        }
    }

    public bool IsFailure => this.Result != NetResult.Success;

    public bool IsSuccess => this.Result == NetResult.Success;

    public readonly NetResult Result;
    public readonly TValue? Value;

    public override string ToString()
        => $"Result: {this.Result.ToString()}, Value: {this.Value?.ToString()}";
}

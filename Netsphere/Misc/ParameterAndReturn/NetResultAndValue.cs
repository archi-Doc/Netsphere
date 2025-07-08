// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Netsphere;

/// <summary>
/// Represents a net result and value.
/// </summary>
/// <typeparam name="TValue">The type of the value.</typeparam>
public readonly record struct NetResultAndValue<TValue>
{
    public NetResultAndValue(NetResult result, TValue value)
    {
        this.Result = result;
        this.Value = value;
    }

    public NetResultAndValue(NetResult result)
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

    public NetResultAndValue(TValue value)
    {
        this.Result = NetResult.Success;
        this.Value = value;
    }

    public bool IsFailure => this.Result != NetResult.Success;

    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsSuccess => this.Result == NetResult.Success && this.Value is not null;

    public readonly NetResult Result;
    public readonly TValue? Value;

    public override string ToString()
        => $"Result: {this.Result.ToString()}, Value: {this.Value?.ToString()}";
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Netsphere;

public readonly record struct ServiceResponse
{
    public ServiceResponse(NetResult result)
    {
        this.result = result;
    }

    public NetResult Result => this.result;

    public bool IsSuccess => this.result == NetResult.Success;

    public override string ToString() => $"{this.result.ToString()}";

    private readonly NetResult result;
}

public readonly record struct ServiceResponse<T>
{
    public ServiceResponse(T value)
        : this(value, default)
    {
    }

    public ServiceResponse(T value, NetResult result)
    {
        this.value = value;
        this.result = result;
    }

    public T Value => this.value;

    public NetResult Result => this.result;

    public bool IsSuccess => this.result == NetResult.Success;

    public override string ToString()
    {
        if (this.value == null)
        {
            return this.result.ToString();
        }
        else
        {
            return $"{this.result.ToString()}: {this.value.ToString()}";
        }
    }

    private readonly T value;
    private readonly NetResult result;
}

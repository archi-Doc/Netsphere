// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;

#pragma warning disable SA1304 // Non-private readonly fields should begin with upper-case letter
#pragma warning disable SA1307 // Accessible fields should begin with upper-case letter

namespace Netsphere;

/// <summary>
/// Represents the result of a net service call as Task-like.
/// </summary>
/// <typeparam name="TResponse">The type of the result.</typeparam>
[AsyncMethodBuilder(typeof(AsyncNetTaskMethodBuilder<>))]
public readonly struct NetTask<TResponse>
{
    internal readonly bool hasRawValue;
    internal readonly TResponse rawValue;
    internal readonly Task<TResponse>? rawTaskValue;
    internal readonly Task<ServiceResponse<TResponse>>? response;

    public NetTask(TResponse rawValue)
    {
        this.hasRawValue = true;
        this.rawValue = rawValue;
        this.rawTaskValue = default;
        this.response = default;
    }

    public NetTask(Task<TResponse> rawTaskValue)
    {
        this.hasRawValue = true;
        this.rawValue = default!;
        this.rawTaskValue = rawTaskValue ?? throw new ArgumentNullException(nameof(rawTaskValue));
        this.response = default;
    }

    public NetTask(Task<ServiceResponse<TResponse>> response)
    {
        this.hasRawValue = false;
        this.rawValue = default!;
        this.rawTaskValue = default!;
        this.response = response ?? throw new ArgumentNullException(nameof(response));
    }

    /// <summary>
    /// Gets asynchronous call result (value).
    /// </summary>
    public Task<TResponse> ValueAsync
    {
        get
        {
            if (!this.hasRawValue)
            {
                if (this.response == null)
                {
                    return Task.FromResult(default(TResponse)!);
                }
                else
                {
                    static async Task<TResponse> RawToResponse(Task<ServiceResponse<TResponse>> task)
                    {
                        var response = await task.ConfigureAwait(false);
                        return response.Value;
                    }

                    return RawToResponse(this.response);
                }
            }
            else if (this.rawTaskValue != null)
            {
                return this.rawTaskValue;
            }
            else
            {
                return Task.FromResult(this.rawValue);
            }
        }
    }

    /// <summary>
    /// Gets asynchronous call result (response).
    /// </summary>
    public Task<ServiceResponse<TResponse>> ResponseAsync
    {
        get
        {
            if (!this.hasRawValue)
            {
                if (this.response == null)
                {
                    return Task.FromResult(default(ServiceResponse<TResponse>));
                }
                else
                {
                    return this.response;
                }
            }
            else if (this.rawTaskValue != null)
            {
                static async Task<ServiceResponse<TResponse>> RawToResponse(Task<TResponse> task)
                {
                    var raw = await task.ConfigureAwait(false);
                    return new ServiceResponse<TResponse>(raw);
                }

                return RawToResponse(this.rawTaskValue);
            }
            else
            {
                return Task.FromResult(new ServiceResponse<TResponse>(this.rawValue));
            }
        }
    }

    /// <summary>
    /// Gets an awaiter.
    /// </summary>
    /// <returns>An awaiter instance.</returns>
    public TaskAwaiter<TResponse> GetAwaiter() => this.ValueAsync.GetAwaiter();
}

/// <summary>
/// Represents the result of a net service call as Task-like.
/// </summary>
[AsyncMethodBuilder(typeof(AsyncNetTaskMethodBuilder))]
public readonly struct NetTask
{
    /// <summary>
    /// Creates a <see cref="NetTask{TResponse}"/> with the specified result.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="value">The value to store into the completed task.</param>
    /// <returns><see cref="NetTask{TResponse}"/> instance.</returns>
    public static NetTask<T> FromResult<T>(T value) => new NetTask<T>(value);

    /// <summary>
    /// Creates a <see cref="NetTask{TResponse}"/> with the specified result task.
    /// </summary>
    /// <typeparam name="T">The type of the result.</typeparam>
    /// <param name="task">The specified result task.</param>
    /// <returns><see cref="NetTask{TResponse}"/> instance.</returns>
    public static NetTask<T> FromResult<T>(Task<T> task) => new NetTask<T>(task);

    internal readonly bool hasRawValue;
    internal readonly Task? rawTaskValue;
    internal readonly Task<ServiceResponse>? response;

    public NetTask()
    {
        this.hasRawValue = true;
        this.rawTaskValue = default;
        this.response = default;
    }

    public NetTask(Task rawTaskValue)
    {
        this.hasRawValue = true;
        this.rawTaskValue = rawTaskValue ?? throw new ArgumentNullException(nameof(rawTaskValue));
        this.response = default;
    }

    public NetTask(Task<ServiceResponse> response)
    {
        this.hasRawValue = false;
        this.rawTaskValue = default!;
        this.response = response ?? throw new ArgumentNullException(nameof(response));
    }

    /// <summary>
    /// Gets asynchronous call result (value).
    /// </summary>
    public Task ValueAsync
    {
        get
        {
            if (!this.hasRawValue)
            {
                if (this.response == null)
                {
                    return Task.CompletedTask;
                }
                else
                {
                    return this.response;
                }
            }
            else if (this.rawTaskValue != null)
            {
                return this.rawTaskValue;
            }
            else
            {
                return Task.CompletedTask;
            }
        }
    }

    /// <summary>
    /// Gets asynchronous call result (response).
    /// </summary>
    public Task<ServiceResponse> ResponseAsync
    {
        get
        {
            if (!this.hasRawValue)
            {
                if (this.response == null)
                {
                    return Task.FromResult(default(ServiceResponse));
                }
                else
                {
                    return this.response;
                }
            }
            else if (this.rawTaskValue != null)
            {
                static async Task<ServiceResponse> RawToResponse(Task task)
                {
                    await task.ConfigureAwait(false);
                    return default(ServiceResponse);
                }

                return RawToResponse(this.rawTaskValue);
            }
            else
            {
                return Task.FromResult(default(ServiceResponse));
            }
        }
    }

    /// <summary>
    /// Gets an awaiter.
    /// </summary>
    /// <returns>An awaiter instance.</returns>
    public TaskAwaiter GetAwaiter() => this.ValueAsync.GetAwaiter();
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Security;

namespace Netsphere;

public struct AsyncNetTaskMethodBuilder<T>
{
    private AsyncTaskMethodBuilder<T> methodBuilder;
    private T result;
    private bool haveResult;
    private bool useBuilder;

    public static AsyncNetTaskMethodBuilder<T> Create()
    {
        return new AsyncNetTaskMethodBuilder<T>() { methodBuilder = AsyncTaskMethodBuilder<T>.Create() };
    }

    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        this.methodBuilder.Start(ref stateMachine);
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine)
    {
        this.methodBuilder.SetStateMachine(stateMachine);
    }

    public void SetResult(T result)
    {
        if (this.useBuilder)
        {
            this.methodBuilder.SetResult(result);
        }
        else
        {
            this.result = result;
            this.haveResult = true;
        }
    }

    public void SetException(Exception exception)
    {
        this.methodBuilder.SetException(exception);
    }

    public NetTask<T> Task
    {
        get
        {
            if (this.haveResult)
            {
                return new NetTask<T>(this.result);
            }
            else
            {
                this.useBuilder = true;
                return new NetTask<T>(this.methodBuilder.Task);
            }
        }
    }

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        this.useBuilder = true;
        this.methodBuilder.AwaitOnCompleted(ref awaiter, ref stateMachine);
    }

    [SecuritySafeCritical]
    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        this.useBuilder = true;
        this.methodBuilder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }
}

public struct AsyncNetTaskMethodBuilder
{
    private AsyncTaskMethodBuilder methodBuilder;
    private bool haveResult;
    private bool useBuilder;

    public static AsyncNetTaskMethodBuilder Create()
    {
        return new AsyncNetTaskMethodBuilder() { methodBuilder = AsyncTaskMethodBuilder.Create() };
    }

    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        this.methodBuilder.Start(ref stateMachine);
    }

    public void SetStateMachine(IAsyncStateMachine stateMachine)
    {
        this.methodBuilder.SetStateMachine(stateMachine);
    }

    public void SetResult()
    {
        if (this.useBuilder)
        {
            this.methodBuilder.SetResult();
        }
        else
        {
            this.haveResult = true;
        }
    }

    public void SetException(Exception exception)
    {
        this.methodBuilder.SetException(exception);
    }

    public NetTask Task
    {
        get
        {
            if (this.haveResult)
            {
                return default(NetTask);
            }
            else
            {
                this.useBuilder = true;
                return new NetTask(this.methodBuilder.Task);
            }
        }
    }

    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        this.useBuilder = true;
        this.methodBuilder.AwaitOnCompleted(ref awaiter, ref stateMachine);
    }

    [SecuritySafeCritical]
    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine
    {
        this.useBuilder = true;
        this.methodBuilder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }
}

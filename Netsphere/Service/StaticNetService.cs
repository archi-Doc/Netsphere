// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Netsphere;

#pragma warning disable SA1401 // Fields should be private

public static class StaticNetService
{
    public delegate INetService FrontendFactoryDelegate(ClientConnection clientConnection);

    internal static ThreadsafeTypeKeyHashtable<NetServiceInfo> ServiceInfoTable = new();
    private static ThreadsafeTypeKeyHashtable<NetObjectInfo> objectInfoTable = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetServiceId<TService>()
        => TinyhandTypeIdentifier.GetTypeIdentifier<TService>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint GetServiceId(Type serviceType)
        => TinyhandTypeIdentifier.GetTypeIdentifier(serviceType);

    public static void SetFrontendFactory<TService>(FrontendFactoryDelegate factoryDelegate)
        where TService : INetService
    {
        DelegateCache<TService>.FactoryDelegate = factoryDelegate;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TNetService CreateFrontend<TNetService>(ClientConnection clientConnection)
        where TNetService : INetService
    {
        var factoryDelegate = DelegateCache<TNetService>.FactoryDelegate;
        if (factoryDelegate is not null && factoryDelegate(clientConnection) is TNetService service)
        {
            return service;
        }

        throw new InvalidOperationException($"Could not create a frontend instance of NetService '{typeof(TNetService).ToString()}'.");
    }

    public static NetObjectInfo GetOrAddNetObjectInfo(Type objectType, Func<object>? factory)
        => objectInfoTable.GetOrAdd(objectType, x =>
        {
            return new(objectType, factory);
        });

    public static bool TryGetNetObjectInfo(Type objectType, [MaybeNullWhen(false)] out NetObjectInfo netObjectInfo)
        => objectInfoTable.TryGetValue(objectType, out netObjectInfo);

    public static bool AddNetService<TNetService, TNetObject>(bool enableByDefault)
        where TNetService : class, INetService
        where TNetObject : class, TNetService
    {
        if (!objectInfoTable.TryGetValue(typeof(TNetObject), out var netObjectInfo))
        {
            return false;
        }

        return ServiceInfoTable.TryAdd(typeof(TNetService), serviceType => new(serviceType, netObjectInfo, enableByDefault));
    }

    private static class DelegateCache<T>
    {
        internal static FrontendFactoryDelegate? FactoryDelegate;
#pragma warning restore SA1401 // Fields should be private

        static DelegateCache()
        {
        }
    }
}

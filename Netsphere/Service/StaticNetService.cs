// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Netsphere;

#pragma warning disable SA1401 // Fields should be private

public static class StaticNetService
{
    public delegate INetService FrontendFactoryDelegate(ClientConnection clientConnection);

    internal static Dictionary<Type, NetServiceObject> ServiceToObject = new();
    private static ThreadsafeTypeKeyHashtable<NetServiceObject> typeToObject = new();

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

    public static NetServiceObject GetOrAddNetServiceObject(Type objectType, Func<object>? factory)
        => typeToObject.GetOrAdd(objectType, x =>
        {
            return new(objectType, factory);
        });

    public static bool TryGetNetServiceObject(Type objectType, [MaybeNullWhen(false)] out NetServiceObject netServiceObject)
        => typeToObject.TryGetValue(objectType, out netServiceObject);

    public static bool AddNetService<TNetService, TNetObject>()
        where TNetService : INetService
        where TNetObject : class, TNetService
    {
        if (!typeToObject.TryGetValue(typeof(TNetObject), out var netServiceObject))
        {
            return false;
        }

        return ServiceToObject.TryAdd(typeof(TNetService), netServiceObject);
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

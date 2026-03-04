// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Netsphere;

public static class StaticNetService
{
    public delegate INetService FrontendFactoryDelegate(ClientConnection clientConnection);

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
    public static TService CreateFrontend<TService>(ClientConnection clientConnection)
        where TService : INetService
    {
        var factoryDelegate = DelegateCache<TService>.FactoryDelegate;
        if (factoryDelegate is not null && factoryDelegate(clientConnection) is TService service)
        {
            return service;
        }

        throw new InvalidOperationException($"Could not create a frontend instance of NetService '{typeof(TService).ToString()}'.");
    }

    public static NetServiceObject GetOrAddNetServiceObject(Type objectType, Func<object>? factory)
        => typeToObject.GetOrAdd(objectType, x =>
        {
            return new(objectType, factory);
        });

    public static bool TryGetNetServiceObject(Type agentType, [MaybeNullWhen(false)] out NetServiceObject info)
        => typeToObject.TryGetValue(agentType, out info);

    public static bool AddNetService<TService, TAgent>(bool enableByDefault)
        where TService : INetService
        where TAgent : class, TService
    {
        if (!typeToObject.TryGetValue(typeof(TAgent), out var netServiceObject))
        {
            return false;
        }

        if (enableByDefault)
        {
        }

        return serviceToObject.TryAdd(typeof(TService), netServiceObject);
    }

    private static ThreadsafeTypeKeyHashtable<NetServiceObject> typeToObject = new();
    private static ThreadsafeTypeKeyHashtable<NetServiceObject> serviceToObject = new();
    // private static UInt32Hashtable<NetServiceObject> serviceIdToAgentInfo = new();

    private static class DelegateCache<T>
    {
#pragma warning disable SA1401 // Fields should be private
        internal static FrontendFactoryDelegate? FactoryDelegate;
#pragma warning restore SA1401 // Fields should be private

        static DelegateCache()
        {
        }
    }
}

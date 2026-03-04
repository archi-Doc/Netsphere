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

    public static NetServiceObject GetOrAddAgentInformation(Type agentType, Func<object>? createAgent)
    {
        return typeToAgentInfo.GetOrAdd(agentType, x =>
        {
            return new(agentType, createAgent);
        });
    }

    public static void TryAddAgentInfo(NetServiceObject info)
        => typeToAgentInfo.TryAdd(info.Type, info);

    public static bool TryGetAgentInfo(Type agentType, [MaybeNullWhen(false)] out NetServiceObject info)
        => typeToAgentInfo.TryGetValue(agentType, out info);

    private static ThreadsafeTypeKeyHashtable<NetServiceObject> typeToAgentInfo = new();
    private static UInt32Hashtable<NetServiceObject> serviceIdToAgentInfo = new();

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

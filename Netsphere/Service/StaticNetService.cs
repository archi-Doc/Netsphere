// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace Netsphere;

public static class StaticNetService
{
    public static void SetFrontendDelegate<TService>(ServerConnectionContext.CreateFrontendDelegate @delegate)
        where TService : INetService
    {
        DelegateCache<TService>.Create = @delegate;
    }

    public static AgentInformation GetOrAddAgentInformation(Type agentType, Func<object>? createAgent)
    {
        return typeToAgentInfo.GetOrAdd(agentType, x =>
        {
            return new(agentType, createAgent);
        });
    }

    public static void TryAddAgentInfo(AgentInformation info)
        => typeToAgentInfo.TryAdd(info.AgentType, info);

    public static bool TryGetAgentInfo(Type agentType, [MaybeNullWhen(false)] out AgentInformation info)
        => typeToAgentInfo.TryGetValue(agentType, out info);

    public static TService CreateClient<TService>(ClientConnection clientConnection)
        where TService : INetService
    {
        var create = DelegateCache<TService>.Create;
        if (create != null && create(clientConnection) is TService service)
        {
            return service;
        }

        throw new InvalidOperationException($"Could not create an instance of the net service {typeof(TService).ToString()}.");
    }

    private static ThreadsafeTypeKeyHashtable<AgentInformation> typeToAgentInfo = new();
    private static UInt32Hashtable<AgentInformation> serviceIdToAgentInfo = new();

    private static class DelegateCache<T>
    {
#pragma warning disable SA1401 // Fields should be private
        internal static ServerConnectionContext.CreateFrontendDelegate? Create;
#pragma warning restore SA1401 // Fields should be private

        static DelegateCache()
        {
        }
    }
}

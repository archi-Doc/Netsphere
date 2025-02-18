// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Netsphere;

public sealed class ServiceControl
{
    public sealed class Table
    {
        public readonly record struct Agent(AgentInformation AgentInformation, int Index);

        public Table(Dictionary<uint, AgentInformation> serviceIdToAgentInformation)
        {
            var typeToIndex = new Dictionary<Type, int>();
            foreach (var (serviceId, agentInformation) in serviceIdToAgentInformation)
            {
                if (!typeToIndex.TryGetValue(agentInformation.AgentType, out var index))
                {
                    index = typeToIndex.Count;
                    typeToIndex.TryAdd(agentInformation.AgentType, index);
                }

                this.serviceIdToAgentInformation.TryAdd(serviceId, new Agent(agentInformation, index));
            }

            this.Count = typeToIndex.Count;
        }

        private UInt32Hashtable<Agent> serviceIdToAgentInformation = new();

        public int Count { get; }

        public bool TryGetAgent(uint serviceId, [MaybeNullWhen(false)] out Agent agent)
            => this.serviceIdToAgentInformation.TryGetValue(serviceId, out agent);
    }

    public ServiceControl()
    {
    }

    #region FieldAndProperty

    private readonly Lock lockObject = new();
    private readonly Dictionary<uint, AgentInformation> serviceIdToAgentInformation = new();
    private Table? table;

    public Table GetTable()
    {
        if (this.table is { } table)
        {
            return table;
        }

        var newTable = new Table(this.serviceIdToAgentInformation);
        Interlocked.CompareExchange(ref this.table, newTable, null);
        return newTable;
    }

    #endregion

    public void Register<TService, TAgent>()
        where TService : INetService
        where TAgent : class, TService
    {
        using (this.lockObject.EnterScope())
        {
            var serviceId = ServiceTypeToId<TService>();
            this.Register(serviceId, typeof(TAgent));

            this.ResetTable();
        }
    }

    public void Register(Type serviceType, Type agentType)
    {
        using (this.lockObject.EnterScope())
        {
            var serviceId = ServiceTypeToId(serviceType);
            this.Register(serviceId, agentType);

            this.ResetTable();
        }
    }

    public void Unregister<TService>()
        where TService : INetService
    {
        using (this.lockObject.EnterScope())
        {
            var serviceId = ServiceTypeToId<TService>();
            this.serviceIdToAgentInformation.Remove(serviceId);

            this.ResetTable();
        }
    }

    public void Unregister(Type serviceType)
    {
        using (this.lockObject.EnterScope())
        {
            var serviceId = ServiceTypeToId(serviceType);
            this.serviceIdToAgentInformation.Remove(serviceId);

            this.ResetTable();
        }
    }

    /*public bool TryGet<TService>([MaybeNullWhen(false)] out AgentInformation info)
        where TService : INetService
    {
        var serviceId = ServiceTypeToId<TService>();
        return this.TryGet(serviceId, out info);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(uint serviceId, [MaybeNullWhen(false)] out AgentInformation info)
        => this.serviceIdToAgentInfo.TryGetValue(serviceId, out info);*/

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ServiceTypeToId<TService>()
        where TService : INetService
        => (uint)FarmHash.Hash64(typeof(TService).FullName ?? string.Empty);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ServiceTypeToId(Type serviceType)
        => (uint)FarmHash.Hash64(serviceType.FullName ?? string.Empty);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Register(uint serviceId, Type agentType)
    {
        if (!StaticNetService.TryGetAgentInfo(agentType, out var info))
        {
            throw new InvalidOperationException("Failed to register the class with the corresponding ServiceId.");
        }

        this.serviceIdToAgentInformation.TryAdd(serviceId, info);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetTable()
    {
        this.table = default;
    }

    /*private void RebuildTable()
    {// using (this.lockObject.EnterScope())
        var newTable = new Table(this.serviceIdToAgentInformation);
        Volatile.Write(ref this.table, newTable);
    }*/
}

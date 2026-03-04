// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Netsphere;

public sealed class ServiceControl
{
    internal sealed class Table
    {
        public readonly record struct Agent(NetServiceObject AgentInformation, int Index);

        public Table(Dictionary<uint, NetServiceObject> serviceIdToAgentInformation)
        {
            var typeToIndex = new Dictionary<Type, int>();
            foreach (var (serviceId, agentInformation) in serviceIdToAgentInformation)
            {
                if (!typeToIndex.TryGetValue(agentInformation.Type, out var index))
                {
                    index = typeToIndex.Count;
                    typeToIndex.TryAdd(agentInformation.Type, index);
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
    private readonly Dictionary<uint, NetServiceObject> serviceIdToAgentInformation = new();
    private Table? table;

    #endregion

    public void Register<TService, TAgent>()
        where TService : INetService
        where TAgent : class, TService
    {
        using (this.lockObject.EnterScope())
        {
            var serviceId = StaticNetService.GetServiceId<TService>();
            this.Register(serviceId, typeof(TAgent));

            this.ResetTable();
        }
    }

    public void Register(Type serviceType, Type agentType)
    {
        using (this.lockObject.EnterScope())
        {
            var serviceId = StaticNetService.GetServiceId(serviceType);
            this.Register(serviceId, agentType);

            this.ResetTable();
        }
    }

    public void Unregister<TService>()
        where TService : INetService
    {
        using (this.lockObject.EnterScope())
        {
            var serviceId = StaticNetService.GetServiceId<TService>();
            this.serviceIdToAgentInformation.Remove(serviceId);

            this.ResetTable();
        }
    }

    public void Unregister(Type serviceType)
    {
        using (this.lockObject.EnterScope())
        {
            var serviceId = StaticNetService.GetServiceId(serviceType);
            this.serviceIdToAgentInformation.Remove(serviceId);

            this.ResetTable();
        }
    }

    internal Table GetTable()
    {
        if (this.table is { } table)
        {
            return table;
        }

        var newTable = new Table(this.serviceIdToAgentInformation);
        Interlocked.CompareExchange(ref this.table, newTable, null);
        return newTable;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Register(uint serviceId, Type agentType)
    {
        if (!StaticNetService.TryGetNetServiceObject(agentType, out var info))
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

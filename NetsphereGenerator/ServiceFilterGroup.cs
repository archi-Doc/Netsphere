// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Visceral;

#pragma warning disable RS1024 // Compare symbols correctly

namespace Netsphere.Generator;

public class ServiceFilterGroup
{
    public ServiceFilterGroup(NetsphereObject obj, ServiceFilter serviceFilter)
    {
        this.Object = obj;
        this.ServiceFilter = serviceFilter;
    }

    public class Item
    {
        public Item(NetsphereObject obj, NetsphereObject? callContextObject, string identifier, string? argument, int order, bool isAsync)
        {
            this.Object = obj;
            this.CallContextObject = callContextObject;
            this.Identifier = identifier;
            this.Arguments = argument;
            this.Order = order;
            this.IsAsync = isAsync;
        }

        public NetsphereObject Object { get; private set; }

        public NetsphereObject? CallContextObject { get; private set; }

        public string Identifier { get; private set; }

        public string? Arguments { get; private set; }

        public int Order { get; private set; }

        public bool IsAsync { get; private set; }
    }

    public static Item[]? FromClassAndMethod(ServiceFilterGroup? classFilters, ServiceFilterGroup? methodFilters)
    {
        Item[]? items = null;

        if (classFilters?.Items != null)
        {
            if (methodFilters?.Items != null)
            {
                items = classFilters.Items.Concat(methodFilters.Items).OrderBy(x => x.Order).ToArray();
            }
            else
            {
                items = classFilters.Items;
            }
        }
        else
        {
            if (methodFilters?.Items != null)
            {
                items = methodFilters.Items;
            }
            else
            {
                return null;
            }
        }

        return items;
    }

    public static void GenerateInitialize(ScopingStringBuilder ssb, string serviceProvider, Item[]? items)
    {
        if (items == null)
        {
            return;
        }

        foreach (var x in items)
        {
            var hasDefaultConstructor = false;
            foreach (var a in x.Object.GetMembers(VisceralTarget.Method))
            {
                if (a.Method_IsConstructor && a.ContainingObject == x.Object)
                {// Constructor
                    if (a.Method_Parameters.Length == 0)
                    {
                        hasDefaultConstructor = true;
                        break;
                    }
                }
            }

            // ssb.AppendLine($"this.{x.Identifier} = ({x.Object.FullName}){context}.ServiceFilters.GetOrAdd(typeof({x.Object.FullName}), x => (IServiceFilter){newInstance});");
            if (hasDefaultConstructor)
            {
                ssb.AppendLine($"{x.Identifier} ??= new {x.Object.FullName}();");
            }
            else
            {
                ssb.AppendLine($"{x.Identifier} ??= {serviceProvider}?.GetService(typeof({x.Object.FullName})) as {x.Object.FullName};");
            }

            using (var scopeNull = ssb.ScopeBrace($"if ({x.Identifier} == null)"))
            {
                ssb.AppendLine($"throw new InvalidOperationException($\"Could not create an instance of the net filter '{x.Object.FullName}'.\");");
            }

            if (x.Arguments != null)
            {
                ssb.AppendLine($"(({NetsphereBody.ServiceFilterBaseName}){x.Identifier}).{NetsphereBody.ServiceFilterSetArgumentsName}({x.Arguments});");
            }
        }
    }

    public NetsphereObject Object { get; }

    public ServiceFilter ServiceFilter { get; }

    public Item[]? Items { get; private set; }

    // public Dictionary<NetServiceFilterAttributeMock, Item>? AttributeToItem { get; private set; }

    public void CheckAndPrepare()
    {
        var errorFlag = false;
        var filterList = this.ServiceFilter.FilterList;
        var items = new Item[filterList.Count];
        var dictionary = new Dictionary<NetServiceFilterAttributeMock, Item>();
        for (var i = 0; i < filterList.Count; i++)
        {
            var obj = this.Object.Body.Add(filterList[i].FilterType!);
            bool isAsync = false;
            var filterObject = obj == null ? null : this.GetFilterObject(obj, out isAsync);
            if (obj == null || filterObject == null)
            {
                this.Object.Body.AddDiagnostic(NetsphereBody.Error_FilterTypeNotDerived, filterList[i].Location);
                errorFlag = true;
                continue;
            }

            NetsphereObject? callContextObject = null;
            if (filterObject.Generics_Arguments.Length > 0)
            {
                callContextObject = filterObject.Generics_Arguments[0];
            }

            string? argument = null;
            if (!string.IsNullOrEmpty(filterList[i].Arguments))
            {
                argument = filterList[i].Arguments;
            }

            var item = new Item(obj, callContextObject, this.Object.Identifier.GetIdentifier(), argument, filterList[i].Order, isAsync);
            items[i] = item;

            dictionary[filterList[i]] = item;
        }

        if (errorFlag)
        {
            return;
        }

        if (items.Length > 0)
        {
            this.Items = items;
            // this.AttributeToItem = dictionary;
        }
    }

    /*public Item? GetIdentifier(NetServiceFilterAttributeMock? filterAttribute)
    {
        if (this.AttributeToItem == null || filterAttribute == null)
        {
            return null;
        }

        if (this.AttributeToItem.TryGetValue(filterAttribute, out var identifier))
        {
            return identifier;
        }

        return null;
    }*/

    public void GenerateDefinition(ScopingStringBuilder ssb)
    {
        if (this.Items == null)
        {
            return;
        }

        foreach (var x in this.Items)
        {
            ssb.AppendLine($"private static {x.Object.FullName}? {x.Identifier};");
        }
    }

    private NetsphereObject? GetFilterObject(NetsphereObject obj, out bool isAsync)
    {
        isAsync = false;
        foreach (var x in obj.AllInterfaceObjects)
        {
            if (x.Generics_IsGeneric)
            {// Generic
                if (x.OriginalDefinition?.FullName == NetsphereBody.ServiceFilterSyncFullName2)
                {
                    return x;
                }
                else if (x.OriginalDefinition?.FullName == NetsphereBody.ServiceFilterAsyncFullName2)
                {
                    isAsync = true;
                    return x;
                }
            }
            else
            {// Not generic
                if (x.FullName == NetsphereBody.ServiceFilterSyncFullName)
                {
                    return x;
                }
                else if (x.FullName == NetsphereBody.ServiceFilterAsyncFullName)
                {
                    isAsync = true;
                    return x;
                }
            }
        }

        return null;
    }
}

// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Arc.Visceral;

#pragma warning disable RS1024 // Compare symbols correctly

namespace Netsphere.Generator;

public class ServiceFilterGroup
{
    public ServiceFilterGroup(NetsphereObject ownerObject, ServiceFilterSet filterSet)
    {
        this.OwnerObject = ownerObject;
        this.FilterSet = filterSet;
    }

    public class FilterItem
    {
        public FilterItem(NetsphereObject filterObject, NetsphereObject? callContextObject, string identifier, string? arguments, int order, bool isAsync)
        {
            this.FilterObject = filterObject;
            this.CallContextObject = callContextObject;
            this.Identifier = identifier;
            this.Arguments = arguments;
            this.Order = order;
            this.IsAsync = isAsync;
        }

        public NetsphereObject FilterObject { get; private set; }

        public NetsphereObject? CallContextObject { get; private set; }

        public string Identifier { get; private set; }

        public string? Arguments { get; private set; }

        public int Order { get; private set; }

        public bool IsAsync { get; private set; }
    }

    public static FilterItem[]? CombineItems(ServiceFilterGroup? classFilters, ServiceFilterGroup? methodFilters)
    {
        FilterItem[]? items = null;

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

    public static void GenerateFilterInstances(ScopingStringBuilder ssb, string serviceProvider, FilterItem[]? items)
    {
        if (items == null)
        {
            return;
        }

        foreach (var x in items)
        {
            var hasDefaultConstructor = false;
            foreach (var a in x.FilterObject.GetMembers(VisceralTarget.Method))
            {
                if (a.Method_IsConstructor && a.ContainingObject == x.FilterObject)
                {// Constructor
                    if (a.Method_Parameters.Length == 0)
                    {
                        hasDefaultConstructor = true;
                        break;
                    }
                }
            }

            // ssb.AppendLine($"this.{x.Identifier} = ({x.FilterObject.FullName}){context}.ServiceFilters.GetOrAdd(typeof({x.FilterObject.FullName}), x => (IServiceFilter){newInstance});");
            if (hasDefaultConstructor)
            {
                ssb.AppendLine($"var {x.Identifier} = new {x.FilterObject.FullName}();");
            }
            else
            {
                ssb.AppendLine($"var {x.Identifier} = {serviceProvider}?.GetService(typeof({x.FilterObject.FullName})) as {x.FilterObject.FullName};");
            }

            if (!hasDefaultConstructor)
            {
                using (var scopeNull = ssb.ScopeBrace($"if ({x.Identifier} == null)"))
                {
                    ssb.AppendLine($"throw new InvalidOperationException($\"Could not create an instance of the net filter '{x.FilterObject.FullName}'.\");");
                }
            }

            if (x.Arguments != null)
            {
                ssb.AppendLine($"(({NetsphereBody.ServiceFilterBaseName}){x.Identifier}).{NetsphereBody.ServiceFilterSetArgumentsName}({x.Arguments});");
            }
        }
    }

    public NetsphereObject OwnerObject { get; }

    public ServiceFilterSet FilterSet { get; }

    public FilterItem[]? Items { get; private set; }

    // public Dictionary<NetServiceFilterAttributeMock, Item>? AttributeToItem { get; private set; }

    public void CheckAndPrepare()
    {
        var errorFlag = false;
        var filterList = this.FilterSet.FilterList;
        var items = new FilterItem[filterList.Count];
        for (var i = 0; i < filterList.Count; i++)
        {
            var obj = this.OwnerObject.Body.Add(filterList[i].FilterTypeSymbol!);
            bool isAsync = false;
            var filterObject = obj == null ? null : this.GetFilterObject(obj, out isAsync);
            if (obj == null || filterObject == null)
            {
                this.OwnerObject.Body.AddDiagnostic(NetsphereBody.Error_FilterTypeNotDerived, filterList[i].Location);
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

            var item = new FilterItem(obj, callContextObject, this.OwnerObject.Identifier.GetIdentifier(), argument, filterList[i].Order, isAsync);
            items[i] = item;
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

    /*public FilterItem? GetIdentifier(NetServiceFilterAttributeMock? filterAttribute)
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

    private NetsphereObject? GetFilterObject(NetsphereObject obj, out bool isAsync)
    {
        isAsync = false;
        foreach (var x in obj.AllInterfaceObjects)
        {
            if (x.Generics_IsGeneric)
            {// Generic
                if (x.OriginalDefinition?.FullName == NetsphereBody.GenericServiceFilterSyncFullName)
                {
                    return x;
                }
                else if (x.OriginalDefinition?.FullName == NetsphereBody.GenericServiceFilterAsyncFullName)
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

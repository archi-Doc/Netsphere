// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.CodeAnalysis;

#pragma warning disable RS1024 // Compare symbols correctly

namespace Netsphere.Generator;

public class ServiceFilter
{
    public static ServiceFilter? CreateFromObject(NetsphereObject obj)
    {
        List<NetServiceFilterAttributeMock>? filterList = null;
        var errorFlag = false;
        foreach (var x in obj.AllAttributes)
        {
            if (x.FullName.StartsWith(NetServiceFilterAttributeMock.StartName) && x.FullName.EndsWith(">"))
            {
                NetsphereObject? genericType = default;
                // MachineObjectAttributeMock? atr = default;
                var args = x.AttributeData?.AttributeClass?.TypeArguments;
                if (args.HasValue && args.Value.Length > 0 && args.Value[0] is INamedTypeSymbol typeSymbol)
                {// AddMachineAttribute<machineType>
                    genericType = obj.Body.Add(typeSymbol);
                    if (genericType is null)
                    {
                        obj.Body.AddDiagnostic(NetsphereBody.Error_NoFilterType, x.Location);
                        errorFlag = true;
                        continue;
                    }

                    NetServiceFilterAttributeMock attr;
                    try
                    {
                        attr = NetServiceFilterAttributeMock.FromArray(x.ConstructorArguments, x.NamedArguments, x.Location);
                    }
                    catch (InvalidCastException)
                    {
                        obj.Body.AddDiagnostic(NetsphereBody.Error_AttributePropertyError, x.Location);
                        errorFlag = true;
                        continue;
                    }

                    attr.FilterType = typeSymbol;
                    filterList ??= new();
                    filterList.Add(attr);

                    /*atr = obj.TryGetObjectAttribute();
                    if (obj.ObjectAttribute is null)
                    {
                        obj.ObjectAttribute = atr;
                    }*/
                }
            }
        }

        /*foreach (var x in obj.AllAttributes.Where(a => a.FullName == NetServiceFilterAttributeMock.FullName))
        {
            NetServiceFilterAttributeMock attr;
            try
            {
                attr = NetServiceFilterAttributeMock.FromArray(x.ConstructorArguments, x.NamedArguments, x.Location);
            }
            catch (InvalidCastException)
            {
                obj.Body.AddDiagnostic(NetsphereBody.Error_AttributePropertyError, x.Location);
                errorFlag = true;
                continue;
            }

            if (attr.FilterType == null)
            {
                obj.Body.AddDiagnostic(NetsphereBody.Error_NoFilterType, x.Location);
                errorFlag = true;
                continue;
            }

            filterList ??= new();
            filterList.Add(attr);
        }*/

        if (errorFlag)
        {
            return null;
        }

        if (filterList == null)
        {// No filter attribute.
            return null;
        }

        // Check for duplicates.
        var checker2 = new HashSet<ISymbol>();
        foreach (var item in filterList)
        {
            if (item.FilterType != null && !checker2.Add(item.FilterType))
            {
                obj.Body.AddDiagnostic(NetsphereBody.Error_FilterTypeConflicted, item.Location);
                errorFlag = true;
            }
        }

        if (errorFlag)
        {
            return null;
        }

        return new ServiceFilter(filterList, checker2);
    }

    public ServiceFilter()
    {
        this.FilterList = new();
        this.FilterSet = new();
    }

    public ServiceFilter(List<NetServiceFilterAttributeMock> filterList, HashSet<ISymbol> filterSet)
    {
        this.FilterList = filterList;
        this.FilterSet = filterSet;
    }

    public ServiceFilter(ServiceFilter serviceFilter)
    {
        this.FilterList = new(serviceFilter.FilterList);
        this.FilterSet = new(serviceFilter.FilterSet);
    }

    public void TryAdd(ServiceFilter serviceFilter)
    {
        foreach (var x in serviceFilter.FilterList)
        {
            this.FilterSet.Add(x.FilterType!);
            this.FilterList.Add(x);
        }
    }

    public void TryMerge(ServiceFilter serviceFilter)
    {
        foreach (var x in serviceFilter.FilterList)
        {
            if (!this.FilterSet.Contains(x.FilterType!))
            {
                this.FilterSet.Add(x.FilterType!);
                this.FilterList.Add(x);
            }
        }
    }

    public void Sort()
    {
        this.FilterList = this.FilterList.OrderBy(a => a.Order).ToList();
    }

    public HashSet<ISymbol> FilterSet { get; private set; }

    public List<NetServiceFilterAttributeMock> FilterList { get; private set; }
}

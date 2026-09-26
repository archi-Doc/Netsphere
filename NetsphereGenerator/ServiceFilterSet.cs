// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Microsoft.CodeAnalysis;

#pragma warning disable RS1024 // Compare symbols correctly

namespace Netsphere.Generator;

public class ServiceFilterSet
{
    public static ServiceFilterSet? CreateFromObject(NetsphereObject obj)
    {
        List<NetServiceFilterAttributeMock>? filterList = null;
        var errorFlag = false;
        foreach (var x in obj.AllAttributes)
        {
            if (x.FullName.StartsWith(NetServiceFilterAttributeMock.GenericFullNamePrefix) && x.FullName.EndsWith(">"))
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
                        obj.Body.AddDiagnostic(NetsphereBody.Error_AttributePropertyType, x.Location);
                        errorFlag = true;
                        continue;
                    }

                    attr.FilterTypeSymbol = typeSymbol;
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
                obj.Body.AddDiagnostic(NetsphereBody.Error_AttributePropertyType, x.Location);
                errorFlag = true;
                continue;
            }

            if (attr.FilterTypeSymbol == null)
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
            if (item.FilterTypeSymbol != null && !checker2.Add(item.FilterTypeSymbol))
            {
                obj.Body.AddDiagnostic(NetsphereBody.Error_DuplicateFilterType, item.Location);
                errorFlag = true;
            }
        }

        if (errorFlag)
        {
            return null;
        }

        return new ServiceFilterSet(filterList);
    }

    public ServiceFilterSet()
    {
        this.FilterList = new();
    }

    public ServiceFilterSet(List<NetServiceFilterAttributeMock> filterList)
    {
        this.FilterList = filterList;
    }

    public void Sort()
    {
        this.FilterList = this.FilterList.OrderBy(a => a.Order).ToList();
    }

    public List<NetServiceFilterAttributeMock> FilterList { get; private set; }
}

# NetsphereGenerator Renames

## Summary

- Scope: public types, members, and parameters in the `NetsphereGenerator` project.
- Not changed: vendored `Arc.Visceral` and `Arc.Crypto` code, diagnostic IDs (`NSG001`–`NSG016`), diagnostic messages.
- **Generated code is unchanged.** The generated sources of all consuming projects in `Netsphere.slnx` are byte-identical before and after.
- Projects that only reference `NetsphereGenerator` as an analyzer need no changes.
- Code that references generator types directly (forks, copied generator code, generator tests) must apply the renames below.
- The emitted-files path changes because the generator type name changed (only relevant with `EmitCompilerGeneratedFiles`):
  - Old: `generated/NetsphereGenerator/Netsphere.Generator.NetsphereGeneratorV2/`
  - New: `generated/NetsphereGenerator/Netsphere.Generator.NetsphereGenerator/`

## Files

| Old | New |
|---|---|
| `GeneratorShared/INetService.cs` | `GeneratorShared/NetServiceInterfaceMock.cs` |
| `GeneratorShared/AttributeInterfaceMock.cs` | `GeneratorShared/NetsphereGeneratorOptionAttributeMock.cs` |
| (in `AttributeInterfaceMock.cs`) `AttributeHelper` | `GeneratorShared/AttributeHelper.cs` (moved, not renamed) |
| `GeneratorShared/NetServiceAttribute.cs` | `GeneratorShared/NetServiceAttributeMock.cs` |
| `GeneratorShared/NetServiceObject.cs` | `GeneratorShared/NetObjectAttributeMock.cs` |
| `GeneratorShared/NetServiceFilter.cs` | `GeneratorShared/NetServiceFilterAttributeMock.cs` |
| `ServiceFilter.cs` | `ServiceFilterSet.cs` |
| `Internal/GeneratorInformation.cs` | `Internal/GeneratorState.cs` |

## Types

| Old | New |
|---|---|
| `NetsphereGeneratorV2` | `NetsphereGenerator` |
| `INetService` (class) | `NetServiceInterfaceMock` |
| `NetsphereObjectFlag` | `NetsphereObjectFlags` |
| `ServiceMethod.Type` | `ServiceMethod.PayloadKind` |
| `ServiceMethod.MethodKind` | `ServiceMethod.ServiceMethodKind` |
| `ServiceFilter` | `ServiceFilterSet` |
| `ServiceFilterGroup.Item` | `ServiceFilterGroup.FilterItem` |
| `GeneratorInformation` | `GeneratorState` |

## Enum values

| Type | Old | New |
|---|---|---|
| `ServiceMethod.PayloadKind` | `RentMemory` | `RentedMemory` |
| `ServiceMethod.PayloadKind` | `RentReadOnlyMemory` | `RentedReadOnlyMemory` |

## Members

### NetServiceFilterAttributeMock

| Old | New |
|---|---|
| `StartName` | `GenericFullNamePrefix` |
| `FilterType` | `FilterTypeSymbol` |

### NetsphereObject

| Old | New |
|---|---|
| `ObjectFlag` | `ObjectFlags` |
| `ClassFilters` | `ClassFilterGroup` |
| `MethodToFilter` | `MethodNameToFilterGroup` |
| `ClassName` | `GeneratedClassName` |
| `CheckKeyword(string keyword, Location? location)` | `TryReserveIdentifier(string identifier, Location? location)` |

### NetsphereBody

| Old | New |
|---|---|
| `TaskFullName2` | `GenericTaskFullName` |
| `FrontendClassName` | `FrontendClassPrefix` |
| `BackendClassName` | `BackendClassPrefix` |
| `ArgumentName` | `ArgumentPrefix` |
| `ServiceFilterSyncFullName2` | `GenericServiceFilterSyncFullName` |
| `ServiceFilterAsyncFullName2` | `GenericServiceFilterAsyncFullName` |
| `IClientConnectionInternalName` | `IClientConnectionInternalFullName` |
| `ReceiveDelegateAndValueInternalName` | `IResponseChannelInternalName` |
| `Error_AttributePropertyError` | `Error_AttributePropertyType` |
| `Error_KeywordUsed` | `Error_DuplicateIdentifier` |
| `Error_INetService` | `Error_NotDerivedFromINetService` |
| `Error_FilterTypeConflicted` | `Error_DuplicateFilterType` |
| `Error_SendStreamParam` | `Error_SendStreamParameter` |
| `Error_MethodForm` | `Error_ResponseChannelMethodForm` |
| `Error_CancellationToken` | `Error_CancellationTokenPosition` |
| `GenerateFrontend(IGeneratorInformation generator, string assemblyId)` | `GenerateFrontend(IGeneratorInformation generator, string assemblySuffix)` |
| `GenerateBackend(IGeneratorInformation generator, string assemblyId)` | `GenerateBackend(IGeneratorInformation generator, string assemblySuffix)` |

### ServiceMethod

| Old | New |
|---|---|
| `RentMemoryName` | `RentedMemoryFullName` |
| `RentReadOnlyMemoryName` | `RentedReadOnlyMemoryFullName` |
| `ConnectBidirectionallyName` | `ConnectBidirectionallyMethodFullName` |
| `UpdateAgreementName` | `UpdateAgreementMethodFullName` |
| `ResponseChannelName` | `ResponseChannelPrefix` |
| `ResponseChannelFullName` | `ResponseChannelFullNamePrefix` |
| `Create(NetsphereObject obj, NetsphereObject method)` | `Create(NetsphereObject serviceInterface, NetsphereObject method)` |
| `ParameterLength` | `ParameterCount` |
| `Id` | `FullId` |
| `IdString` | `FullIdLiteral` |
| `MethodString` | `GeneratedMethodName` |
| `ReturnObject` | `TaskResultObject` |
| `ParameterType` | `ParameterKind` |
| `ReturnType` | `ReturnKind` |
| `GenericsType` | `ResultTypeArgumentName` |
| `GetParameterCount(int decrement)` | `GetParameterCount(int excludedTrailingCount)` |
| `GetReturnTypeName()` | `GetTaskResultTypeName()` |
| `GetParameterFormatterRegistrations(int decrement)` | `GetValueTupleTypeArgumentLists(int excludedTrailingCount)` |
| `GetParameters()` | `GetParameterDeclarations()` |
| `GetParameterNames(string name, int decrement)` | `GetParameterNames(string prefix, int excludedTrailingCount)` |
| `TryGetNullCheck(string name, int decrement, out string statement)` | `TryGetNullCheck(string valueName, int excludedTrailingCount, out string condition)` |
| `GetParameterTypes(int decrement)` | `GetParameterTypes(int excludedTrailingCount)` |
| `GetTupleNames(string name, int decrement, bool hasCancellationTokenParameter)` | `GetTupleItemArguments(string tupleName, int excludedTrailingCount, bool hasCancellationTokenParameter)` |

### ServiceFilterSet (was ServiceFilter)

| Old | New |
|---|---|
| `ServiceFilter(ServiceFilter serviceFilter)` | `ServiceFilterSet(ServiceFilterSet filterSet)` |
| `TryAdd(ServiceFilter serviceFilter)` | `AddRange(ServiceFilterSet filterSet)` |
| `TryMerge(ServiceFilter serviceFilter)` | `Merge(ServiceFilterSet filterSet)` |

### ServiceFilterGroup

| Old | New |
|---|---|
| `ServiceFilterGroup(NetsphereObject obj, ServiceFilter serviceFilter)` | `ServiceFilterGroup(NetsphereObject ownerObject, ServiceFilterSet filterSet)` |
| `Object` | `OwnerObject` |
| `ServiceFilter` (property) | `FilterSet` |
| `FromClassAndMethod(...)` | `CombineItems(...)` |
| `GenerateInitialize(...)` | `GenerateFilterInstances(...)` |
| `Item(NetsphereObject obj, ..., string? argument, ...)` | `FilterItem(NetsphereObject filterObject, ..., string? arguments, ...)` |
| `Item.Object` | `FilterItem.FilterObject` |

### GeneratorState (was GeneratorInformation)

| Old | New |
|---|---|
| `ModuleInitializerClass` | `ModuleInitializerClasses` |
| `CreateBlock(string blockKey, out GeneratorBlock block)` | `TryCreateBlock(string blockKey, out GeneratorBlock block)` |
| `GeneratorBlock.SSB` | `GeneratorBlock.Ssb` |

## Search-and-replace cautions

Do not replace these names blindly; limit replacements to the generator types listed above.

- `ReturnType`: Roslyn `IMethodSymbol.ReturnType` is unchanged.
- `Type`, `MethodKind`: may refer to `System.Type` or `Microsoft.CodeAnalysis.MethodKind`.
- `Id`, `Item`, `Object`, `ClassName`, `ServiceFilter`: common words used elsewhere.
- `RentMemory`: `TransmissionContext.RentMemory` inside generated-code string literals is unchanged.

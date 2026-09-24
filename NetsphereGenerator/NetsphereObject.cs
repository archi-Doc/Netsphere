// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Arc.Visceral;
using Microsoft.CodeAnalysis;

#pragma warning disable SA1202 // Elements should be ordered by access
#pragma warning disable SA1204 // Static elements should appear before instance elements
#pragma warning disable SA1602 // Enumeration items should be documented

namespace Netsphere.Generator;

public enum DeclarationCondition
{
    NotDeclared, // Not declared
    ImplicitlyDeclared, // declared (implicitly)
    ExplicitlyDeclared, // declared (explicitly interface)
}

[Flags]
public enum NetsphereObjectFlags
{
    Configured = 1 << 0,
    Checked = 1 << 2,

    NetService = 1 << 10, // NetService
    NetObject = 1 << 11, // NetObject
    HasDefaultConstructor = 1 << 12, // Has default constructor
}

public class NetsphereObject : VisceralObjectBase<NetsphereObject>
{
    public NetsphereObject()
    {
    }

    public new NetsphereBody Body => (NetsphereBody)((VisceralObjectBase<NetsphereObject>)this).Body;

    public NetsphereObjectFlags ObjectFlags { get; private set; }

    public NetObjectAttributeMock? NetObjectAttribute { get; private set; }

    public NetServiceAttributeMock? NetServiceAttribute { get; private set; }

    public VisceralIdentifier Identifier { get; private set; } = VisceralIdentifier.Default;

    public List<NetsphereObject>? ServiceInterfaces { get; private set; } // For NetObjectAttribute; Net service interfaces implemented by this net service object.

    // public NetsphereObject? NetServiceBase { get; private set; } // For NetObjectAttribute; Net service base implemented by this net service object.

    public ServiceFilterGroup? ClassFilterGroup { get; private set; } // For NetObjectAttribute; Service filters.

    public Dictionary<string, ServiceFilterGroup>? MethodNameToFilterGroup { get; private set; } // For NetObjectAttribute; Method full name to Service filters.

    public Dictionary<uint, ServiceMethod>? ServiceMethods { get; private set; } // For NetService; Methods included in this net service interface.

    public string GeneratedClassName { get; set; } = string.Empty;

    public void Configure()
    {
        if (this.ObjectFlags.HasFlag(NetsphereObjectFlags.Configured))
        {
            return;
        }

        this.ObjectFlags |= NetsphereObjectFlags.Configured;

        if (this.AllAttributes.FirstOrDefault(x => x.FullName == NetObjectAttributeMock.FullName) is { } objectAttribute)
        {// NetObjectAttribute
            try
            {
                this.NetObjectAttribute = NetObjectAttributeMock.FromArray(objectAttribute.ConstructorArguments, objectAttribute.NamedArguments);
                this.NetObjectAttribute.Location = objectAttribute.Location;
                this.ObjectFlags |= NetsphereObjectFlags.NetObject;
            }
            catch (InvalidCastException)
            {
                this.Body.AddDiagnostic(NetsphereBody.Error_AttributePropertyType, objectAttribute.Location);
            }
        }
        else if (TryGetNetServiceAttribute(this))
        {// NetServiceAttribute
        }
        else
        {
            return;
        }

        // Generic type is not supported.
        if (this.Generics_Kind != VisceralGenericsKind.NotGeneric)
        {
            this.Body.AddDiagnostic(NetsphereBody.Error_GenericType, this.Location);
            return;
        }

        // Must be derived from INetService
        if (!this.AllInterfaces.Any(x => x == NetServiceInterfaceMock.FullName))
        {
            this.Body.AddDiagnostic(NetsphereBody.Error_NotDerivedFromINetService, this.Location);
            return;
        }

        // Used keywords
        this.Identifier = new VisceralIdentifier("__gen_ns_identifier__");
        foreach (var x in this.AllMembers.Where(a => a.ContainingObject == this))
        {
            this.Identifier.Add(x.SimpleName);
        }

        if (this.NetServiceAttribute != null)
        {// NetService
            if (this.Body.IdToNetInterface.TryGetValue(this.NetServiceAttribute.ServiceId, out var obj))
            {
                this.Body.AddDiagnostic(NetsphereBody.Error_DuplicateServiceId, this.NetServiceAttribute.Location, this.NetServiceAttribute.ServiceId);
                this.Body.AddDiagnostic(NetsphereBody.Error_DuplicateServiceId, obj.NetServiceAttribute!.Location, obj.NetServiceAttribute!.ServiceId);
            }
            else
            {
                this.Body.IdToNetInterface.Add(this.NetServiceAttribute.ServiceId, this);
            }
        }
        else if (this.NetObjectAttribute != null)
        {// NetObject
            var accessibility = this.AccessibilityName;
            if (accessibility != "public" && accessibility != "internal")
            {
                this.Body.AddDiagnostic(NetsphereBody.Error_Accessibility, this.Location);
                return;
            }

            this.ServiceInterfaces = new();
            foreach (var x in this.InterfaceObjects)
            {
                if (x.AllInterfaces.Any(x => x == NetServiceInterfaceMock.FullName))
                {
                    if (x.NetServiceAttribute == null)
                    {
                        if (!TryGetNetServiceAttribute(x))
                        {
                            continue;
                        }

                        x.Check();
                    }

                    this.ServiceInterfaces.Add(x);
                }
            }

            if (this.ServiceInterfaces.Count == 0)
            {
                return;
            }

            this.ConfigureServiceFilters();

            this.Body.NetObjects.Add(this);
        }

        static bool TryGetNetServiceAttribute(NetsphereObject obj)
        {
            if (obj.AllAttributes.FirstOrDefault(x => x.FullName == NetServiceAttributeMock.FullName) is { } interfaceAttribute)
            {// NetServiceAttribute
                try
                {
                    obj.NetServiceAttribute = NetServiceAttributeMock.FromArray(interfaceAttribute.ConstructorArguments, interfaceAttribute.NamedArguments);
                    obj.NetServiceAttribute.Location = interfaceAttribute.Location;
                    obj.ObjectFlags |= NetsphereObjectFlags.NetService;

                    // Service ID
                    if (obj.NetServiceAttribute.ServiceId == 0)
                    {
                        obj.NetServiceAttribute.ServiceId = (uint)Arc.Crypto.FarmHash.Hash64(obj.FullName);
                    }

                    return true;
                }
                catch (InvalidCastException)
                {
                    obj.Body.AddDiagnostic(NetsphereBody.Error_AttributePropertyType, interfaceAttribute.Location);
                    return false;
                }
            }

            return false;
        }
    }

    public void ConfigureServiceFilters()
    {
        var classFilters = ServiceFilterSet.CreateFromObject(this) ?? new ServiceFilterSet();
        classFilters.Sort();
        this.ClassFilterGroup = new ServiceFilterGroup(this, classFilters);
        this.ClassFilterGroup.CheckAndPrepare();

        this.MethodNameToFilterGroup ??= new();
        foreach (var x in this.GetMembers(VisceralTarget.Method))
        {
            var methodFilters = ServiceFilterSet.CreateFromObject(x);
            if (methodFilters != null)
            {
                methodFilters.Sort();
                var filterGroup = new ServiceFilterGroup(this, methodFilters);
                filterGroup.CheckAndPrepare();
                this.MethodNameToFilterGroup[x.FullName] = filterGroup;
            }
        }
    }

    public bool TryReserveIdentifier(string identifier, Location? location = null)
    {
        if (!this.Identifier.Add(identifier))
        {
            this.Body.AddDiagnostic(NetsphereBody.Error_DuplicateIdentifier, location ?? Location.None, this.SimpleName, identifier);
            return false;
        }

        return true;
    }

    public void Check()
    {
        if (this.ObjectFlags.HasFlag(NetsphereObjectFlags.Checked))
        {
            return;
        }

        this.ObjectFlags |= NetsphereObjectFlags.Checked;

        if (this.NetObjectAttribute != null)
        {// NetObject
            this.GeneratedClassName = NetsphereBody.BackendClassPrefix + Arc.Crypto.FarmHash.Hash32(this.FullName).ToString("x");

            if (this.ServiceInterfaces != null)
            {
                foreach (var x in this.ServiceInterfaces)
                {
                    if (x.NetServiceAttribute != null)
                    {
                        if (this.Body.IdToNetObject.TryGetValue(x.NetServiceAttribute.ServiceId, out var obj))
                        {
                            var serviceInterface = x.ToString();
                            this.Body.AddDiagnostic(NetsphereBody.Error_DuplicateServiceObject, obj.Location, serviceInterface);
                            this.Body.AddDiagnostic(NetsphereBody.Error_DuplicateServiceObject, this.Location, serviceInterface);
                        }
                        else
                        {
                            this.Body.IdToNetObject.Add(x.NetServiceAttribute.ServiceId, this);
                        }
                    }
                }
            }

            foreach (var x in this.GetMembers(VisceralTarget.Method))
            {
                if (x.Method_IsConstructor && x.ContainingObject == this)
                {// Constructor
                    if (x.Method_Parameters.Length == 0)
                    {
                        this.ObjectFlags |= NetsphereObjectFlags.HasDefaultConstructor;
                        break;
                    }
                }
            }
        }
        else if (this.NetServiceAttribute != null)
        {// NetService
            this.GeneratedClassName = NetsphereBody.FrontendClassPrefix + this.NetServiceAttribute.ServiceId.ToString("x");

            foreach (var x in this.GetMembers(VisceralTarget.Method))
            {
                AddMethod(this, x);
            }

            foreach (var @interface in this.AllInterfaceObjects)
            {
                foreach (var x in @interface.GetMembers(VisceralTarget.Method).Where(y => y.ContainingObject == @interface))
                {
                    AddMethod(this, x);
                }
            }
        }

        static void AddMethod(NetsphereObject obj, NetsphereObject method)
        {
            var serviceMethod = ServiceMethod.Create(obj, method);
            if (serviceMethod == null)
            {
                return;
            }

            // Add
            obj.ServiceMethods ??= new();
            if (obj.ServiceMethods.TryGetValue(serviceMethod.MethodId, out var s))
            {// Duplicated
                obj.Body.AddDiagnostic(NetsphereBody.Error_DuplicateServiceMethod, s.Location, serviceMethod.MethodId);
                obj.Body.AddDiagnostic(NetsphereBody.Error_DuplicateServiceMethod, serviceMethod.Location, serviceMethod.MethodId);
            }
            else
            {
                obj.ServiceMethods.Add(serviceMethod.MethodId, serviceMethod);
            }
        }
    }

    private static string ToPropertyDefinitionString(IPropertySymbol property)
    {
        var sb = new StringBuilder();

        sb.Append($"public {property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {property.Name} {{");

        if (property.GetMethod is not null)
        {
            sb.Append(" get => default!;");
        }

        if (property.SetMethod is not null)
        {
            sb.Append($" {(property.SetMethod.IsInitOnly ? "init" : "set")} {{ }}");
        }

        sb.Append(" }");

        return sb.ToString();
    }

    internal void GenerateFrontend(ScopingStringBuilder ssb)
    {
        using (var cls = ssb.ScopeBrace($"private class {this.GeneratedClassName} : {this.FullName}")) // {this.AccessibilityName}
        {
            /*ssb.AppendLine("public NetResult Result => this.result;");
            ssb.AppendLine();
            ssb.AppendLine("private NetResult result;");
            ssb.AppendLine();*/

            using (var ctr = ssb.ScopeBrace($"public {this.GeneratedClassName}(ClientConnection clientConnection)"))
            {
                // ssb.AppendLine("this.result = default;");
                ssb.AppendLine("this.ClientConnection = clientConnection;");
            }

            ssb.AppendLine();
            ssb.AppendLine("public ClientConnection ClientConnection { get; }");
            foreach (var x in this.GetMembers(VisceralTarget.Property))
            {
                if (x.symbol is IPropertySymbol propertySymbol)
                {
                    var definitionString = ToPropertyDefinitionString(propertySymbol);
                    ssb.AppendLine(definitionString);
                }
            }

            if (this.ServiceMethods != null)
            {
                foreach (var x in this.ServiceMethods.Values)
                {
                    ssb.AppendLine();
                    this.GenerateFrontend_Method(ssb, x);
                }
            }
        }
    }

    internal void GenerateFrontend_Method(ScopingStringBuilder ssb, ServiceMethod method)
    {
        var returnTypeName = method.GetTaskResultTypeName();
        var genericString = method.TaskResultObject == null ? string.Empty : $"<{returnTypeName}>";
        var taskString = $"Task{genericString}";
        var deserializeString = method.TaskResultObject == null ? "NetResult" : returnTypeName;
        var decrement = method.HasCancellationTokenParameter ? 1 : 0;

        var asyncPrefix = "async ";
        if (method.Kind == ServiceMethod.ServiceMethodKind.UpdateAgreement ||
            method.Kind == ServiceMethod.ServiceMethodKind.ConnectBidirectionally)
        {
            asyncPrefix = string.Empty;
        }

        if (method.ReturnKind == ServiceMethod.PayloadKind.ResponseChannel)
        {
            using (var scopeMethod = ssb.ScopeBrace($"public void {method.SimpleName}({method.GetParameterDeclarations()})"))
            {
                var channelName = $"{NetsphereBody.ArgumentPrefix}{method.ParameterCount}";
                using (var scopeSerialize = ssb.ScopeBrace($"if (!NetHelper.TrySerialize({method.GetParameterNames(NetsphereBody.ArgumentPrefix, decrement)}, out var owner))"))
                {
                    ssb.AppendLine($"(({NetsphereBody.IResponseChannelInternalName}){channelName}).Invoke(NetResult.SerializationFailed);");
                    ssb.AppendLine("return;");
                }

                ssb.AppendLine();

                using (ssb.ScopeBrace("try"))
                {
                    ssb.AppendLine($"((Netsphere.Internal.IClientConnectionInternal)this.ClientConnection).RpcSendAndReceive2(owner, {method.FullIdLiteral}, {channelName});");
                }

                using (ssb.ScopeBrace("finally"))
                {
                    ssb.AppendLine("owner.Return();");
                }
            }

            return;
        }

        using (var scopeMethod = ssb.ScopeBrace($"public {asyncPrefix}{taskString} {method.SimpleName}({method.GetParameterDeclarations()})"))
        {
            if (method.Kind == ServiceMethod.ServiceMethodKind.UpdateAgreement)
            {
                ssb.AppendLine($"return (({NetsphereBody.IClientConnectionInternalFullName})this.ClientConnection).UpdateAgreement({method.FullIdLiteral}, a1);");

                return;
            }
            else if (method.Kind == ServiceMethod.ServiceMethodKind.ConnectBidirectionally)
            {
                ssb.AppendLine($"return (({NetsphereBody.IClientConnectionInternalFullName})this.ClientConnection).ConnectBidirectionally({method.FullIdLiteral}, a1);");
                return;
            }

            if (method.ReturnKind == ServiceMethod.PayloadKind.SendStream)
            {
                if (method.ParameterCount <= 1)
                {
                    ssb.AppendLine($"var response = this.ClientConnection.SendStream(a1, {method.FullIdLiteral});");
                }
                else
                {
                    ssb.AppendLine($"var response = await this.ClientConnection.SendBlockAndStream(({method.GetParameterNames(NetsphereBody.ArgumentPrefix, 1)}), a{method.ParameterCount}, {method.FullIdLiteral}).ConfigureAwait(false);");
                }

                ssb.AppendLine("return response.Stream;");
                return;
            }
            else if (method.ReturnKind == ServiceMethod.PayloadKind.SendStreamAndReceive)
            {
                if (method.ParameterCount <= 1)
                {
                    ssb.AppendLine($"var response = this.ClientConnection.SendStreamAndReceive<{method.ResultTypeArgumentName}>(a1, {method.FullIdLiteral});");
                }
                else
                {
                    ssb.AppendLine($"var response = await this.ClientConnection.SendBlockAndStreamAndReceive<{method.GetParameterTypes(1)}, {method.ResultTypeArgumentName}>(({method.GetParameterNames(NetsphereBody.ArgumentPrefix, 1)}), a{method.ParameterCount}, {method.FullIdLiteral}).ConfigureAwait(false);");
                }

                ssb.AppendLine("return response.Stream;");
                return;
            }

            if (method.ParameterKind == ServiceMethod.PayloadKind.NetResult)
            {
                ssb.AppendLine($"NetHelper.SerializeNetResult(a1, out var owner);");
            }
            else if (method.ParameterKind == ServiceMethod.PayloadKind.ByteArray ||
                method.ParameterKind == ServiceMethod.PayloadKind.Memory ||
                method.ParameterKind == ServiceMethod.PayloadKind.ReadOnlyMemory)
            {// a1(Memory<byte>) -> owner(RentMemory)
                ssb.AppendLine($"var owner = Arc.Collections.BytePool.RentedMemory.CreateFrom(a1);");
            }
            else if (method.ParameterKind == ServiceMethod.PayloadKind.RentedMemory)
            {// a1(RentMemory) -> owner(RentMemory)
                ssb.AppendLine("var owner = a1.IncrementAndShare();");
            }
            else if (method.ParameterKind == ServiceMethod.PayloadKind.RentedReadOnlyMemory)
            {
                ssb.AppendLine("var owner = a1.IncrementAndShare().UnsafeMemory;");
            }
            else if ((method.ParameterCount - decrement) == 0)
            {
                ssb.AppendLine($"var owner = {ServiceMethod.RentedMemoryFullName}.Empty;");
            }
            else
            {
                using (var scopeSerialize = ssb.ScopeBrace($"if (!NetHelper.TrySerialize({method.GetParameterNames(NetsphereBody.ArgumentPrefix, decrement)}, out var owner))"))
                {
                    AppendReturn("NetResult.SerializationFailed");
                }
            }

            ssb.AppendLine();
            var scopeOwner = ssb.ScopeBrace("try");
            if (method.ReturnKind == ServiceMethod.PayloadKind.ReceiveStream)
            {
                var cancellationToken = method.HasCancellationTokenParameter ? $", {NetsphereBody.ArgumentPrefix}{method.ParameterCount}" : string.Empty;
                ssb.AppendLine($"var response = await (({NetsphereBody.IClientConnectionInternalFullName})this.ClientConnection).RpcSendAndReceiveStream(owner, {method.FullIdLiteral}{cancellationToken}).ConfigureAwait(false);");
                ssb.AppendLine("return response.Stream;");
            }
            else
            {
                var cancellationToken = method.HasCancellationTokenParameter ? $", {NetsphereBody.ArgumentPrefix}{method.ParameterCount}" : string.Empty;
                ssb.AppendLine($"var response = await (({NetsphereBody.IClientConnectionInternalFullName})this.ClientConnection).RpcSendAndReceive(owner, {method.FullIdLiteral}{cancellationToken}).ConfigureAwait(false);");
                var transfersResponse = method.ReturnKind == ServiceMethod.PayloadKind.RentedMemory || method.ReturnKind == ServiceMethod.PayloadKind.RentedReadOnlyMemory;
                if (transfersResponse)
                {
                    ssb.AppendLine("var transferResponse = false;");
                }

                var scopeResponse = ssb.ScopeBrace("try");
                var rawResponse = method.ReturnKind == ServiceMethod.PayloadKind.ByteArray ||
                    method.ReturnKind == ServiceMethod.PayloadKind.Memory || method.ReturnKind == ServiceMethod.PayloadKind.ReadOnlyMemory || transfersResponse;
                var emptyResponseCondition = rawResponse ? " && response.DataId != (ulong)NetResult.Success" : string.Empty;
                using (var scopeNoNetService = ssb.ScopeBrace($"if (response.Result == NetResult.Success && response.Value.IsEmpty{emptyResponseCondition})"))
                {
                    AppendReturn("(NetResult)response.DataId");
                }

                using (var scopeNotSuccess = ssb.ScopeBrace("else if (response.Result != NetResult.Success)"))
                {
                    AppendReturn("response.Result");
                }

                ssb.AppendLine();
                if (method.TaskResultObject is null)
                {// Task: the response carries no payload.
                }
                else if (method.ReturnKind == ServiceMethod.PayloadKind.NetResult)
                {
                    ssb.AppendLine("NetHelper.DeserializeNetResult(response.DataId, response.Value.Memory.Span, out var result);");
                }
                else if (method.ReturnKind == ServiceMethod.PayloadKind.NetResultAndValue)
                {
                    using (var scopeDeserialize = ssb.ScopeBrace($"if (!Tinyhand.TinyhandSerializer.TryDeserialize<{method.ResultTypeArgumentName}>(response.Value.Memory.Span, out var result2))"))
                    {
                        AppendReturn("NetResult.DeserializationFailed");
                    }

                    ssb.AppendLine();
                    // ssb.AppendLine($"var result = new {deserializeString}(response.Result, result2);");
                    ssb.AppendLine($"var result = new {deserializeString}((NetResult)response.DataId, result2);");
                }
                else if (method.ReturnKind == ServiceMethod.PayloadKind.ByteArray)
                {
                    ssb.AppendLine("var result = response.Value.Memory.ToArray();");
                }
                else if (method.ReturnKind == ServiceMethod.PayloadKind.Memory ||
                    method.ReturnKind == ServiceMethod.PayloadKind.ReadOnlyMemory)
                {// response.Value(RentMemory) -> result(Memory<byte>)
                    ssb.AppendLine("var result = response.Value.Memory.ToArray().AsMemory();");
                }
                else if (method.ReturnKind == ServiceMethod.PayloadKind.RentedMemory)
                {// response.Value(RentMemory) -> result(RentMemory)
                    ssb.AppendLine("var result = response.Value;");
                    ssb.AppendLine("transferResponse = true;");
                }
                else if (method.ReturnKind == ServiceMethod.PayloadKind.RentedReadOnlyMemory)
                {
                    ssb.AppendLine("var result = response.Value.ReadOnly;");
                    ssb.AppendLine("transferResponse = true;");
                }
                else
                {
                    using (var scopeDeserialize = ssb.ScopeBrace($"if (!Tinyhand.TinyhandSerializer.TryDeserialize<{deserializeString}>(response.Value.Memory.Span, out var result))"))
                    {
                        AppendReturn("NetResult.DeserializationFailed");
                    }

                    ssb.AppendLine();
                }

                if (method.TaskResultObject is not null)
                {
                    ssb.AppendLine($"return result;");
                }

                scopeResponse.Dispose();
                using (ssb.ScopeBrace("finally"))
                {
                    if (transfersResponse)
                    {
                        using (ssb.ScopeBrace("if (!transferResponse)"))
                        {
                            ssb.AppendLine("response.Value.Return();");
                        }
                    }
                    else
                    {
                        ssb.AppendLine("response.Value.Return();");
                    }
                }
            }

            scopeOwner.Dispose();
            using (ssb.ScopeBrace("finally"))
            {
                ssb.AppendLine("owner.Return();");
            }
        }

        void AppendReturn(string netResult)
        {
            if (method.TaskResultObject is null)
            {
                ssb.AppendLine($"return;");
            }
            else
            {
                if (method.ReturnKind == ServiceMethod.PayloadKind.NetResult)
                {
                    ssb.AppendLine($"return {netResult};");
                }
                else if (method.ReturnKind == ServiceMethod.PayloadKind.NetResultAndValue)
                {
                    ssb.AppendLine($"return new({netResult});");
                }
                else
                {
                    ssb.AppendLine($"return default!;");
                }
            }
        }
    }

    internal void GenerateBackend(ScopingStringBuilder ssb)
    {
        using (var cls = ssb.ScopeBrace($"private static class {this.GeneratedClassName}"))
        {
            if (this.ServiceInterfaces != null)
            {
                foreach (var x in this.ServiceInterfaces)
                {
                    this.GenerateBackend_Interface(ssb, x);
                }
            }
        }
    }

    internal void GenerateBackend_Interface(ScopingStringBuilder ssb, NetsphereObject serviceInterface)
    {
        if (serviceInterface.ServiceMethods != null)
        {
            foreach (var x in serviceInterface.ServiceMethods.Values)
            {
                ssb.AppendLine();
                this.GenerateBackend_Method(ssb, serviceInterface, x);
            }
        }

        ssb.AppendLine();
        this.GenerateBackend_AgentInfo(ssb, serviceInterface);
    }

    internal ServiceFilterGroup? GetServiceFilter(NetsphereObject serviceInterface, ServiceMethod method)
    {
        if (this.MethodNameToFilterGroup == null)
        {
            return null;
        }

        var explicitName = this.FullName + "." + serviceInterface.FullName + "." + method.LocalName;
        if (this.MethodNameToFilterGroup.TryGetValue(explicitName, out var serviceFilter))
        {
            return serviceFilter;
        }

        var name = this.FullName + "." + method.LocalName;
        if (this.MethodNameToFilterGroup.TryGetValue(name, out var serviceFilter2))
        {
            return serviceFilter2;
        }

        return null;
    }

    internal void GenerateBackend_Method(ScopingStringBuilder ssb, NetsphereObject serviceInterface, ServiceMethod method)
    {
        var decrement = method.HasCancellationTokenParameter ? 1 : 0;
        using (var scopeMethod = ssb.ScopeBrace($"private static async Task {method.GeneratedMethodName}(object obj, TransmissionContext c0)"))
        {
            var methodFilters = this.GetServiceFilter(serviceInterface, method);
            var filters = ServiceFilterGroup.CombineItems(this.ClassFilterGroup, methodFilters);

            var code = $"Core(({serviceInterface.FullName})obj, c0)";
            var previousAsync = true;
            if (filters != null)
            {
                ServiceFilterGroup.GenerateFilterInstances(ssb, "c0.ServerConnection.GetContext().ServiceProvider", filters);
                ssb.AppendLine();

                code = $"Core(({serviceInterface.FullName})obj, c{filters.Length})";
                for (var i = filters.Length - 1; i >= 0; i--)
                {
                    var n = i + 1;
                    var item = filters[i];
                    var filterType = item.CallContextObject == null ? string.Empty : $"({item.CallContextObject.FullName})";
                    if (item.IsAsync == previousAsync)
                    {
                        code = $"{item.Identifier}.{NetsphereBody.ServiceFilterInvokeName}({filterType}c{i}, c{n} => {code})";
                    }
                    else if (item.IsAsync)
                    {
                        code = $"{item.Identifier}.{NetsphereBody.ServiceFilterInvokeName}({filterType}c{i}, async c{n} => {code})";
                    }
                    else
                    {
                        code = $"{item.Identifier}.{NetsphereBody.ServiceFilterInvokeName}({filterType}c{i}, c{n} => {code}.Wait())";
                    }

                    previousAsync = item.IsAsync;
                }
            }

            if (previousAsync)
            {
                ssb.AppendLine($"await {code}.ConfigureAwait(false);");
            }
            else
            {
                ssb.AppendLine($"{code};");
            }

            ssb.AppendLine();

            using (var scopeCore = ssb.ScopeBrace($"static async Task Core({serviceInterface.FullName} agent, TransmissionContext context)"))
            {
                // ssb.AppendLine("var rent = context.RentMemory;");
                // using (var scopeTry = ssb.ScopeBrace("try"))
                {
                    this.GenerateBackend_MethodCore(ssb, serviceInterface, method, decrement);
                }

                // using (var scopeFinally = ssb.ScopeBrace("finally"))
                // {
                //    ssb.AppendLine("rent.Return();");
                // }
            }
        }
    }

    internal void GenerateBackend_MethodCore(ScopingStringBuilder ssb, NetsphereObject serviceInterface, ServiceMethod method, int decrement)
    {
        if (method.ReturnKind == ServiceMethod.PayloadKind.ResponseChannel)
        {
            using (ssb.ScopeBrace($"if (!NetHelper.Deserialize<{method.GetParameterTypes(decrement)}>(context.RentMemory, out var value))"))
            {
                this.Generate_DeserializationFailed(ssb);
            }

            ssb.AppendLine($"agent.{method.SimpleName}({method.GetTupleItemArguments("value", decrement, method.HasCancellationTokenParameter)});");
            var responseValue = method.ParameterCount == 1 ? "value" : $"value.Item{method.ParameterCount}";
            using (ssb.ScopeBrace($"if (NetHelper.TrySerialize({responseValue}, out var owner2))"))
            {
                this.Generate_ReturnRentMemory(ssb);
                ssb.AppendLine("context.RentMemory = owner2;");
            }

            using (ssb.ScopeBrace("else"))
            {
                this.Generate_ReturnRentMemory(ssb);
                ssb.AppendLine("context.Result = NetResult.SerializationFailed;");
            }

            return;
        }

        if (method.ParameterKind == ServiceMethod.PayloadKind.NetResult)
        {
            using (var scopeDeserialize = ssb.ScopeBrace($"if (!NetHelper.TryDeserializeNetResult(context.RentMemory, out var value))"))
            {
                this.Generate_DeserializationFailed(ssb);
            }
        }
        else if (method.ParameterKind == ServiceMethod.PayloadKind.ByteArray)
        {// context.RentMemory(RentMemory) -> value(byte[])
            ssb.AppendLine("var value = context.RentMemory.Memory.ToArray();");
        }
        else if (method.ParameterKind == ServiceMethod.PayloadKind.Memory ||
            method.ParameterKind == ServiceMethod.PayloadKind.ReadOnlyMemory)
        {// context.RentMemory(RentMemory) -> value(Memory<byte>)
            ssb.AppendLine("var value = context.RentMemory.Memory;");
        }
        else if (method.ParameterKind == ServiceMethod.PayloadKind.RentedMemory)
        {// context.RentMemory(RentMemory) -> value(RentMemory)
            ssb.AppendLine("var value = context.RentMemory;");
        }
        else if (method.ParameterKind == ServiceMethod.PayloadKind.RentedReadOnlyMemory)
        {// BytePool.RentedReadOnlyMemory
            ssb.AppendLine("var value = context.RentMemory.ReadOnly;");
        }
        else if ((method.ParameterCount - decrement) == 0 ||
            method.ReturnKind == ServiceMethod.PayloadKind.SendStream ||
            method.ReturnKind == ServiceMethod.PayloadKind.SendStreamAndReceive)
        {// No request payload to deserialize.
        }
        else
        {
            /*using (var scopeDeserialize = ssb.ScopeBrace($"if (!NetHelper.TryDeserialize<{method.GetParameterTypes(0)}>(context.RentMemory, out var value))"))
            {
                ssb.AppendLine("context.Result = NetResult.DeserializationFailed;");
                ssb.AppendLine("return;");
            }*/

            using (var scopeDeserialize = ssb.ScopeBrace($"if (!NetHelper.Deserialize<{method.GetParameterTypes(decrement)}>(context.RentMemory, out var value))"))
            {
                this.Generate_DeserializationFailed(ssb);
            }

            if (method.TryGetNullCheck("value", decrement, out var condition))
            {
                using (var scopeDeserialize = ssb.ScopeBrace($"if ({condition})"))
                {
                    this.Generate_DeserializationFailed(ssb);
                }
            }
        }

        ssb.AppendLine();

        // Set ServerContext
        /*if (this.NetServiceBase != null)
        {
            if (this.NetServiceBase.Generics_IsGeneric)
            {
                ssb.AppendLine($"(({this.NetServiceBase.FullName})backend).Context = ({this.NetServiceBase.Generics_Arguments[0].FullName})context!;");
            }
            else
            {
                ssb.AppendLine($"(({this.NetServiceBase.FullName})backend).Context = (ServerContext)context!;");
            }
        }*/

        var prefix = string.Empty;
        if (method.TaskResultObject != null)
        {
            prefix = "var result = ";
        }

        if (method.ReturnKind == ServiceMethod.PayloadKind.SendStream ||
            method.ReturnKind == ServiceMethod.PayloadKind.SendStreamAndReceive)
        {
            if (method.ParameterCount > 1)
            {
                ssb.AppendLine($"var rr = await ((IReceiveStreamInternal)context.GetReceiveStream()).ReceiveBlock<{method.GetParameterTypes(1)}>().ConfigureAwait(false);");
                using (var scopeIf = ssb.ScopeBrace("if (rr.IsFailure)"))
                {
                    this.Generate_DeserializationFailed(ssb);
                }

                ssb.AppendLine($"{prefix}await agent.{method.SimpleName}({method.GetTupleItemArguments("rr.Value!", decrement + 1, method.HasCancellationTokenParameter)}, context.GetReceiveStream().MaxStreamLength).ConfigureAwait(false);");
            }
            else
            {
                ssb.AppendLine($"{prefix}await agent.{method.SimpleName}(context.GetReceiveStream().MaxStreamLength).ConfigureAwait(false);");
            }
        }
        else
        {
            ssb.AppendLine($"{prefix}await agent.{method.SimpleName}({method.GetTupleItemArguments("value", decrement, method.HasCancellationTokenParameter)}).ConfigureAwait(false);");
        }

        // ssb.AppendLine("context.Return();"); -> try-finally

        if (method.Kind == ServiceMethod.ServiceMethodKind.UpdateAgreement)
        {
            using (var scopeIf = ssb.ScopeBrace($"if (result == NetResult.Success)"))
            {
                ssb.AppendLine("context.ServerConnection.Agreement.AcceptAll(value.Target);");
                // ssb.AppendLine("context.ServerConnection.ApplyAgreement();");
            }
        }
        else if (method.Kind == ServiceMethod.ServiceMethodKind.ConnectBidirectionally)
        {
            using (var scopeIf = ssb.ScopeBrace($"if (result == NetResult.Success)"))
            {
                ssb.AppendLine("context.ServerConnection.Agreement.EnableBidirectionalConnection = true;");
                using (var scopeIf2 = ssb.ScopeBrace("if (value is not null)"))
                {
                    ssb.AppendLine("context.ServerConnection.Agreement.AcceptAll(value.Target);");
                    // ssb.AppendLine("context.ServerConnection.ApplyAgreement();");
                }
            }
        }

        if (method.TaskResultObject == null)
        {
            this.Generate_ReturnRentMemory(ssb);
            ssb.AppendLine("context.Result = NetResult.Success;");
        }
        else if (method.ReturnKind == ServiceMethod.PayloadKind.NetResult)
        {
            this.Generate_ReturnRentMemory(ssb);
            ssb.AppendLine("context.Result = result;");
            // ssb.AppendLine($"NetHelper.SerializeNetResult(result, out var owner2);");
            // this.Generate_ReturnRentMemory(ssb);
            // ssb.AppendLine("context.RentMemory = owner2;");
        }
        else if (method.ReturnKind == ServiceMethod.PayloadKind.ByteArray ||
            method.ReturnKind == ServiceMethod.PayloadKind.Memory ||
            method.ReturnKind == ServiceMethod.PayloadKind.ReadOnlyMemory)
        {// byte[]/Memory/ReadOnlyMemory
            ssb.AppendLine("context.SetResponseMemory(result);");
        }
        else if (method.ReturnKind == ServiceMethod.PayloadKind.RentedMemory)
        {// BytePool.RentedMemory result; the handler may return the borrowed request lease.
            ssb.AppendLine("context.SetResponseRentMemory(result);");
        }
        else if (method.ReturnKind == ServiceMethod.PayloadKind.RentedReadOnlyMemory)
        {// BytePool.RentedReadOnlyMemory result; the handler may return the borrowed request lease.
            ssb.AppendLine("context.SetResponseRentMemory(result.UnsafeMemory);");
        }
        else if (method.ReturnKind == ServiceMethod.PayloadKind.ReceiveStream)
        {// The handler has consumed the request; an empty result is sent unless the handler opened a stream.
            this.Generate_ReturnRentMemory(ssb);
        }
        else if (method.ReturnKind == ServiceMethod.PayloadKind.SendStream ||
            method.ReturnKind == ServiceMethod.PayloadKind.SendStreamAndReceive)
        {// The request arrives as a stream; the context holds no request lease.
        }
        else if (method.ReturnKind == ServiceMethod.PayloadKind.NetResultAndValue)
        {
            using (var scopeSerialize = ssb.ScopeBrace($"if (NetHelper.TrySerialize(result.Value, out var owner2))"))
            {
                this.Generate_ReturnRentMemory(ssb);
                ssb.AppendLine("context.Result = result.Result;");
                ssb.AppendLine("context.RentMemory = owner2;");
            }

            using (var scopeElse = ssb.ScopeBrace("else"))
            {
                this.Generate_ReturnRentMemory(ssb);
                ssb.AppendLine("context.Result = NetResult.SerializationFailed;");
            }
        }
        else
        {// Other
            using (var scopeSerialize = ssb.ScopeBrace($"if (NetHelper.TrySerialize(result, out var owner2))"))
            {
                this.Generate_ReturnRentMemory(ssb);
                ssb.AppendLine("context.RentMemory = owner2;");
            }

            using (var scopeElse = ssb.ScopeBrace("else"))
            {
                // ssb.AppendLine("context.RentMemory = default;");
                this.Generate_ReturnRentMemory(ssb);
                ssb.AppendLine("context.Result = NetResult.SerializationFailed;");
            }
        }

        // ssb.AppendLine("context.Result = NetResult.Success;");
    }

    internal void Generate_ReturnRentMemory(ScopingStringBuilder ssb)
    {
        ssb.AppendLine("context.RentMemory = context.RentMemory.Return();");
    }

    internal void Generate_DeserializationFailed(ScopingStringBuilder ssb)
    {// Release the request lease; otherwise the request bytes would be sent back as the response payload.
        this.Generate_ReturnRentMemory(ssb);
        ssb.AppendLine("context.Result = NetResult.DeserializationFailed;");
        ssb.AppendLine("return;");
    }

    internal void GenerateBackend_AgentInfo(ScopingStringBuilder ssb, NetsphereObject serviceInterface)
    {
        var serviceIdString = serviceInterface.NetServiceAttribute!.ServiceId.ToString("x");
        using (var scopeMethod = ssb.ScopeBrace($"public static void Object_{serviceIdString}()"))
        {
            var createAgent = this.ObjectFlags.HasFlag(NetsphereObjectFlags.HasDefaultConstructor) ? $"static () => new {this.FullName}()" : "null";
            ssb.AppendLine($"var info = StaticNetService.GetOrAddNetObjectInfo(typeof({this.FullName}), {createAgent});"); // 0x{serviceIdString}u
            if (serviceInterface.ServiceMethods != null)
            {
                foreach (var x in serviceInterface.ServiceMethods.Values)
                {
                    ssb.AppendLine($"info.AddMethod(new ServiceMethod({x.FullIdLiteral}, {x.GeneratedMethodName}));");
                }
            }

            ssb.AppendLine($"StaticNetService.AddNetService<{serviceInterface.FullName}, {this.FullName}>();");
            // var enableByDefault = this.NetObjectAttribute?.EnableByDefault == true ? "true" : "false";
            // ssb.AppendLine($"StaticNetService.AddNetService<{serviceInterface.FullName}, {this.FullName}>({enableByDefault});");
        }
    }

    internal bool IsReturnTypeArgument_NotNullable()
    {
        if (this.symbol is IMethodSymbol ms &&
            ms.ReturnType is INamedTypeSymbol nts)
        {
            if (nts.TypeArguments.Length > 0)
            {
                var ta = nts.TypeArguments[0];
                if (ta.NullableAnnotation == Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated)
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal IMethodSymbol? TryGetMethodSymbol()
        => this.symbol as IMethodSymbol;
}

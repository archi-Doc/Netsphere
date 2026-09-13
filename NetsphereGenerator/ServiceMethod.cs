// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Visceral;
using Microsoft.CodeAnalysis;

namespace Netsphere.Generator;

public class ServiceMethod
{
    public const string ByteArrayName = "byte[]";
    public const string MemoryName = "System.Memory<byte>";
    public const string ReadOnlyMemoryName = "System.ReadOnlyMemory<byte>";
    public const string RentedMemoryFullName = "Arc.Collections.BytePool.RentedMemory";
    public const string RentedReadOnlyMemoryFullName = "Arc.Collections.BytePool.RentedReadOnlyMemory";
    public const string ReceiveStreamName = "Netsphere.ReceiveStream";
    public const string SendStreamName = "Netsphere.SendStream";
    public const string SendStreamAndReceiveName = "Netsphere.SendStreamAndReceive<TReceive>";
    public const string NetResultName = "Netsphere.NetResult";
    public const string NetResultAndValueName = "Netsphere.NetResultAndValue<TValue>";
    public const string ConnectBidirectionallyMethodFullName = "Netsphere.INetServiceWithConnectBidirectionally.ConnectBidirectionally(Netsphere.Crypto.CertificateToken<Netsphere.ConnectionAgreement>)";
    public const string UpdateAgreementMethodFullName = "Netsphere.INetServiceWithUpdateAgreement.UpdateAgreement(Netsphere.Crypto.CertificateToken<Netsphere.ConnectionAgreement>)";
    public const string ResponseChannelPrefix = "ResponseChannel<";
    public const string ResponseChannelFullNamePrefix = "Netsphere.ResponseChannel<";

    public enum PayloadKind
    {
        Other,
        NetResult,
        NetResultAndValue,
        ByteArray,
        Memory,
        ReadOnlyMemory,
        RentedMemory,
        RentedReadOnlyMemory,
        ReceiveStream,
        SendStream,
        SendStreamAndReceive,
        ResponseChannel,
    }

    public enum ServiceMethodKind
    {
        Other,
        UpdateAgreement,
        ConnectBidirectionally,
    }

    public static ServiceMethod? Create(NetsphereObject serviceInterface, NetsphereObject method)
    {
        var returnObject = method.Method_ReturnObject;
        if (returnObject == null)
        {
            return null;
        }

        if (returnObject.FullName == NetsphereBody.TaskFullName)
        {// Task
        }
        else
        {
            var fullName = returnObject.OriginalDefinition?.FullName;
            if (fullName == NetsphereBody.GenericTaskFullName)
            {// Task<TResult>
            }
            else if (fullName is null &&
                method.Method_Parameters.Length > 0 &&
                (method.Method_Parameters[method.Method_Parameters.Length - 1].StartsWith(ServiceMethod.ResponseChannelPrefix) ||
                method.Method_Parameters[method.Method_Parameters.Length - 1].StartsWith(ServiceMethod.ResponseChannelFullNamePrefix)))
            {// void Method(int x, ResponseChannel<TReceive> channel);
            }
            else
            {// Invalid return type
                method.Body.ReportDiagnostic(NetsphereBody.Error_MethodReturnType, method.Location);
            }
        }

        if (method.Body.Abort)
        {
            return null;
        }

        var serviceMethod = new ServiceMethod(method);
        serviceMethod.MethodId = (uint)Arc.Crypto.FarmHash.Hash64(method.FullName);
        if (serviceInterface.NetServiceAttribute == null)
        {
            serviceMethod.FullId = serviceMethod.MethodId;
        }
        else
        {
            serviceMethod.FullId = (ulong)serviceInterface.NetServiceAttribute.ServiceId << 32 | serviceMethod.MethodId;
        }

        if (returnObject.Generics_Arguments.Length > 0)
        {
            serviceMethod.TaskResultObject = returnObject.TypeObjectWithNullable?.Generics_ArgumentsWithNullable[0];
            if (serviceMethod.TaskResultObject?.Object is { } rt)
            {
                if (rt.Kind.IsReferenceType() &&
                method.IsReturnTypeArgument_NotNullable())
                {
                    method.Body.AddDiagnostic(NetsphereBody.Warning_NullableReferenceType, method.Location, rt.LocalName);
                }

                serviceMethod.ReturnKind = NameToPayloadKind(rt.FullName);
                if (serviceMethod.ReturnKind == PayloadKind.Other)
                {
                    serviceMethod.ReturnKind = NameToPayloadKind(rt.OriginalDefinition?.FullName);
                }

                if (serviceMethod.ReturnKind == PayloadKind.NetResultAndValue ||
                    serviceMethod.ReturnKind == PayloadKind.SendStreamAndReceive)
                {
                    serviceMethod.ResultTypeArgumentName = rt.Generics_Arguments[0].FullName;
                }
            }
        }

        if (method.Method_Parameters.Length == 1)
        {
            serviceMethod.ParameterKind = NameToPayloadKind(method.Method_Parameters[0]);
        }

        if (returnObject.FullName == "void" &&
            method.TryGetMethodSymbol() is { } methodSymbol)
        {// void Method(params, ref ResponseChannel<TResponse> channel);
            var parameters = methodSymbol.Parameters;
            if (parameters.Length == 0 ||
                parameters[parameters.Length - 1].RefKind != RefKind.Ref)
            {
                method.Body.AddDiagnostic(NetsphereBody.Error_ResponseChannelMethodForm, method.Location);
                return null;
            }

            serviceMethod.ReturnKind = PayloadKind.ResponseChannel;
            serviceMethod.ParameterKind = PayloadKind.ResponseChannel;
        }

        /*if (serviceMethod.ReturnKind == PayloadKind.SendStream)
        {
            method.Body.AddDiagnostic(NetsphereBody.Error_SendStreamRemoved, method.Location);
            return null;
        }
        else */
        if (serviceMethod.ReturnKind == PayloadKind.SendStream ||
     serviceMethod.ReturnKind == PayloadKind.SendStreamAndReceive)
        {
            if (/*method.Method_Parameters.Length > 1 || */method.Method_Parameters.Length == 0)
            {
                method.Body.AddDiagnostic(NetsphereBody.Error_SendStreamParameter, method.Location);
                return null;
            }
            else if (method.Method_Parameters[method.Method_Parameters.Length - 1] != "long")
            {
                method.Body.AddDiagnostic(NetsphereBody.Error_SendStreamParameter, method.Location);
                return null;
            }
        }

        if (method.FullName == UpdateAgreementMethodFullName)
        {
            serviceMethod.Kind = ServiceMethodKind.UpdateAgreement;
        }
        else if (method.FullName == ConnectBidirectionallyMethodFullName)
        {
            serviceMethod.Kind = ServiceMethodKind.ConnectBidirectionally;
        }

        if (method.Method_Parameters.Length > 0)
        {
            for (var i = 0; i < method.Method_Parameters.Length; i++)
            {
                if (method.Method_Parameters[i] == NetsphereBody.CancellationTokenFullName)
                {
                    if (i == method.Method_Parameters.Length - 1)
                    {
                        serviceMethod.HasCancellationTokenParameter = true;
                    }
                    else
                    {
                        method.Body.AddDiagnostic(NetsphereBody.Error_CancellationTokenPosition, method.Location);
                    }
                }
            }
        }

        return serviceMethod;
    }

    public ServiceMethod(NetsphereObject method)
    {
        this.method = method;
    }

    public Location Location => this.method.Location;

    public string SimpleName => this.method.SimpleName;

    public string LocalName => this.method.LocalName;

    public int ParameterCount => this.method.Method_Parameters.Length;

    public uint MethodId { get; private set; }

    public ulong FullId { get; private set; }

    public string FullIdLiteral => $"0x{this.FullId:x}ul";

    public string GeneratedMethodName => $"Method_{this.FullId:x}";

    public WithNullable<NetsphereObject>? TaskResultObject { get; internal set; }

    public PayloadKind ParameterKind { get; private set; }

    public PayloadKind ReturnKind { get; private set; }

    public string ResultTypeArgumentName { get; private set; } = string.Empty;

    public ServiceMethodKind Kind { get; private set; }

    public bool HasCancellationTokenParameter { get; private set; }

    public int GetParameterCount(int excludedTrailingCount)
        => this.method.Method_Parameters.Length - excludedTrailingCount;

    public string GetTaskResultTypeName()
    {
        if (this.method.TryGetMethodSymbol()?.ReturnType is INamedTypeSymbol { TypeArguments.Length: 1 } task)
        {
            var format = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);
            return task.TypeArguments[0].ToDisplayString(format);
        }

        return this.TaskResultObject?.FullNameWithNullable ?? string.Empty;
    }

    public IEnumerable<string> GetValueTupleTypeArgumentLists(int excludedTrailingCount)
    {
        var parameters = this.method.Method_Parameters;
        var length = parameters.Length - excludedTrailingCount;
        for (var offset = 0; offset < length; offset += 7)
        {
            yield return GetValueTupleTypeArguments(parameters, length, offset);
        }
    }

    public string GetParameterDeclarations()
    {// int a1, string a2
        var methodSymbol = this.method.TryGetMethodSymbol();
        if (methodSymbol is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < this.method.Method_Parameters.Length; i++)
        {
            if (i != 0)
            {
                sb.Append(", ");
            }

            if (methodSymbol.Parameters[i].RefKind != RefKind.None)
            {
                sb.Append(VisceralHelper.RefKindToStringWithSpace(methodSymbol.Parameters[i].RefKind));
            }

            sb.Append(this.method.Method_Parameters[i]);
            sb.Append(" ");
            sb.Append(NetsphereBody.ArgumentPrefix);
            sb.Append(i + 1);
        }

        return sb.ToString();
    }

    public string GetParameterNames(string prefix, int excludedTrailingCount)
    {// string.Empty, a1, (a1, a2)
        var parameters = this.method.Method_Parameters;
        var length = parameters.Length - excludedTrailingCount;
        if (length <= 0)
        {
            return string.Empty;
        }
        else if (length == 1)
        {
            return prefix + "1";
        }
        else
        {
            var sb = new StringBuilder();
            sb.Append("(");
            for (var i = 0; i < length; i++)
            {
                if (i != 0)
                {
                    sb.Append(", ");
                }

                sb.Append(prefix);
                sb.Append(i + 1);
            }

            sb.Append(")");
            return sb.ToString();
        }
    }

    public bool TryGetNullCheck(string valueName, int excludedTrailingCount, out string condition)
    {
        condition = string.Empty;
        var methodSymbol = this.method.TryGetMethodSymbol();
        if (methodSymbol == null)
        {
            return false;
        }

        var parameters = methodSymbol.Parameters;
        var length = parameters.Length - excludedTrailingCount;
        if (length == 0)
        {
            return false;
        }
        else if (length == 1)
        {
            if (parameters[0].Type.IsReferenceType && parameters[0].Type.NullableAnnotation == Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated)
            {
                condition = $"{valueName} is null";
                return true;
            }
            else
            {
                return false;
            }
        }
        else
        {
            StringBuilder? sb = default;
            for (var i = 0; i < length; i++)
            {
                if (parameters[i].Type.IsReferenceType && parameters[i].Type.NullableAnnotation == Microsoft.CodeAnalysis.NullableAnnotation.NotAnnotated)
                {
                    if (sb == null)
                    {
                        sb = new StringBuilder();
                        sb.Append($"{valueName}.Item{i + 1} is null");
                    }
                    else
                    {
                        sb.Append($" || {valueName}.Item{i + 1} is null");
                    }
                }
            }

            if (sb != null)
            {
                condition = sb.ToString();
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    public string GetParameterTypes(int excludedTrailingCount)
    {// (int, string)
        var parameters = this.method.Method_Parameters;
        var length = parameters.Length - excludedTrailingCount;

        if (length <= 0)
        {
            return string.Empty;
        }
        else if (length == 1)
        {
            return parameters[0];
        }
        else
        {
            var sb = new StringBuilder();
            sb.Append("(");
            for (var i = 0; i < length; i++)
            {
                if (i != 0)
                {
                    sb.Append(", ");
                }

                sb.Append(parameters[i]);
            }

            sb.Append(")");
            return sb.ToString();
        }
    }

    public string GetTupleItemArguments(string tupleName, int excludedTrailingCount, bool hasCancellationTokenParameter)
    {// value, value.Item1, value.Item2
        var methodSymbol = this.method.TryGetMethodSymbol();
        if (methodSymbol is null)
        {
            return string.Empty;
        }

        var parameters = this.method.Method_Parameters;
        var length = parameters.Length - excludedTrailingCount;

        if (length <= 0)
        {
            if (hasCancellationTokenParameter)
            {
                return "default";
            }
            else
            {
                return string.Empty;
            }
        }
        else if (length == 1)
        {
            var prefix = VisceralHelper.RefKindToStringWithSpace(methodSymbol.Parameters[0].RefKind);
            if (hasCancellationTokenParameter)
            {
                return $"{prefix}{tupleName}, default";
            }
            else
            {
                return prefix + tupleName;
            }
        }
        else
        {
            var sb = new StringBuilder();
            for (var i = 0; i < length; i++)
            {
                if (i != 0)
                {
                    sb.Append(", ");
                }

                if (methodSymbol.Parameters[i].RefKind != RefKind.None)
                {
                    sb.Append(VisceralHelper.RefKindToStringWithSpace(methodSymbol.Parameters[i].RefKind));
                }

                sb.Append(tupleName);
                sb.Append(".Item");
                sb.Append(i + 1);
            }

            if (hasCancellationTokenParameter)
            {
                sb.Append(", default");
            }

            return sb.ToString();
        }
    }

    private static string GetValueTupleTypeArguments(IReadOnlyList<string> parameters, int length, int offset)
    {
        var numberOfItems = Math.Min(7, length - offset);
        var sb = new StringBuilder();
        for (var i = 0; i < numberOfItems; i++)
        {
            if (i != 0)
            {
                sb.Append(", ");
            }

            sb.Append(parameters[offset + i]);
        }

        if ((length - offset) > 7)
        {
            sb.Append(", System.ValueTuple<");
            sb.Append(GetValueTupleTypeArguments(parameters, length, offset + 7));
            sb.Append('>');
        }

        return sb.ToString();
    }

    private static PayloadKind NameToPayloadKind(string? name)
    {
        var result = name switch
        {
            NetResultName => PayloadKind.NetResult,
            NetResultAndValueName => PayloadKind.NetResultAndValue,
            ByteArrayName => PayloadKind.ByteArray,
            MemoryName => PayloadKind.Memory,
            ReadOnlyMemoryName => PayloadKind.ReadOnlyMemory,
            RentedMemoryFullName => PayloadKind.RentedMemory,
            RentedReadOnlyMemoryFullName => PayloadKind.RentedReadOnlyMemory,
            ReceiveStreamName => PayloadKind.ReceiveStream,
            SendStreamName => PayloadKind.SendStream,
            SendStreamAndReceiveName => PayloadKind.SendStreamAndReceive,
            _ => PayloadKind.Other,
        };

        if (name is not null &&
            result == PayloadKind.Other)
        {
            if (name.StartsWith(ResponseChannelPrefix) ||
                name.StartsWith(ResponseChannelFullNamePrefix))
            {
                result = PayloadKind.ResponseChannel;
            }
        }

        return result;
    }

    private NetsphereObject method;
}

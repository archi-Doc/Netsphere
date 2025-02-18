// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Visceral;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

#pragma warning disable RS2008
#pragma warning disable SA1310 // Field names should not contain underscore
#pragma warning disable SA1401 // Fields should be private
#pragma warning disable SA1117 // Parameters should be on same line or separate lines

namespace Netsphere.Generator;

public class NetsphereBody : VisceralBody<NetsphereObject>
{
    public const string TaskFullName = "System.Threading.Tasks.Task";
    public const string TaskFullName2 = "System.Threading.Tasks.Task<TResult>";
    public const string NetTaskFullName = "Netsphere.NetTask";
    public const string NetTaskFullName2 = "Netsphere.NetTask<TResponse>";
    public const string GeneratorName = "NetsphereGenerator";
    public const string FrontendClassName = "Frontend_"; // "__gen_frontend__";
    public const string BackendClassName = "Backend_";
    public const string ArgumentName = "a";
    public const string NetResultFullName = "Netsphere.NetResult";
    // public const string NetServiceBaseFullName = "Netsphere.NetServiceBase";
    // public const string NetServiceBaseFullName2 = "Netsphere.NetServiceBase<TServerContext>";
    public const string ServiceFilterSyncFullName = "Netsphere.IServiceFilterSync";
    public const string ServiceFilterSyncFullName2 = "Netsphere.IServiceFilterSync<TCallContext>";
    public const string ServiceFilterAsyncFullName = "Netsphere.IServiceFilter";
    public const string ServiceFilterAsyncFullName2 = "Netsphere.IServiceFilter<TCallContext>";
    public const string ServiceFilterBaseName = "IServiceFilterBase";
    public const string ServiceFilterInvokeName = "Invoke";
    public const string ServiceFilterSetArgumentsName = "SetArguments";
    public const string IClientConnectionInternalName = "Netsphere.Internal.IClientConnectionInternal";

    public static readonly DiagnosticDescriptor Error_AttributePropertyError = new DiagnosticDescriptor(
        id: "NSG001", title: "Attribute property type error", messageFormat: "The argument specified does not match the type of the property",
        category: GeneratorName, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Error_KeywordUsed = new DiagnosticDescriptor(
        id: "NSG002", title: "Keyword used", messageFormat: "The type '{0}' already contains a definition for '{1}'",
        category: GeneratorName, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Error_GenericType = new DiagnosticDescriptor(
        id: "NSG003", title: "Generic type", messageFormat: "Generic type is not supported",
        category: GeneratorName, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Error_INetService = new DiagnosticDescriptor(
        id: "NSG004", title: "INetService", messageFormat: "NetServiceObject or NetServiceInterface must be derived from INetService",
        category: GeneratorName, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Error_Accessibility = new DiagnosticDescriptor(
        id: "NSG005", title: "Accessibility", messageFormat: "Access modifier of NetServiceObject must be public or internal",
        category: GeneratorName, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Error_DuplicateServiceId = new DiagnosticDescriptor(
        id: "NSG006", title: "Duplicate Service Id", messageFormat: "Duplicate Service Id {0} is found",
        category: GeneratorName, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Error_DuplicateServiceObject = new DiagnosticDescriptor(
        id: "NSG007", title: "Duplicate Service Object", messageFormat: "Multiple service objects implement service interface {0}",
        category: GeneratorName, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Error_MethodReturnType = new DiagnosticDescriptor(
        id: "NSG008", title: "Method return type", messageFormat: "The return type of service method must be NetTask or NetTask<T> or Task or Task<TResult>",
        category: GeneratorName, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Error_DuplicateServiceMethod = new DiagnosticDescriptor(
        id: "NSG009", title: "Duplicate Service Method", messageFormat: "Duplicate Service Method {0} is found",
        category: GeneratorName, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Warning_NullableReferenceType = new DiagnosticDescriptor(
        id: "NSG010", title: "Nullable not annotated", messageFormat: "The return type should be nullable '{0}?' for the reference type",
        category: GeneratorName, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Error_NoFilterType = new DiagnosticDescriptor(
        id: "NSG011", title: "No FilterType", messageFormat: "Could not get the filtertype from the specified string",
        category: GeneratorName, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Error_FilterTypeConflicted = new DiagnosticDescriptor(
        id: "NSG012", title: "FilterType conflict", messageFormat: "Service filters of the same type has been detected",
        category: GeneratorName, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Error_FilterTypeNotDerived = new DiagnosticDescriptor(
        id: "NSG013", title: "FilterType not derived", messageFormat: "Service filter must implement 'IServiceFilter' or 'IServiceFilterAsync'",
        category: GeneratorName, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor Error_SendStreamParam = new DiagnosticDescriptor(
        id: "NSG014", title: "SendStream param", messageFormat: "Method that returns SendStream type must be declared as 'Method(long maxLength)''",
        category: GeneratorName, DiagnosticSeverity.Error, isEnabledByDefault: true);

    public NetsphereBody(SourceProductionContext context)
        : base(context)
    {
    }

    internal Dictionary<uint, NetsphereObject> IdToNetInterface = new();

    internal List<NetsphereObject> NetObjects = new();

    internal Dictionary<uint, NetsphereObject> IdToNetObject = new();

    internal Dictionary<string, List<NetsphereObject>> Namespaces = new();

    public void Prepare()
    {
        // Configure objects.
        var array = this.FullNameToObject.Values.ToArray();
        foreach (var x in array)
        {
            x.Configure();
        }

        this.FlushDiagnostic();
        if (this.Abort)
        {
            return;
        }

        array = this.IdToNetInterface.Values.Concat(this.NetObjects).ToArray();
        foreach (var x in array)
        {
            x.ConfigureRelation();
        }

        // Check
        foreach (var x in array)
        {
            x.Check();
        }

        this.FlushDiagnostic();
        if (this.Abort)
        {
            return;
        }
    }

    public void Generate(IGeneratorInformation generator, CancellationToken cancellationToken)
    {
        var assemblyId = string.Empty; // Assembly ID
        if (!string.IsNullOrEmpty(generator.AssemblyName))
        {
            assemblyId = VisceralHelper.AssemblyNameToIdentifier("_" + generator.AssemblyName);
        }

        this.GenerateFrontend(generator, assemblyId);
        this.GenerateBackend(generator, assemblyId);
    }

    public void GenerateFrontend(IGeneratorInformation generator, string assemblyId)
    {
        ScopingStringBuilder ssb = new();
        GeneratorInformation info = new();

        var array = this.IdToNetInterface.Values.ToArray();

        this.GenerateHeader(ssb);
        ssb.AppendLine($"namespace Netsphere.Generated;");
        ssb.AppendLine();

        using (var scopeClass = ssb.ScopeBrace("internal static class Frontend" + assemblyId))
        {
            ssb.AppendLine("private static bool Initialized;");
            ssb.AppendLine();
            ssb.AppendLine("[ModuleInitializer]");

            using (var scopeMethod = ssb.ScopeBrace("public static void Initialize()"))
            {
                ssb.AppendLine("if (Initialized) return;");
                ssb.AppendLine("Initialized = true;");
                ssb.AppendLine();

                foreach (var y in array.Where(a => a.ObjectFlag.HasFlag(NetsphereObjectFlag.NetServiceInterface)))
                {
                    ssb.AppendLine($"StaticNetService.SetFrontendDelegate<{y.FullName}>(static x => new {y.ClassName}(x));");
                }
            }

            foreach (var y in array)
            {
                if (y.ObjectFlag.HasFlag(NetsphereObjectFlag.NetServiceInterface))
                {// NetServiceInterface (Frontend)
                    ssb.AppendLine();
                    y.GenerateFrontend(ssb, info);
                }
            }

            var result = ssb.Finalize();
            if (generator.GenerateToFile && generator.TargetFolder != null && Directory.Exists(generator.TargetFolder))
            {
                this.StringToFile(result, Path.Combine(generator.TargetFolder, $"gen.Netsphere.Frontend.cs"));
            }
            else
            {
                this.Context?.AddSource($"gen.Netsphere.Frontend", SourceText.From(result, Encoding.UTF8));
                this.Context2?.AddSource($"gen.Netsphere.Frontend", SourceText.From(result, Encoding.UTF8));
            }
        }

        this.FlushDiagnostic();
    }

    public void GenerateBackend(IGeneratorInformation generator, string assemblyId)
    {
        ScopingStringBuilder ssb = new();
        GeneratorInformation info = new();

        var array = this.NetObjects.ToArray();

        this.GenerateHeader(ssb);
        ssb.AppendLine($"namespace Netsphere.Generated;");
        ssb.AppendLine();

        using (var scopeClass = ssb.ScopeBrace("internal static class Backend" + assemblyId))
        {
            ssb.AppendLine("private static bool Initialized;");
            ssb.AppendLine();
            ssb.AppendLine("[ModuleInitializer]");

            using (var scopeMethod = ssb.ScopeBrace("public static void Initialize()"))
            {
                ssb.AppendLine("if (Initialized) return;");
                ssb.AppendLine("Initialized = true;");
                ssb.AppendLine();

                foreach (var y in array.Where(a => a.ObjectFlag.HasFlag(NetsphereObjectFlag.NetServiceObject)))
                {
                    if (y.ServiceInterfaces != null)
                    {
                        foreach (var z in y.ServiceInterfaces)
                        {
                            // ssb.AppendLine($"StaticNetService.TryAddAgentInfo({y.ClassName}.Agent_{z.NetServiceInterfaceAttribute!.ServiceId.ToString("x")}());");
                            ssb.AppendLine($"{y.ClassName}.Agent_{z.NetServiceInterfaceAttribute!.ServiceId.ToString("x")}();");
                        }
                    }
                }
            }

            foreach (var y in array)
            {
                if (y.ObjectFlag.HasFlag(NetsphereObjectFlag.NetServiceObject))
                {// NetServiceObject (Backend)
                    ssb.AppendLine();
                    y.GenerateBackend(ssb, info);
                }
            }

            var result = ssb.Finalize();
            if (generator.GenerateToFile && generator.TargetFolder != null && Directory.Exists(generator.TargetFolder))
            {
                this.StringToFile(result, Path.Combine(generator.TargetFolder, $"gen.Netsphere.Backend.cs"));
            }
            else
            {
                this.Context?.AddSource($"gen.Netsphere.Backend", SourceText.From(result, Encoding.UTF8));
                this.Context2?.AddSource($"gen.Netsphere.Backend", SourceText.From(result, Encoding.UTF8));
            }
        }

        this.FlushDiagnostic();
    }

    /*public void Generate(IGeneratorInformation generator, CancellationToken cancellationToken)
    {
        ScopingStringBuilder ssb = new();
        GeneratorInformation info = new();
        List<NetsphereObject> rootObjects = new();

        // Namespace
        var assemblyId = string.Empty; // Assembly ID
        if (!string.IsNullOrEmpty(generator.AssemblyName))
        {
            assemblyId = VisceralHelper.AssemblyNameToIdentifier("_" + generator.AssemblyName);
        }

        // Namespace
        foreach (var x in this.Namespaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.GenerateHeader(ssb);
            ssb.AppendLine($"namespace {x.Key};");
            ssb.AppendLine();

            rootObjects.AddRange(x.Value); // For loader generation

            using (var scopeClass = ssb.ScopeBrace("internal static class NetsphereModule" + assemblyId))
            {
                ssb.AppendLine("private static bool Initialized;");
                ssb.AppendLine();
                ssb.AppendLine("[ModuleInitializer]");

                using (var scopeMethod = ssb.ScopeBrace("public static void Initialize()"))
                {
                    ssb.AppendLine("if (Initialized) return;");
                    ssb.AppendLine("Initialized = true;");
                    ssb.AppendLine();

                    foreach (var y in x.Value.Where(a => a.ObjectFlag.HasFlag(NetsphereObjectFlag.NetServiceInterface)))
                    {
                        ssb.AppendLine($"StaticNetService.SetFrontendDelegate<{y.FullName}>(static x => new {y.ClassName}(x));");
                    }
                }

                foreach (var y in x.Value)
                {
                    ssb.AppendLine();
                    y.Generate(ssb, info); // Primary objects
                }
            }

            var result = ssb.Finalize();

            if (generator.GenerateToFile && generator.TargetFolder != null && Directory.Exists(generator.TargetFolder))
            {
                this.StringToFile(result, Path.Combine(generator.TargetFolder, $"gen.Netsphere.{x.Key}.cs"));
            }
            else
            {
                this.Context?.AddSource($"gen.Netsphere.{x.Key}", SourceText.From(result, Encoding.UTF8));
                this.Context2?.AddSource($"gen.Netsphere.{x.Key}", SourceText.From(result, Encoding.UTF8));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        // this.GenerateLoader(generator, info, rootObjects);

        this.FlushDiagnostic();
    }*/

    private void GenerateHeader(ScopingStringBuilder ssb)
    {
        ssb.AddHeader("// <auto-generated/>");
        ssb.AddUsing("System");
        ssb.AddUsing("System.Collections.Generic");
        ssb.AddUsing("System.Diagnostics.CodeAnalysis");
        ssb.AddUsing("System.Runtime.CompilerServices");
        ssb.AddUsing("Arc.Collections");
        ssb.AddUsing("Netsphere");

        ssb.AppendLine("#nullable enable", false);
        ssb.AppendLine("#pragma warning disable CS1591", false);
        ssb.AppendLine("#pragma warning disable CS1998", false);
        ssb.AppendLine();
    }

    private void GenerateInitializer(IGeneratorInformation generator, ScopingStringBuilder ssb, GeneratorInformation info)
    {
        // Namespace
        var ns = "Netsphere";
        var assemblyId = string.Empty; // Assembly ID
        if (!string.IsNullOrEmpty(generator.CustomNamespace))
        {// Custom namespace.
            ns = generator.CustomNamespace;
        }
        else
        {// Other (Apps)
         // assemblyId = "_" + generator.AssemblyId.ToString("x");
            if (!string.IsNullOrEmpty(generator.AssemblyName))
            {
                assemblyId = VisceralHelper.AssemblyNameToIdentifier("_" + generator.AssemblyName);
            }
        }

        info.ModuleInitializerClass.Add("Netsphere.Generator.Generated");

        ssb.AppendLine();
        using (var scopeCrossLink = ssb.ScopeNamespace(ns!))
        using (var scopeClass = ssb.ScopeBrace("public static class NetsphereModule" + assemblyId))
        {
            ssb.AppendLine("private static bool Initialized;");
            ssb.AppendLine();
            ssb.AppendLine("[ModuleInitializer]");

            using (var scopeMethod = ssb.ScopeBrace("public static void Initialize()"))
            {
                ssb.AppendLine("if (Initialized) return;");
                ssb.AppendLine("Initialized = true;");
                ssb.AppendLine();

                foreach (var x in info.ModuleInitializerClass)
                {
                    ssb.Append(x, true);
                    ssb.AppendLine(".RegisterBM();", false);
                }
            }
        }
    }
}

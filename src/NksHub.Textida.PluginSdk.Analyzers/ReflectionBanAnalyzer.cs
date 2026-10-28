using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace NksHub.Textida.PluginSdk.Analyzers;

/// <summary>
/// A plugin is published with NativeAOT and trimmed, so code that finds members, types or constructors by name at run time works in a debug build and fails in the release one.
/// This analyzer turns that into a compile error (<c>NKSPLG001</c>): reflection over members, <c>Activator</c>, loading assemblies, <c>dynamic</c>, compiling expression trees and the
/// <c>JsonSerializer</c> overloads that take no source-generated type information. Reading the resources and the name of the own assembly stays allowed.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReflectionBanAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "NKSPLG001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Reflection cannot be used in a NativeAOT plugin",
        "'{0}' uses reflection, which a NativeAOT plugin cannot rely on; use source-generated code instead",
        "NativeAot",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Plugins are published with NativeAOT and trimmed. Members, types and constructors found by name at run time are removed or unreachable there.",
        customTags: WellKnownDiagnosticTags.NotConfigurable);

    /// <summary>Members of types outside System.Reflection that look things up by name or call them through a delegate or a handle. A null set bans the whole type.</summary>
    private static readonly Dictionary<string, HashSet<string>?> BannedMembers = new(StringComparer.Ordinal)
    {
        ["System.Activator"] = null,
        ["System.Runtime.Loader.AssemblyLoadContext"] = null,
        ["System.Type"] = Names("GetMethod", "GetMethods", "GetProperty", "GetProperties", "GetField", "GetFields", "GetMember", "GetMembers", "GetConstructor", "GetConstructors",
            "GetEvent", "GetEvents", "GetNestedType", "GetNestedTypes", "GetInterface", "GetInterfaces", "GetDefaultMembers", "FindMembers", "InvokeMember", "MakeGenericType", "GetType"),
        ["System.Delegate"] = Names("DynamicInvoke", "CreateDelegate"),
        ["System.Attribute"] = Names("GetCustomAttribute", "GetCustomAttributes", "IsDefined"),
        ["System.AppDomain"] = Names("GetAssemblies", "Load", "CreateInstance", "CreateInstanceFrom", "CreateInstanceAndUnwrap"),
        ["System.Linq.Expressions.LambdaExpression"] = Names("Compile"),
        ["System.Linq.Expressions.Expression"] = Names("Compile"),
    };

    /// <summary>The only members of System.Reflection a plugin may use: its own resources, the identity of its own assembly, and the name of a type (<c>Type.Name</c> is declared on <c>MemberInfo</c>).</summary>
    private static readonly HashSet<string> AllowedReflection = Names(
        "Assembly.GetManifestResourceStream", "Assembly.GetManifestResourceNames", "Assembly.GetName", "Assembly.GetExecutingAssembly", "Assembly.GetEntryAssembly", "Assembly.FullName",
        "MemberInfo.Name");

    private static HashSet<string> Names(params string[] names) => new(names, StringComparer.Ordinal);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterOperationAction(c => Check(c, ((IInvocationOperation)c.Operation).TargetMethod), OperationKind.Invocation);
        context.RegisterOperationAction(c => { if (((IObjectCreationOperation)c.Operation).Constructor is { } ctor) Check(c, ctor); }, OperationKind.ObjectCreation);
        context.RegisterOperationAction(c => Check(c, ((IPropertyReferenceOperation)c.Operation).Property), OperationKind.PropertyReference);
        context.RegisterOperationAction(c => Check(c, ((IMethodReferenceOperation)c.Operation).Method), OperationKind.MethodReference);
        // a call on a dynamic value holds a member reference as well; the call is reported once
        context.RegisterOperationAction(c => { if (c.Operation.Parent is not IDynamicInvocationOperation) Report(c, "dynamic"); },
            OperationKind.DynamicInvocation, OperationKind.DynamicMemberReference, OperationKind.DynamicObjectCreation, OperationKind.DynamicIndexerAccess);
    }

    private static void Check(OperationAnalysisContext context, ISymbol member)
    {
        if (member.ContainingType is not { } type) return;
        var typeName = (type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() + "." : "") + type.Name;
        if (IsBanned(typeName, type.Name, member)) Report(context, type.Name + "." + (member.Name == ".ctor" ? "new" : member.Name));
    }

    private static bool IsBanned(string typeName, string simpleName, ISymbol member)
    {
        if (typeName == "System.Reflection.AssemblyName") return false;
        if (typeName.StartsWith("System.Reflection.", StringComparison.Ordinal)) return !AllowedReflection.Contains(simpleName + "." + member.Name);
        if (typeName == "System.Text.Json.JsonSerializer") return member is IMethodSymbol m && !m.Parameters.Any(IsSourceGeneratedInfo);
        return BannedMembers.TryGetValue(typeName, out var names) && (names is null || names.Contains(member.Name));
    }

    /// <summary>A parameter that carries source-generated metadata: <c>JsonTypeInfo</c>, <c>JsonTypeInfo&lt;T&gt;</c> or a <c>JsonSerializerContext</c>.</summary>
    private static bool IsSourceGeneratedInfo(IParameterSymbol parameter)
    {
        for (var type = parameter.Type as INamedTypeSymbol; type is not null; type = type.BaseType)
            if (type.Name is "JsonTypeInfo" or "JsonSerializerContext") return true;
        return false;
    }

    private static void Report(OperationAnalysisContext context, string what) =>
        context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation(), what));
}

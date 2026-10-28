using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NksHub.Textida.PluginSdk.Analyzers;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P3b.5.1: the analyzer that fails the build of a plugin which uses reflection, and so could not run as a NativeAOT program.</summary>
public class ReflectionBanAnalyzerTests
{
    static readonly Lazy<MetadataReference[]> References = new(() =>
        [.. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))]);

    const string Usings = "using System; using System.Reflection; using System.Text.Json; using System.Text.Json.Serialization.Metadata;\n";

    static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string code, string fileName = "Plugin.cs")
    {
        var tree = CSharpSyntaxTree.ParseText(Usings + code, path: fileName);
        var compilation = CSharpCompilation.Create("plugin", [tree], References.Value, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        // the snippets must be good C#, so a finding comes from the analyzer and not from a typo
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        return await compilation.WithAnalyzers([new ReflectionBanAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    }

    [Theory]
    [InlineData("class C { object? M() => typeof(C).GetMethod(\"M\"); }", "Type.GetMethod")]
    [InlineData("class C { object? M(object o) => o.GetType().GetProperties(); }", "Type.GetProperties")]
    [InlineData("class C { object? M() => Type.GetType(\"System.String\"); }", "Type.GetType")]
    [InlineData("class C { object? M() => Activator.CreateInstance(typeof(C)); }", "Activator.CreateInstance")]
    [InlineData("class C { object? M() => Assembly.GetExecutingAssembly().GetTypes(); }", "Assembly.GetTypes")]
    [InlineData("class C { object? M(MethodInfo method) => method.Invoke(null, null); }", ".Invoke")]
    [InlineData("class C { object? M(PropertyInfo property, object o) => property.GetValue(o); }", ".GetValue")]
    [InlineData("class C { object? M(Delegate d) => d.DynamicInvoke(); }", "Delegate.DynamicInvoke")]
    [InlineData("class C { object? M() => AppDomain.CurrentDomain.GetAssemblies(); }", "AppDomain.GetAssemblies")]
    [InlineData("class C { object? M() => Assembly.Load(\"x\"); }", "Assembly.Load")]
    [InlineData("class C { object M() => System.Linq.Expressions.Expression.Lambda<Func<int>>(System.Linq.Expressions.Expression.Constant(1)).Compile(); }", ".Compile")]
    [InlineData("class C { object? M(dynamic d) => d.Foo(); }", "dynamic")]
    [InlineData("class C { string M() => JsonSerializer.Serialize(1); }", "JsonSerializer.Serialize")]
    [InlineData("class C { int? M(string json) => JsonSerializer.Deserialize<int>(json, new JsonSerializerOptions()); }", "JsonSerializer.Deserialize")]
    public async Task Reflection_is_an_error_that_names_what_was_used(string code, string used)
    {
        var findings = await AnalyzeAsync(code);

        var finding = Assert.Single(findings);
        Assert.Equal(("NKSPLG001", DiagnosticSeverity.Error), (finding.Id, finding.Severity));
        Assert.Contains(used, finding.GetMessage());
    }

    [Theory]
    [InlineData("class C { System.IO.Stream? M() => typeof(C).Assembly.GetManifestResourceStream(\"x\"); }")]
    [InlineData("class C { string? M() => Assembly.GetExecutingAssembly().GetName().Name; }")]
    [InlineData("class C { string M(object o) => o.GetType().Name + typeof(C).FullName; }")]
    [InlineData("class C { string M(JsonTypeInfo<int> info) => JsonSerializer.Serialize(1, info); }")]
    [InlineData("class C { int M(JsonElement element, JsonTypeInfo<int> info) => element.Deserialize(info); }")]
    [InlineData("class C { bool M(Type type) => type.IsAssignableFrom(typeof(C)); }")]
    public async Task Code_that_needs_no_reflection_is_left_alone(string code) => Assert.Empty(await AnalyzeAsync(code));

    [Fact]
    public async Task Every_use_in_a_file_is_reported_at_its_place()
    {
        var findings = await AnalyzeAsync("class C\n{\n    object? A() => typeof(C).GetMethod(\"A\");\n    object? B() => typeof(C).GetField(\"B\");\n}\n");

        Assert.Equal([3, 4], findings.Select(f => f.Location.GetLineSpan().StartLinePosition.Line).Order());
    }
}

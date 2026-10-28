using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NksHub.Textida.Plugin.Testing;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;

namespace NksHub.Textida.Plugin.Tests;

/// <summary>P8b.2.1: every example in the documentation of the SDK is built and run against the current API.</summary>
public partial class DocumentationTests
{
    private const string ImplicitUsings = "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;";

    [GeneratedRegex("```csharp\\r?\\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex CSharpBlock();

    private static readonly string[] Documents =
    [
        Path.Combine("src", "NksHub.Textida.Plugin.Sdk", "README.md"),
        Path.Combine("src", "NksHub.Textida.Plugin.TestHost", "README.md"),
    ];

    /// <summary>Builds the C# examples of the documents of the SDK packages into one assembly; they refer to each other.</summary>
    private static Assembly Compile()
    {
        var blocks = Documents.SelectMany(file => CSharpBlock().Matches(File.ReadAllText(Path.Combine(T.RepoRoot(), file))).Select(m => m.Groups[1].Value)).ToList();
        Assert.Equal(2, blocks.Count);
        var trees = new[] { ImplicitUsings }.Concat(blocks).Select(code => CSharpSyntaxTree.ParseText(code)).ToList();
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("docs-" + Guid.NewGuid().ToString("N"), trees, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)));
        return Assembly.Load(stream.ToArray());
    }

    [Fact]
    public async Task The_example_of_the_sdk_readme_compiles_and_a_command_of_it_runs_on_the_test_host()
    {
        var assembly = Compile();
        var plugin = (PluginBase)Activator.CreateInstance(assembly.GetType("Docs.CountPlugin", throwOnError: true)!)!;
        await using var editor = await TestEditor.StartAsync(plugin, new TestEditorOptions { Grants = [DocumentPermissions.Read] });
        var document = editor.OpenDocument("a.txt", "héllo\n");

        var reply = await editor.ExecuteCommandAsync("example.count.run", document);

        Assert.Equal("counted", reply.GetProperty("status").GetString());
        Assert.Equal([(NoticeLevel.Info, "6 characters")], editor.Ui.Notices);
    }

    [Fact]
    public async Task The_example_of_the_test_host_readme_compiles_and_runs()
    {
        var assembly = Compile();
        var run = assembly.GetType("Docs.Tests.CountExample", throwOnError: true)!.GetMethod("RunAsync")!;

        var notice = await (Task<string>)run.Invoke(null, null)!;

        Assert.Equal("12 characters", notice);
    }

    [Fact]
    public async Task The_example_of_the_readme_needs_the_permission_the_readme_says_it_needs()
    {
        var assembly = Compile();
        var plugin = (PluginBase)Activator.CreateInstance(assembly.GetType("Docs.CountPlugin", throwOnError: true)!)!;
        await using var editor = await TestEditor.StartAsync(plugin);
        var document = editor.OpenDocument("a.txt", "abc");

        var error = await Assert.ThrowsAsync<Rpc.RpcException>(() => editor.ExecuteCommandAsync("example.count.run", document));

        Assert.Equal((DocumentErrorCodes.PermissionDenied, DocumentPermissions.Read), (error.Code, T.Data(error, "permission")));
    }
}

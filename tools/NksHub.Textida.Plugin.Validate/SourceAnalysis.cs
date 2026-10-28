using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NksHub.Textida.PluginSdk.Analyzers;

namespace NksHub.Textida.Plugin.Validate;

/// <summary>
/// Whether the C# sources of a plugin are fit for NativeAOT: the project must bring the analyzer <c>NKSPLG001</c> and must not switch the trimming warnings off, and the analyzer is run over the code here
/// as well, so the check does not depend on the plugin having been built with it.
/// </summary>
internal static class SourceAnalysis
{
    private static readonly string[] Skipped = ["bin", "obj", ".git", "node_modules"];

    public static async Task<List<Finding>> RunAsync(string folder, CancellationToken ct)
    {
        var findings = new List<Finding>();
        if (!Directory.Exists(folder))
        {
            findings.Add(new(FindingSeverity.Error, "source", $"The source folder '{folder}' does not exist."));
            return findings;
        }
        var files = Enumerate(folder, "*.cs").ToList();
        var projects = Enumerate(folder, "*.csproj").ToList();
        if (files.Count == 0)
        {
            findings.Add(new(FindingSeverity.Warning, "source", $"There is no C# source in '{folder}'."));
            return findings;
        }
        foreach (var project in projects) CheckProject(project, findings);
        if (projects.Count == 0) findings.Add(new(FindingSeverity.Warning, "source", "There is no project file next to the sources; the build settings were not checked."));

        var trees = new List<SyntaxTree>();
        foreach (var file in files) trees.Add(CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(file, ct).ConfigureAwait(false), path: file, cancellationToken: ct));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToList();
        var compilation = CSharpCompilation.Create("plugin", trees, references, new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
        var analyzed = await compilation.WithAnalyzers([new ReflectionBanAnalyzer()]).GetAnalyzerDiagnosticsAsync(ct).ConfigureAwait(false);

        foreach (var d in analyzed.Where(d => d.Id == ReflectionBanAnalyzer.DiagnosticId).OrderBy(d => d.Location.SourceTree?.FilePath).ThenBy(d => d.Location.SourceSpan.Start))
        {
            var span = d.Location.GetLineSpan();
            findings.Add(new(FindingSeverity.Error, "aot", $"{Path.GetRelativePath(folder, span.Path)}({span.StartLinePosition.Line + 1}): {d.GetMessage()}"));
        }
        var unresolved = compilation.GetDiagnostics(ct).Count(d => d.Severity == DiagnosticSeverity.Error);
        if (unresolved > 0)
            findings.Add(new(FindingSeverity.Info, "source", $"{unresolved} compile errors (code a source generator writes is not here); the analyzer ran over the code that compiled."));
        if (!analyzed.Any(d => d.Id == ReflectionBanAnalyzer.DiagnosticId))
            findings.Add(new(FindingSeverity.Info, "source", $"The analyzer NKSPLG001 found no reflection in {files.Count} files."));
        return findings;
    }

    private static IEnumerable<string> Enumerate(string folder, string pattern) =>
        Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories).Where(f => !Path.GetRelativePath(folder, f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => Skipped.Contains(part)));

    private static void CheckProject(string project, List<Finding> findings)
    {
        XDocument xml;
        try { xml = XDocument.Load(project); }
        catch (System.Xml.XmlException e)
        {
            findings.Add(new(FindingSeverity.Error, "source", $"{Path.GetFileName(project)} is not valid XML: {e.Message}"));
            return;
        }
        var name = Path.GetFileName(project);
        string Property(string property) => xml.Descendants().Where(e => e.Name.LocalName == property).Select(e => e.Value.Trim()).LastOrDefault() ?? "";
        bool Imports(string part) => xml.Descendants().Any(e => e.Name.LocalName == "Import" && ((string?)e.Attribute("Project"))?.Contains(part, StringComparison.OrdinalIgnoreCase) == true);
        bool References(string package) => xml.Descendants().Any(e => e.Name.LocalName == "PackageReference" && string.Equals((string?)e.Attribute("Include"), package, StringComparison.OrdinalIgnoreCase));

        if (!References("NksHub.Textida.Plugin.Sdk") && !Imports("Plugin.props"))
            findings.Add(new(FindingSeverity.Error, "aot", $"{name} neither references the package NksHub.Textida.Plugin.Sdk nor imports Plugin.props, so the build does not run the analyzer NKSPLG001 and does not turn the trimming warnings into errors."));
        if (!string.Equals(Property("PublishAot"), "true", StringComparison.OrdinalIgnoreCase))
            findings.Add(new(FindingSeverity.Warning, "aot", $"{name} does not set PublishAot to true; the plugin is not published as a NativeAOT program."));
        if (Regex.IsMatch(Property("NoWarn"), @"NKSPLG001|IL2\d{3}|IL3\d{3}")) findings.Add(new(FindingSeverity.Error, "aot", $"{name} switches off NKSPLG001 or a warning of the trimming or AOT analyzer in NoWarn."));
        if (string.Equals(Property("SuppressTrimAnalysisWarnings"), "true", StringComparison.OrdinalIgnoreCase))
            findings.Add(new(FindingSeverity.Error, "aot", $"{name} sets SuppressTrimAnalysisWarnings; a warning of the trimmer is a plugin that fails at run time."));
        if (string.Equals(Property("JsonSerializerIsReflectionEnabledByDefault"), "true", StringComparison.OrdinalIgnoreCase))
            findings.Add(new(FindingSeverity.Error, "aot", $"{name} lets System.Text.Json use reflection; a NativeAOT plugin cannot."));
    }
}

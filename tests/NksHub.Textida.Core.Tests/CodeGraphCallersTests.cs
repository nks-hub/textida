using System.Diagnostics;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Tests;

/// <summary>PZ.6.4: the output of <c>codegraph callers --json</c>, the folder that makes the command available, and (where codegraph is installed) a real run over a small indexed project.</summary>
public sealed class CodeGraphCallersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cg-" + Guid.NewGuid().ToString("N")[..8]);

    public CodeGraphCallersTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    // the shape codegraph printed for a symbol with two callers
    private const string Sample = """
        {
          "symbol": "helper",
          "targets": [ { "name": "helper", "kind": "function", "filePath": "src/a.js", "startLine": 1 } ],
          "ambiguous": false,
          "file": "src/b.js",
          "filteredOut": true,
          "note": "no definition of \"helper\" matches file \"src/b.js\" - showing all definitions instead.",
          "definitions": [
            {
              "definition": { "name": "helper", "kind": "function", "filePath": "src/a.js", "startLine": 1 },
              "roots": [ "function:1" ],
              "callers": [
                { "id": "function:2", "name": "other", "kind": "function", "filePath": "src/a.js", "startLine": 3, "relationships": [ "calls" ] },
                { "id": "function:3", "name": "third", "kind": "function", "filePath": "src/b.js", "startLine": 2, "relationships": [ "calls" ] },
                { "id": "file:4", "name": "b.js", "kind": "file", "filePath": "src/b.js", "startLine": 1, "relationships": [ "imports" ] }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void The_json_gives_the_definitions_their_callers_and_the_note_and_plain_text_means_not_found()
    {
        var answer = CodeGraphCallers.Parse("helper", Sample);

        Assert.False(answer.NotFound);
        var definition = Assert.Single(answer.Definitions);
        Assert.Equal(("helper", "src/a.js", 1), (definition.Name, definition.File, definition.Line));
        Assert.Equal([("other", "src/a.js", 3), ("third", "src/b.js", 2)], definition.Callers.Select(c => (c.Name, c.File, c.Line)));
        Assert.Contains("showing all definitions", answer.Note);

        var none = CodeGraphCallers.Parse("nosuch", "ℹ Symbol \"nosuch\" not found\n");
        Assert.True(none.NotFound);
        Assert.Contains("not found", none.Message);
        Assert.True(CodeGraphCallers.Parse("x", "{ broken").NotFound);
        Assert.True(CodeGraphCallers.Parse("x", "").NotFound);
    }

    [Fact]
    public void The_command_is_available_only_below_a_codegraph_folder_and_the_symbol_follows_the_double_dash()
    {
        string file = Path.Combine(_root, "src", "deep", "a.js");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        Assert.Null(CodeGraphCallers.ProjectRoot(Path.GetDirectoryName(file)!));
        Directory.CreateDirectory(Path.Combine(_root, ".codegraph"));
        Assert.Equal(_root, CodeGraphCallers.ProjectRoot(Path.GetDirectoryName(file)!), ignoreCase: OperatingSystem.IsWindows());

        var args = CodeGraphCallers.Arguments(_root, "-weird", "src/deep/a.js");
        Assert.Equal(["callers", "--json", "-p", _root, "-l", "50", "-f", "src/deep/a.js", "--", "-weird"], args);
        Assert.Equal("codegraph", LanguageCatalog.CodeGraph.Executable);
    }

    [Fact]
    public async Task A_real_codegraph_lists_the_callers_of_a_function_and_reports_an_unknown_symbol()
    {
        if (ToolLocator.Find("codegraph") is not { } program) return; // codegraph is not installed here
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "a.js"), "function helper(x) { return x + 1; }\nfunction caller(y) { return helper(y); }\nfunction other() { return helper(2) + caller(1); }\n");
        File.WriteAllText(Path.Combine(_root, "src", "b.js"), "import { helper } from \"./a.js\";\nexport function third() { return helper(3); }\n");
        var init = new ProcessStartInfo(program) { WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "init", "-i", "." }) init.ArgumentList.Add(a);
        if (ProcessLaunch.IsBatch(program)) init = new ProcessStartInfo(program, "init -i .") { WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = Process.Start(init)!) await process.WaitForExitAsync();

        var run = await CodeGraphCallers.RunAsync(CodeGraphCallers.Invocation(program, _root, CodeGraphCallers.Arguments(_root, "helper", "src/a.js")));
        Assert.Equal(0, run.ExitCode);
        var answer = CodeGraphCallers.Parse("helper", run.Output);
        Assert.Equal(["caller", "other", "third"], answer.Definitions.Single().Callers.Select(c => c.Name).Order());

        var unknown = await CodeGraphCallers.RunAsync(CodeGraphCallers.Invocation(program, _root, CodeGraphCallers.Arguments(_root, "nosuchsymbol", null)));
        Assert.True(CodeGraphCallers.Parse("nosuchsymbol", unknown.Output).NotFound);
    }
}

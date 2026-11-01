using System.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Tests;

/// <summary>PZ.6.5: the JSON stream of ast-grep, the command line, and (where the program is installed) a real run over a folder and over standard input.</summary>
public sealed class AstGrepTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astgrep-" + Guid.NewGuid().ToString("N")[..8]);

    public AstGrepTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    // the line ast-grep 0.51 printed for a rewrite on a file
    private const string Sample = "{\"text\":\"console.log(\\\"a\\\")\",\"range\":{\"byteOffset\":{\"start\":17,\"end\":33},\"start\":{\"line\":1,\"column\":2},\"end\":{\"line\":1,\"column\":18}},\"file\":\"t.js\",\"lines\":\"  console.log(\\\"a\\\");\",\"charCount\":{\"leading\":2,\"trailing\":1},\"replacement\":\"logger.info(\\\"a\\\")\",\"replacementOffsets\":{\"start\":17,\"end\":33},\"language\":\"JavaScript\",\"metaVariables\":{\"single\":{},\"multi\":{},\"transformed\":{}}}";

    [Fact]
    public void A_line_of_the_stream_gives_the_range_the_line_and_the_replacement_and_anything_else_gives_nothing()
    {
        var match = AstGrep.ParseLine(Sample)!;
        Assert.Equal(("t.js", 17, 33, 2, 2, "console.log(\"a\")", "logger.info(\"a\")"), (match.File, match.Start, match.End, match.Line, match.Column, match.Text, match.Replacement));
        Assert.Null(AstGrep.ParseLine("not json"));
        Assert.Null(AstGrep.ParseLine("{\"text\":\"x\"}"));
        Assert.Null(AstGrep.ParseLine("[1]"));
        Assert.Null(AstGrep.ParseLine(Sample.Replace("\"replacement\":\"logger.info(\\\"a\\\")\",", "")!.Replace("\"file\"", "\"f\""))!.Replacement);
    }

    [Fact]
    public void The_arguments_keep_a_pattern_that_starts_with_a_dash_as_a_value_and_exclude_the_tabs_sent_on_input()
    {
        Assert.Equal(["run", "--pattern=-x $A", "--rewrite=y($A)", "--lang=js", "--json=stream", "--globs=!sub/a.js", "."], AstGrep.Arguments("-x $A", "y($A)", "js", false, ["sub\\a.js"]));
        Assert.Equal(["run", "--pattern=p", "--lang=cs", "--json=stream", "--stdin"], AstGrep.Arguments("p", "", "cs", true, ["ignored"]));
        Assert.Equal(("ts", "tsx", "cs", null), (AstGrep.LanguageFor("typescript"), AstGrep.LanguageFor("typescript", ".TSX"), AstGrep.LanguageFor("csharp"), AstGrep.LanguageFor("plaintext")));
    }

    private static string? Program => Environment.GetEnvironmentVariable("TEXTIDA_TEST_AST_GREP") ?? ToolLocator.Find(LanguageCatalog.StructuralSearch.Executable);

    [Fact]
    public async Task A_real_run_finds_and_rewrites_in_a_folder_and_in_the_bytes_of_an_open_tab()
    {
        if (Program is not { } program) return; // ast-grep is not installed on this machine
        string file = Path.Combine(_root, "a.js");
        File.WriteAllText(file, "function f() {\n  console.log(\"é\");\n  return console.log(1 + 2);\n}\n");
        File.WriteAllText(Path.Combine(_root, "tab.js"), "console.log('on disk')\n");

        var args = AstGrep.Arguments("console.log($A)", "logger.info($A)", "js", false, ["tab.js"]);
        var run = await AstGrep.RunAsync(AstGrep.Invocation(program, _root, args), null);

        Assert.Equal(2, run.Matches.Count); // tab.js is left out: its bytes come from the editor
        var change = StructuralPlanner.ForFile(file, run.Matches)!;
        Assert.Equal(2, change.Hunks.Count);
        var plan = ChangeBuilder.Proposal("t", "ast-grep", change);
        var report = new ChangeApplier(_ => null, null).Apply(plan, new ChangeSelection());
        Assert.Equal(1, report.Applied);
        Assert.Equal("function f() {\n  logger.info(\"é\");\n  return logger.info(1 + 2);\n}\n", File.ReadAllText(file));

        var tab = Encoding.UTF8.GetBytes("let x = console.log('unsaved é')\n");
        var fromTab = await AstGrep.RunAsync(AstGrep.Invocation(program, _root, AstGrep.Arguments("console.log($A)", "L($A)", "js", true, [])), tab);
        var m = Assert.Single(fromTab.Matches);
        Assert.Equal("let x = L('unsaved é')\n", Encoding.UTF8.GetString([.. tab[..m.Start], .. Encoding.UTF8.GetBytes(m.Replacement!), .. tab[m.End..]]));

        var none = await AstGrep.RunAsync(AstGrep.Invocation(program, _root, AstGrep.Arguments("nothing_like_this($A)", "", "js", false, [])), null);
        Assert.Empty(none.Matches);
    }

    [Fact]
    public async Task A_program_that_is_not_there_is_a_tool_error()
    {
        await Assert.ThrowsAsync<ToolException>(() => AstGrep.RunAsync(AstGrep.Invocation(Path.Combine(_root, "missing-ast-grep.exe"), _root, ["run"]), null));
    }
}

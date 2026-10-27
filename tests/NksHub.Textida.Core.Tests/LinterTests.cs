using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P8.8.1: the linters of the catalog through the real runner. A copy of ToolEcho named like the linter stands in for it and prints a recorded sample (written here in my own words after the documented
/// output shape of each tool). The real PHPStan, Psalm, golangci-lint, Clippy, clang-tidy and RuboCop are not installed, so their command lines and output are not verified against the programs.
/// </summary>
public sealed class LinterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-lint-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string ExeName(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    /// <param name="Sample">The recorded output; <c>{FILE}</c> is the full path of the file and <c>{FILEJSON}</c> the same escaped for a JSON string.</param>
    private sealed record Case(string Id, string File, string Text, string[] Project, string Sample, string Underlined, int Line, int Column, ToolSeverity Severity, string? Code,
        string Syntax, ToolSeverity SyntaxSeverity, string Clean);

    private static readonly Case[] Cases =
    [
        new("phpstan", "src/a.php", "<?php\n\nfoo($x);\n", ["composer.json"],
            """{"totals":{"errors":0,"file_errors":1},"files":{"{FILEJSON}":{"errors":1,"messages":[{"message":"Function foo not found.","line":3,"ignorable":true,"identifier":"function.notFound"}]}},"errors":[]}""",
            "foo($x);", 3, 1, ToolSeverity.Error, "function.notFound",
            """{"totals":{"errors":0,"file_errors":1},"files":{"{FILEJSON}":{"errors":1,"messages":[{"message":"Syntax error, unexpected ';' on line 3","line":3,"ignorable":false}]}},"errors":[]}""", ToolSeverity.Error,
            """{"totals":{"errors":0,"file_errors":0},"files":[],"errors":[]}"""),
        new("psalm", "src/a.php", "<?php\n$a = $b + 1;\n", ["composer.json"],
            """[{"severity":"error","line_from":2,"line_to":2,"type":"UndefinedVariable","message":"Cannot find referenced variable $b","file_name":"src/a.php","file_path":"{FILEJSON}","selected_text":"$b","from":14,"to":16,"column_from":6,"column_to":8}]""",
            "$b", 2, 6, ToolSeverity.Error, "UndefinedVariable",
            """[{"severity":"error","line_from":2,"line_to":2,"type":"ParseError","message":"Parse error: unexpected token","file_path":"{FILEJSON}","column_from":1,"column_to":3}]""", ToolSeverity.Error,
            "[]"),
        new("golangci-lint", "main.go", "package main\n\nfunc main() {\n\tos.Remove(\"x\")\n}\n", ["go.mod"],
            """{"Issues":[{"FromLinter":"errcheck","Text":"Error return value of `os.Remove` is not checked","Severity":"","Pos":{"Filename":"main.go","Offset":38,"Line":4,"Column":2}}],"Report":{"Linters":[{"Name":"errcheck","Enabled":true}]}}""",
            "os", 4, 2, ToolSeverity.Warning, "errcheck",
            """{"Issues":[{"FromLinter":"typecheck","Text":"expected declaration, found 'x'","Severity":"error","Pos":{"Filename":"main.go","Line":5,"Column":1}}]}""", ToolSeverity.Error,
            """{"Issues":null,"Report":{}}"""),
        new("clippy", "src/lib.rs", "pub fn f() -> i32 {\n    return 1;\n}\n", ["Cargo.toml"],
            """
            {"reason":"compiler-artifact","package_id":"demo 0.1.0","target":{"name":"demo"}}
            {"reason":"compiler-message","package_id":"demo 0.1.0","message":{"level":"warning","message":"unneeded `return` statement","code":{"code":"clippy::needless_return"},"spans":[{"file_name":"src/lib.rs","byte_start":24,"byte_end":33,"line_start":2,"line_end":2,"column_start":5,"column_end":14,"is_primary":true}]}}
            {"reason":"compiler-message","package_id":"demo 0.1.0","message":{"level":"warning","message":"another file","code":null,"spans":[{"file_name":"src/other.rs","byte_start":0,"byte_end":3,"line_start":1,"line_end":1,"column_start":1,"column_end":4,"is_primary":true}]}}
            {"reason":"compiler-message","package_id":"demo 0.1.0","message":{"level":"warning","message":"1 warning emitted","code":null,"spans":[]}}
            {"reason":"build-finished","success":true}
            """,
            "return 1;", 2, 5, ToolSeverity.Warning, "clippy::needless_return",
            """
            #exit 101
            {"reason":"compiler-message","package_id":"demo 0.1.0","message":{"level":"error","message":"expected one of `;` or `}`, found `x`","code":null,"spans":[{"file_name":"src/lib.rs","byte_start":24,"byte_end":30,"line_start":2,"line_end":2,"column_start":5,"column_end":11,"is_primary":true}]}}
            {"reason":"build-finished","success":false}
            """, ToolSeverity.Error,
            """
            {"reason":"compiler-artifact","package_id":"demo 0.1.0","target":{"name":"demo"}}
            {"reason":"build-finished","success":true}
            """),
        new("clang-tidy", "src/a.cpp", "int main() {\n  int x = 0;\n  return 0;\n}\n", [".clang-tidy"],
            "{FILE}:2:7: warning: unused variable 'x' [clang-diagnostic-unused-variable]\n  int x = 0;\n      ^\n1 warning generated.",
            "x", 2, 7, ToolSeverity.Warning, "clang-diagnostic-unused-variable",
            "#exit 1\n{FILE}:2:12: error: expected ';' at end of declaration\n  int x = 0\n           ^", ToolSeverity.Error,
            ""),
        new("rubocop-lint", "a.rb", "x = 1\nputs x \n", [".rubocop.yml"],
            """{"metadata":{"rubocop_version":"1.0.0"},"files":[{"path":"a.rb","offenses":[{"severity":"convention","message":"Layout/TrailingWhitespace: Trailing whitespace detected.","cop_name":"Layout/TrailingWhitespace","corrected":false,"correctable":true,"location":{"start_line":2,"start_column":7,"last_line":2,"last_column":7,"length":1,"line":2,"column":7}}]}],"summary":{"offense_count":1}}""",
            " ", 2, 7, ToolSeverity.Info, "Layout/TrailingWhitespace",
            """{"files":[{"path":"a.rb","offenses":[{"severity":"fatal","message":"Lint/Syntax: unexpected token tEOF","cop_name":"Lint/Syntax","location":{"start_line":2,"start_column":1,"length":1}}]}]}""", ToolSeverity.Error,
            """{"files":[{"path":"a.rb","offenses":[]}],"summary":{"offense_count":0}}"""),
    ];

    public static TheoryData<string> Ids
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var c in Cases) data.Add(c.Id);
            return data;
        }
    }

    private static Case CaseOf(string id) => Cases.First(c => c.Id == id);

    private sealed record Setup(Case Case, CatalogLinter Linter, string Path, string Tool, Doc Doc, TimeSpan Timeout);

    /// <summary>A project with the file, the project marker of the linter and a copy of ToolEcho named like the linter in vendor/bin.</summary>
    private Setup Prepare(string id, string? sample = null, int seconds = 60)
    {
        var c = CaseOf(id);
        var linter = Linters.Find(id)!;
        string project = Path.Combine(_dir, id);
        foreach (var marker in c.Project) { Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(project, marker))!); File.WriteAllText(Path.Combine(project, marker), ""); }
        string path = Path.Combine(project, c.File.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, c.Text, new UTF8Encoding(false));

        string folder = Path.Combine(project, "vendor", "bin");
        Directory.CreateDirectory(folder);
        foreach (var extension in new[] { ".dll", ".runtimeconfig.json", ".deps.json" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho" + extension), Path.Combine(folder, "NksHub.Textida.ToolEcho" + extension), overwrite: true);
        string tool = Path.Combine(folder, ExeName(linter.Executable));
        File.Copy(Path.Combine(AppContext.BaseDirectory, ExeName("NksHub.Textida.ToolEcho")), tool, overwrite: true);
        Say(tool, sample ?? c.Sample, path);
        return new Setup(c, linter, path, tool, Doc.Open(path, ReferenceBackendFactory.Instance), TimeSpan.FromSeconds(seconds));
    }

    private static void Say(string tool, string sample, string path) =>
        File.WriteAllText(tool + ".sample", sample.Replace("{FILEJSON}", JsonSerializer.Serialize(path)[1..^1]).Replace("{FILE}", path), new UTF8Encoding(false));

    private static Task<LintOutcome> Run(Setup s) => Linters.RunAsync(s.Doc, s.Linter, Linters.Prepare(s.Linter, s.Path, s.Timeout));

    private static string Underlined(Doc d, LintDiagnostic f) => Encoding.UTF8.GetString(d.ReadBytes(f.Range));

    // ---- the catalog and the choice ----

    [Fact]
    public void Every_linter_of_the_plan_has_a_parser_for_its_output_format_and_a_severity_table()
    {
        foreach (var id in new[] { "phpstan", "psalm", "golangci-lint", "clippy", "clang-tidy", "rubocop-lint" })
            Assert.NotNull(Linters.Find(id));
        Assert.Equal(Cases.Select(c => c.Id).Order(), new[] { "phpstan", "psalm", "golangci-lint", "clippy", "clang-tidy", "rubocop-lint" }.Order());
        Assert.Null(Linters.Find("ruff")); // data only: no parser reads its output yet
        Assert.Equal(ToolSeverity.Error, Linters.SeverityOf(Linters.Find("phpstan")!, "anything"));
        Assert.Equal(ToolSeverity.Info, Linters.SeverityOf(Linters.Find("psalm")!, "INFO"));
        Assert.Equal(ToolSeverity.Warning, Linters.SeverityOf(Linters.Find("psalm")!, "unknown")); // no "*" row: a warning
        Assert.Equal(ToolSeverity.Hint, Linters.SeverityOf(Linters.Find("clippy")!, "help"));
        Assert.Equal(ToolSeverity.Warning, Linters.SeverityOf(Linters.Find("clippy")!, "failure-note"));
        Assert.Equal(ToolSeverity.Info, Linters.SeverityOf(Linters.Find("rubocop-lint")!, "refactor"));
    }

    [Fact]
    public void When_two_linters_handle_a_file_the_one_with_a_configuration_file_wins_else_the_first()
    {
        string project = Directory.CreateDirectory(Path.Combine(_dir, "choice", "src")).FullName;
        string file = Path.Combine(project, "a.php");
        File.WriteAllText(file, "<?php\n");
        Assert.Equal(["phpstan", "psalm"], Linters.For(file).Select(l => l.Id));
        File.WriteAllText(Path.Combine(_dir, "choice", "psalm.xml"), "<psalm/>");
        Assert.Equal("psalm", Linters.Choose(file)!.Id);
        File.WriteAllText(Path.Combine(_dir, "choice", "phpstan.neon"), "");
        Assert.Equal("phpstan", Linters.Choose(file)!.Id);
        Assert.Null(Linters.Choose(Path.Combine(project, "notes.txt")));
    }

    // ---- the scenarios, for each linter ----

    [Theory]
    [MemberData(nameof(Ids))]
    public async Task A_finding_is_underlined_on_the_right_line_and_column_with_the_severity_of_the_table(string id)
    {
        var setup = Prepare(id);
        using var doc = setup.Doc;
        var outcome = await Run(setup);

        Assert.Equal(LintState.Done, outcome.State);
        var finding = Assert.Single(outcome.Findings);
        Assert.Equal(setup.Case.Underlined, Underlined(doc, finding));
        Assert.Equal((setup.Case.Line, setup.Case.Column, setup.Case.Severity, setup.Case.Code), (finding.Line, finding.Column, finding.Severity, finding.Code));
        Assert.Equal(doc.Revision, outcome.Revision);
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public async Task After_a_fix_the_next_run_has_no_findings_and_a_clean_run_is_a_success(string id)
    {
        var setup = Prepare(id);
        using var doc = setup.Doc;
        Assert.Single((await Run(setup)).Findings);

        Say(setup.Tool, setup.Case.Clean, setup.Path);
        var again = await Run(setup);

        Assert.Equal(LintState.Done, again.State);
        Assert.Empty(again.Findings);
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public async Task A_syntax_error_the_linter_reports_is_an_error_finding_and_a_crash_or_bad_output_is_reported(string id)
    {
        var setup = Prepare(id, CaseOf(id).Syntax);
        using var doc = setup.Doc;
        var syntax = await Run(setup);
        Assert.Equal(LintState.Done, syntax.State);
        Assert.Equal(setup.Case.SyntaxSeverity, Assert.Single(syntax.Findings).Severity);

        Say(setup.Tool, "#exit 2\nFatal error: something broke inside the tool", setup.Path);
        var crashed = await Run(setup);
        Assert.Equal(LintState.Failed, crashed.State);
        Assert.Contains("code 2", crashed.Reason);
        Assert.Contains("something broke", crashed.Reason);

        if (setup.Linter.OutputFormat != "gcc-text") // free text has no shape that could be wrong
        {
            Say(setup.Tool, "this is not what the documentation says", setup.Path);
            var garbage = await Run(setup);
            Assert.Equal(LintState.BadOutput, garbage.State);
            Assert.Empty(garbage.Findings);
        }
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void A_missing_program_is_reported_before_anything_runs(string id)
    {
        var setup = Prepare(id);
        using var doc = setup.Doc;
        var missing = setup.Linter with { Executable = "textida-no-such-" + Guid.NewGuid().ToString("N")[..8] };
        var e = Assert.Throws<ToolException>(() => Linters.Prepare(missing, setup.Path, setup.Timeout));
        Assert.Contains("was not found", e.Message);
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public async Task A_linter_over_its_time_limit_is_stopped_and_reported(string id)
    {
        var setup = Prepare(id, "#sleep", seconds: 1);
        using var doc = setup.Doc;
        var watch = Stopwatch.StartNew();
        var outcome = await Run(setup);

        Assert.Equal(LintState.TimedOut, outcome.State);
        Assert.Contains("did not finish in 1 s", outcome.Reason);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30));
    }

    // ---- details ----

    [Fact]
    public async Task A_document_with_unsaved_changes_is_not_checked_because_the_linter_reads_the_file()
    {
        var setup = Prepare("phpstan");
        using var doc = setup.Doc;
        doc.Insert(0, "x"u8.ToArray());
        var outcome = await Run(setup);
        Assert.Equal(LintState.Skipped, outcome.State);
        Assert.Contains("save first", outcome.Reason);
    }

    [Fact]
    public async Task Columns_count_characters_for_text_tools_and_bytes_for_Go_and_clang_tidy()
    {
        // 'é' is two bytes: RuboCop counts it as one column, golangci-lint as two
        var setup = Prepare("rubocop-lint");
        using var doc = setup.Doc;
        File.WriteAllText(setup.Path, "é = 1 \n", new UTF8Encoding(false));
        using var edited = Doc.Open(setup.Path, ReferenceBackendFactory.Instance);
        Say(setup.Tool, """{"files":[{"path":"a.rb","offenses":[{"severity":"convention","message":"m","cop_name":"c","location":{"start_line":1,"start_column":6,"length":1}}]}]}""", setup.Path);
        var finding = Assert.Single((await Linters.RunAsync(edited, setup.Linter, Linters.Prepare(setup.Linter, setup.Path, setup.Timeout))).Findings);
        Assert.Equal(" ", Underlined(edited, finding));

        var go = Prepare("golangci-lint");
        using var goDoc = go.Doc;
        File.WriteAllText(go.Path, "é := 1\n", new UTF8Encoding(false));
        using var goEdited = Doc.Open(go.Path, ReferenceBackendFactory.Instance);
        Say(go.Tool, """{"Issues":[{"FromLinter":"x","Text":"m","Severity":"","Pos":{"Filename":"main.go","Line":1,"Column":4}}]}""", go.Path);
        var goFinding = Assert.Single((await Linters.RunAsync(goEdited, go.Linter, Linters.Prepare(go.Linter, go.Path, go.Timeout))).Findings);
        Assert.Equal(":", Underlined(goEdited, goFinding)); // byte column 4 is the colon; counted as characters it would be the equals sign
    }

    [Fact]
    public async Task Clippy_needs_a_Cargo_toml_and_runs_in_its_folder()
    {
        var setup = Prepare("clippy");
        using var doc = setup.Doc;
        Assert.Equal(Path.Combine(_dir, "clippy"), Linters.Prepare(setup.Linter, setup.Path, setup.Timeout).WorkingDirectory, ignoreCase: OperatingSystem.IsWindows());
        File.Delete(Path.Combine(_dir, "clippy", "Cargo.toml"));
        Assert.Contains("Cargo.toml", Assert.Throws<ToolException>(() => Linters.Prepare(setup.Linter, setup.Path, setup.Timeout)).Message);
        await Task.CompletedTask;
    }
}

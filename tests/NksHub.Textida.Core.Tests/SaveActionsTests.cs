using System.Diagnostics;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>A test that needs a real program; it is skipped where the program is not on PATH.</summary>
public sealed class InstalledFactAttribute : FactAttribute
{
    public InstalledFactAttribute(string program)
    {
        if (ToolLocator.Find(program) is null) Skip = program + " is not installed here.";
    }
}

/// <summary>
/// P8.3.1: the order of a save with actions, with the test program of ToolEcho as the formatter (success, a syntax error, a missing program, a timeout).
/// Every formatter of <see cref="LanguageCatalog"/> goes through the same four cases with a copy of ToolEcho named like it, which gets the arguments of the catalog.
/// gofmt, clang-format and dart format also have a test with the real program, which runs where it is on PATH.
/// </summary>
public sealed class SaveActionsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-saveact-").FullName;

    public void Dispose() => TestDir.Delete(_dir);

    private static string Dotnet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
    private static string EchoDll => Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho.dll");

    private static FormatterDef Fake(params string[] mode) => new("fake", "Fake", Dotnet, [EchoDll, .. mode, "${File}"], [], [".txt"]);

    private Doc OpenModified(string text = "alpha\nbeta\n", string name = "f.txt")
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        var doc = Doc.Open(path, ReferenceBackendFactory.Instance);
        doc.Insert(0, "x"u8.ToArray());
        return doc;
    }

    private static string Text(Doc d) => Encoding.UTF8.GetString(d.ReadBytes(new ByteRange(0, d.ByteLength)));

    private static SaveAction Act(SaveActionKind kind, string id = "fake", bool onlyManual = false, bool enabled = true) => new(kind, id, onlyManual, enabled);

    private static SaveActionSet Set(TimeSpan? timeout, params SaveAction[] actions) => new(actions, timeout, []);

    private static Task<bool> Save(Doc d) => Task.FromResult(d.Save().Saved);

    // ---- the formatter transaction ----

    [Fact]
    public async Task A_formatter_that_succeeds_edits_the_document_as_one_step_and_leaves_no_temporary_file()
    {
        using var doc = OpenModified();
        var outcome = await ConfigFormatters.FormatAsync(doc, Fake("--format"), TimeSpan.FromSeconds(60));

        Assert.Equal(FormatResult.Formatted, outcome.Result);
        Assert.Equal("XALPHA\nBETA\n", Text(doc));
        Assert.Equal([Path.Combine(_dir, "f.txt")], Directory.GetFiles(_dir));
        Assert.NotNull(doc.Undo());
        Assert.Equal("xalpha\nbeta\n", Text(doc));
    }

    [Fact]
    public async Task A_syntax_error_leaves_the_document_and_names_the_reason()
    {
        using var doc = OpenModified();
        var outcome = await ConfigFormatters.FormatAsync(doc, Fake("--format-fail"), TimeSpan.FromSeconds(60));

        Assert.Equal(FormatResult.Failed, outcome.Result);
        Assert.Contains("syntax error", outcome.Reason);
        Assert.Contains("code 2", outcome.Reason);
        Assert.Equal("xalpha\nbeta\n", Text(doc));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task A_missing_program_is_reported_and_the_document_stays()
    {
        using var doc = OpenModified();
        var missing = new FormatterDef("fake", "Fake", "textida-no-such-formatter", ["${File}"], [], [".txt"]);
        var outcome = await ConfigFormatters.FormatAsync(doc, missing, TimeSpan.FromSeconds(60));

        Assert.Equal(FormatResult.Missing, outcome.Result);
        Assert.Contains("not found", outcome.Reason);
        Assert.Equal("xalpha\nbeta\n", Text(doc));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task A_formatter_over_its_time_is_stopped_and_the_document_stays()
    {
        using var doc = OpenModified();
        var outcome = await ConfigFormatters.FormatAsync(doc, Fake("--sleep"), TimeSpan.FromMilliseconds(1500));

        Assert.Equal(FormatResult.TimedOut, outcome.Result);
        Assert.Equal("xalpha\nbeta\n", Text(doc));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task A_document_that_changed_while_the_formatter_ran_keeps_what_was_typed()
    {
        using var doc = OpenModified();
        var slow = new FormatterDef("fake", "Fake", Dotnet, [EchoDll, "--format", "${File}"], [], [".txt"]);
        var task = ConfigFormatters.FormatAsync(doc, slow, TimeSpan.FromSeconds(60));
        doc.Insert(0, "typed "u8.ToArray()); // the document moved on before the result came back
        var outcome = await task;

        Assert.Equal(FormatResult.Skipped, outcome.Result);
        Assert.Equal("typed xalpha\nbeta\n", Text(doc));
    }

    [Fact]
    public void The_edit_of_a_format_covers_only_the_lines_that_changed()
    {
        var (range, insert) = ConfigFormatters.Narrow("a\nb\nc\nd\n"u8.ToArray(), "a\nB\nc\nd\n"u8.ToArray(), 0, lineSafe: true);
        Assert.Equal((2, 4), (range.Start, range.End));
        Assert.Equal("B\n", Encoding.UTF8.GetString(insert));

        var (whole, all) = ConfigFormatters.Narrow("ab"u8.ToArray(), "AB"u8.ToArray(), 0, lineSafe: false);
        Assert.Equal((0, 2), (whole.Start, whole.End));
        Assert.Equal("AB", Encoding.UTF8.GetString(all));
    }

    [InstalledFact("gofmt")]
    public async Task The_gofmt_of_the_catalog_formats_a_go_file_when_it_is_installed()
    {
        File.WriteAllText(Path.Combine(_dir, "go.mod"), "module x\n");
        string path = Path.Combine(_dir, "main.go");
        File.WriteAllText(path, "package main\n\nfunc  main( ){}\n");
        using var doc = Doc.Open(path, ReferenceBackendFactory.Instance);
        doc.Insert(doc.ByteLength, "\n"u8.ToArray());

        var outcome = await ConfigFormatters.FormatAsync(doc, ConfigFormatters.Find("gofmt")!, TimeSpan.FromSeconds(60));

        Assert.Equal(FormatResult.Formatted, outcome.Result);
        Assert.Contains("func main() {}", Text(doc));
        Assert.DoesNotContain(Directory.GetFiles(_dir), f => f.Contains(".textida-fmt-", StringComparison.Ordinal));
    }

    [InstalledFact("clang-format")]
    public async Task The_real_clang_format_formats_code_that_does_not_parse_and_fails_only_on_a_broken_configuration()
    {
        File.WriteAllText(Path.Combine(_dir, ".clang-format"), "BasedOnStyle: LLVM\n");
        using var good = OpenModified("int  main( ){return 0;}\n", "a.cpp");
        Assert.Equal(FormatResult.Formatted, (await ConfigFormatters.FormatAsync(good, ConfigFormatters.Find("clang-format")!, TimeSpan.FromSeconds(60))).Result);
        Assert.Equal("xint main() { return 0; }\n", Text(good));

        using var broken = OpenModified("int main( { return 0;\n", "b.cpp");
        Assert.Equal(FormatResult.Formatted, (await ConfigFormatters.FormatAsync(broken, ConfigFormatters.Find("clang-format")!, TimeSpan.FromSeconds(60))).Result);

        File.WriteAllText(Path.Combine(_dir, ".clang-format"), "BasedOnStyle: NoSuchStyle\n");
        using var config = OpenModified("int  main( ){return 0;}\n", "c.cpp");
        var outcome = await ConfigFormatters.FormatAsync(config, ConfigFormatters.Find("clang-format")!, TimeSpan.FromSeconds(60));
        Assert.Equal(FormatResult.Failed, outcome.Result);
        Assert.Contains("code 1", outcome.Reason);
        Assert.Equal("xint  main( ){return 0;}\n", Text(config));
    }

    [InstalledFact("dart")]
    public async Task The_real_dart_format_formats_a_file_and_ends_with_code_65_on_code_it_cannot_read()
    {
        File.WriteAllText(Path.Combine(_dir, "analysis_options.yaml"), "formatter:\n  page_width: 80\n");
        using var good = OpenModified("void main( ){print(1);}\n", "a.dart");
        Assert.Equal(FormatResult.Formatted, (await ConfigFormatters.FormatAsync(good, ConfigFormatters.Find("dart-format")!, TimeSpan.FromSeconds(120))).Result);
        Assert.Equal("xvoid main() {\n  print(1);\n}\n", Text(good));

        using var broken = OpenModified("void main( { print(1);\n", "b.dart");
        var outcome = await ConfigFormatters.FormatAsync(broken, ConfigFormatters.Find("dart-format")!, TimeSpan.FromSeconds(120));
        Assert.Equal(FormatResult.Failed, outcome.Result);
        Assert.Contains("code 65", outcome.Reason);
        Assert.Equal("xvoid main( { print(1);\n", Text(broken));
    }

    // ---- every formatter of the catalog, through a copy of ToolEcho named like it ----

    /// <param name="File">The file to format, relative to the project.</param>
    /// <param name="Syntax">What the program prints when the text does not parse (written here after the documented behaviour of the tool; clang-format formats such text and fails on a broken configuration instead), and its exit code.</param>
    private sealed record FormatterCase(string Id, string File, string Text, string Syntax, int SyntaxCode);

    private static readonly FormatterCase[] FormatterCases =
    [
        new("gofmt", "main.go", "package main\n\nfunc  main( ){}\n", "main.go:3:1: expected declaration, found x", 2),
        new("rustfmt", "src/lib.rs", "pub fn f( )->i32{1}\n", "error: expected item, found `x`", 1),
        new("clang-format", "src/a.cpp", "int  main( ){return 0;}\n", ".clang-format:1:1: error: Unknown value for BasedOnStyle: NoSuchStyle", 1),
        new("rubocop", "lib/a.rb", "x =  1\n", "a.rb:2:1: F: Lint/Syntax: unexpected token $end", 1),
        new("prettier", "src/a.js", "let  x=1\n", "[error] a.js: SyntaxError: Unexpected token (2:1)", 2),
        new("biome", "src/a.ts", "let  x=1\n", "a.ts:2:1 parse: expected a statement but found the end of the file", 1),
        new("sqlfluff-fix", "q.sql", "select  1\n", "q.sql: unparsable section at line 1, nothing was fixed", 1),
        new("ruff-format", "pkg/a.py", "x =  1\n", "error: Failed to parse a.py:2:1: Expected an expression", 2),
        new("shfmt", "run.sh", "echo  hi\n", "run.sh:2:1: reached EOF without closing quote '", 1),
        new("stylua", "a.lua", "local  x=1\n", "error: could not format file a.lua: failed to parse", 1),
        new("dart-format", "lib/a.dart", "void main( ){}\n", "line 1, column 13 of a.dart: Expected to find '}'.", 65),
    ];

    /// <summary>The formatters these cases cover; the matrix of the catalog is checked against it.</summary>
    public static IReadOnlyList<string> FormatterCaseIds => [.. FormatterCases.Select(c => c.Id)];

    public static TheoryData<string> FormatterIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var c in FormatterCases) data.Add(c.Id);
            return data;
        }
    }

    private static string ExeName(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    private sealed record FormatterSetup(FormatterCase Case, FormatterDef Def, string Path, string Tool, Doc Doc)
    {
        public string Unchanged => Case.Text + "\n";
        public bool NoCopyLeft => !Directory.GetFiles(System.IO.Path.GetDirectoryName(Path)!).Any(f => f.Contains(".textida-fmt-", StringComparison.Ordinal));
    }

    /// <summary>
    /// A project with the first configuration file of the formatter and the file with one unsaved change, and a copy of ToolEcho named like the program in a folder of its own.
    /// The definition is the one of the catalog with the full path of that copy, so the arguments are exactly those of the catalog.
    /// </summary>
    private FormatterSetup PrepareFormatter(string id, string sample)
    {
        var c = FormatterCases.First(f => f.Id == id);
        var def = ConfigFormatters.Find(id)!;
        string project = Path.Combine(_dir, id);
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, def.ConfigFiles[0]), "");
        string path = Path.Combine(project, c.File.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, c.Text, new UTF8Encoding(false));

        string folder = Path.Combine(_dir, "bin-" + id);
        Directory.CreateDirectory(folder);
        foreach (var extension in new[] { ".dll", ".runtimeconfig.json", ".deps.json" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho" + extension), Path.Combine(folder, "NksHub.Textida.ToolEcho" + extension), overwrite: true);
        string tool = Path.Combine(folder, ExeName(def.Executable));
        File.Copy(Path.Combine(AppContext.BaseDirectory, ExeName("NksHub.Textida.ToolEcho")), tool, overwrite: true);
        File.WriteAllText(tool + ".sample", sample, new UTF8Encoding(false));

        var doc = Doc.Open(path, ReferenceBackendFactory.Instance);
        doc.Insert(doc.ByteLength, "\n"u8.ToArray());
        return new FormatterSetup(c, def with { Executable = tool }, path, tool, doc);
    }

    [Fact]
    public void The_cases_cover_every_formatter_of_the_catalog_and_each_formatter_names_a_configuration_file()
    {
        Assert.Equal(LanguageCatalog.Formatters.Select(f => f.Id).Order(), FormatterCases.Select(c => c.Id).Order());
        Assert.Equal(LanguageCatalog.Formatters.Select(f => f.Id), ConfigFormatters.All.Select(f => f.Id));
        Assert.All(ConfigFormatters.All, f => Assert.NotEmpty(f.ConfigFiles));
    }

    [Theory]
    [MemberData(nameof(FormatterIds))]
    public async Task Each_formatter_is_offered_by_its_configuration_gets_the_arguments_of_the_catalog_and_formats_as_one_undo_step(string id)
    {
        var s = PrepareFormatter(id, "#format");
        using var doc = s.Doc;
        Assert.Contains(id, ConfigFormatters.Offered(s.Path).Select(f => f.Id));

        var outcome = await ConfigFormatters.FormatAsync(doc, s.Def, TimeSpan.FromSeconds(60));

        Assert.Equal(FormatResult.Formatted, outcome.Result);
        Assert.Equal(s.Unchanged.ToUpperInvariant(), Text(doc));
        var args = File.ReadAllLines(s.Tool + ".args");
        int file = s.Def.Args.ToList().IndexOf("${File}");
        Assert.Equal(s.Def.Args.Count, args.Length);
        Assert.Equal(s.Def.Args.Where((_, i) => i != file), args.Where((_, i) => i != file));
        // the program rewrote a copy next to the file, with its extension, so it found the configuration of the project
        Assert.Equal(Path.GetDirectoryName(s.Path), Path.GetDirectoryName(args[file]), ignoreCase: OperatingSystem.IsWindows());
        Assert.Equal(Path.GetExtension(s.Path), Path.GetExtension(args[file]));
        Assert.True(s.NoCopyLeft);
        Assert.NotNull(doc.Undo());
        Assert.Equal(s.Unchanged, Text(doc));
    }

    [Theory]
    [MemberData(nameof(FormatterIds))]
    public async Task A_syntax_error_of_each_formatter_leaves_the_document_and_names_the_exit_code_and_the_message(string id)
    {
        var c = FormatterCases.First(f => f.Id == id);
        var s = PrepareFormatter(id, $"#exit {c.SyntaxCode}\n{c.Syntax}");
        using var doc = s.Doc;

        var outcome = await ConfigFormatters.FormatAsync(doc, s.Def, TimeSpan.FromSeconds(60));

        Assert.Equal(FormatResult.Failed, outcome.Result);
        Assert.Contains($"code {c.SyntaxCode}", outcome.Reason);
        Assert.Contains(c.Syntax, outcome.Reason);
        Assert.Equal(s.Unchanged, Text(doc));
        Assert.True(s.NoCopyLeft);
    }

    [Theory]
    [MemberData(nameof(FormatterIds))]
    public async Task A_missing_program_of_each_formatter_is_reported_and_the_document_stays(string id)
    {
        var s = PrepareFormatter(id, "#format");
        using var doc = s.Doc;

        var outcome = await ConfigFormatters.FormatAsync(doc, s.Def with { Executable = "textida-no-such-" + Guid.NewGuid().ToString("N")[..8] }, TimeSpan.FromSeconds(60));

        Assert.Equal(FormatResult.Missing, outcome.Result);
        Assert.Contains("was not found", outcome.Reason);
        Assert.Equal(s.Unchanged, Text(doc));
        Assert.True(s.NoCopyLeft);
    }

    [Theory]
    [MemberData(nameof(FormatterIds))]
    public async Task Each_formatter_over_its_time_limit_is_stopped_and_the_document_stays(string id)
    {
        var s = PrepareFormatter(id, "#sleep");
        using var doc = s.Doc;
        var watch = Stopwatch.StartNew();

        var outcome = await ConfigFormatters.FormatAsync(doc, s.Def, TimeSpan.FromSeconds(1));

        Assert.Equal(FormatResult.TimedOut, outcome.Result);
        Assert.Contains("did not finish in 1 s", outcome.Reason);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30));
        Assert.Equal(s.Unchanged, Text(doc));
        Assert.True(s.NoCopyLeft);
    }

    [Fact]
    public async Task A_save_as_formats_for_the_file_it_writes_even_when_the_document_has_no_name_yet()
    {
        string target = Path.Combine(_dir, "new.txt");
        using var doc = Doc.FromBytes("fresh\n"u8.ToArray(), ReferenceBackendFactory.Instance);
        Assert.True(doc.IsModified);
        var runner = new SaveActionRunner(_ => Fake("--format"));

        var report = await runner.RunAsync(doc, Set(TimeSpan.FromSeconds(60), Act(SaveActionKind.Format)), SaveOrigin.Manual,
            () => Task.FromResult(doc.SaveAs(target, doc.Codec).Saved), _ => Task.FromResult<string?>(null), path: target);

        Assert.True(report.Saved);
        Assert.Equal(SaveActionState.Done, Assert.Single(report.Notes).State);
        Assert.Equal("FRESH\n", File.ReadAllText(target));
        Assert.Equal([target], Directory.GetFiles(_dir));
    }

    // ---- which formatter is offered ----

    [Fact]
    public void A_formatter_is_offered_only_where_its_configuration_exists()
    {
        string src = Directory.CreateDirectory(Path.Combine(_dir, "proj", "src")).FullName;
        string go = Path.Combine(src, "main.go");
        File.WriteAllText(go, "package main");
        Assert.Empty(ConfigFormatters.Offered(go));

        File.WriteAllText(Path.Combine(_dir, "proj", "go.mod"), "module x");
        Assert.Equal(["gofmt"], ConfigFormatters.Offered(go).Select(f => f.Id));
        string txt = Path.Combine(src, "a.txt");
        Assert.Empty(ConfigFormatters.Offered(txt)); // the extension is not one of gofmt
    }

    // ---- the order of a save ----

    [Fact]
    public async Task A_formatter_runs_before_the_save_and_an_upload_after_it_with_the_saved_bytes()
    {
        using var doc = OpenModified();
        var order = new List<string>();
        string? sent = null;
        var runner = new SaveActionRunner(_ => Fake("--format"));

        var report = await runner.RunAsync(doc, Set(TimeSpan.FromSeconds(60), Act(SaveActionKind.Upload, ""), Act(SaveActionKind.Format)), SaveOrigin.Manual,
            () => { order.Add("save:" + File.ReadAllText(doc.FilePath!).Replace("\n", "|")); return Save(doc); },
            a => { order.Add(a.Kind.ToString()); sent = File.ReadAllText(doc.FilePath!); return Task.FromResult<string?>(null); });

        Assert.True(report.Saved);
        Assert.Equal(["save:alpha|beta|", "Upload"], order); // the file still held the old text when the save began
        Assert.Equal("XALPHA\nBETA\n", sent);
        Assert.Equal(Text(doc), sent);
        Assert.Equal([SaveActionState.Done, SaveActionState.Done], report.Notes.Select(n => n.State).OrderBy(s => s));
    }

    [Fact]
    public async Task When_the_formatter_times_out_the_unformatted_text_is_saved_and_that_is_what_is_sent()
    {
        using var doc = OpenModified();
        string? sent = null;
        var runner = new SaveActionRunner(_ => Fake("--sleep"));

        var report = await runner.RunAsync(doc, Set(TimeSpan.FromMilliseconds(1500), Act(SaveActionKind.Format), Act(SaveActionKind.Upload, "")), SaveOrigin.Manual,
            () => Save(doc),
            a => { sent = File.ReadAllText(doc.FilePath!); return Task.FromResult<string?>(null); });

        Assert.True(report.Saved);
        Assert.Contains(report.Notes, n => n.State == SaveActionState.TimedOut);
        Assert.Equal("xalpha\nbeta\n", sent);
        Assert.False(doc.IsModified);
    }

    [Fact]
    public async Task A_formatter_without_a_set_time_limit_does_not_run_and_the_file_is_saved()
    {
        using var doc = OpenModified();
        var runner = new SaveActionRunner(_ => Fake("--format"));

        var report = await runner.RunAsync(doc, Set(null, Act(SaveActionKind.Format)), SaveOrigin.Manual, () => Save(doc), _ => Task.FromResult<string?>(null));

        Assert.True(report.Saved);
        var note = Assert.Single(report.Notes);
        Assert.Equal(SaveActionState.Skipped, note.State);
        Assert.Contains("formatTimeoutSeconds", note.Reason);
        Assert.Equal("xalpha\nbeta\n", File.ReadAllText(doc.FilePath!));
    }

    [Fact]
    public async Task Nothing_after_the_save_runs_when_the_save_fails()
    {
        using var doc = OpenModified();
        int calls = 0;
        var runner = new SaveActionRunner();

        var report = await runner.RunAsync(doc, Set(null, Act(SaveActionKind.Upload, ""), Act(SaveActionKind.Tool, "lint")), SaveOrigin.Manual,
            () => Task.FromResult(false), _ => { calls++; return Task.FromResult<string?>(null); });

        Assert.False(report.Saved);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task An_upload_is_skipped_when_the_file_is_no_longer_the_bytes_of_this_save()
    {
        using var doc = OpenModified();
        int calls = 0;
        var runner = new SaveActionRunner();

        var report = await runner.RunAsync(doc, Set(null, Act(SaveActionKind.Upload, "")), SaveOrigin.Manual,
            async () => { bool saved = await Save(doc); File.WriteAllText(doc.FilePath!, "something else"); return saved; },
            _ => { calls++; return Task.FromResult<string?>(null); });

        Assert.Equal(0, calls);
        Assert.Equal(SaveActionState.Skipped, Assert.Single(report.Notes).State);
    }

    // ---- autosave, plugins, loops ----

    [Fact]
    public async Task An_autosave_leaves_out_the_actions_marked_for_a_manual_save_only()
    {
        using var doc = OpenModified();
        var ran = new List<string>();
        var runner = new SaveActionRunner();
        var set = Set(null, Act(SaveActionKind.Tool, "always"), Act(SaveActionKind.Tool, "manual", onlyManual: true), Act(SaveActionKind.Tool, "off", enabled: false));

        await runner.RunAsync(doc, set, SaveOrigin.Autosave, () => Save(doc), a => { ran.Add(a.Id); return Task.FromResult<string?>(null); });
        Assert.Equal(["always"], ran);

        ran.Clear();
        doc.Insert(0, "y"u8.ToArray());
        await runner.RunAsync(doc, set, SaveOrigin.Manual, () => Save(doc), a => { ran.Add(a.Id); return Task.FromResult<string?>(null); });
        Assert.Equal(["always", "manual"], ran);
    }

    [Fact]
    public async Task A_save_by_a_plugin_runs_no_action_and_a_save_inside_the_actions_cannot_start_a_loop()
    {
        using var doc = OpenModified();
        int ran = 0;
        var runner = new SaveActionRunner();
        var set = Set(null, Act(SaveActionKind.Tool, "t"));
        Task<string?> After(SaveAction a) { ran++; return Task.FromResult<string?>(null); }

        await runner.RunAsync(doc, set, SaveOrigin.Plugin, () => Save(doc), After);
        Assert.Equal(0, ran);

        // the tool itself saves the document again (a plugin that answers a save with a save): the inner save runs no actions
        doc.Insert(0, "y"u8.ToArray());
        var inner = new List<SaveActionReport>();
        var report = await runner.RunAsync(doc, set, SaveOrigin.Manual, () => Save(doc), async a =>
        {
            ran++;
            doc.Insert(0, "z"u8.ToArray());
            inner.Add(await runner.RunAsync(doc, set, SaveOrigin.Manual, () => Save(doc), After));
            return null;
        });

        Assert.True(report.Saved);
        Assert.Equal(1, ran);
        Assert.True(Assert.Single(inner).Saved);
        Assert.Empty(inner[0].Notes);
    }

    [Fact]
    public async Task A_formatter_does_not_touch_a_document_without_changes()
    {
        string path = Path.Combine(_dir, "g.txt");
        File.WriteAllText(path, "abc\n");
        using var doc = Doc.Open(path, ReferenceBackendFactory.Instance);
        var runner = new SaveActionRunner(_ => Fake("--format"));

        var report = await runner.RunAsync(doc, Set(TimeSpan.FromSeconds(60), Act(SaveActionKind.Format)), SaveOrigin.Manual, () => Save(doc), _ => Task.FromResult<string?>(null));

        Assert.Equal("abc\n", File.ReadAllText(path));
        Assert.Equal(SaveActionState.Skipped, Assert.Single(report.Notes).State);
    }

    // ---- the files ----

    [Fact]
    public void The_list_of_a_project_replaces_an_entry_of_the_profile_and_a_bad_entry_is_named()
    {
        string profile = Path.Combine(_dir, "saveActions.json");
        string project = Path.Combine(_dir, "proj", ".textida", "saveActions.json");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(profile, """
            { "formatTimeoutSeconds": 7, // a comment
              "actions": [ { "kind": "format", "id": "gofmt", "onlyManual": true }, { "kind": "upload" }, { "kind": "nonsense" }, { "kind": "tool" } ] }
            """);
        File.WriteAllText(project, """{ "formatTimeoutSeconds": 99, "actions": [ { "kind": "format", "id": "gofmt", "enabled": false } ] }""");

        var set = SaveActionConfig.Load(profile, SaveActionConfig.FindProjectFile(Path.Combine(_dir, "proj", "src")));

        Assert.Equal(TimeSpan.FromSeconds(7), set.FormatTimeout);
        Assert.Equal(2, set.Errors.Count);
        var format = Assert.Single(set.Actions, a => a.Kind == SaveActionKind.Format);
        Assert.False(format.Enabled);
        Assert.False(format.OnlyManual);
        Assert.Equal(SaveActionSource.Project, format.Source);
        Assert.Contains(set.Actions, a => a.Kind == SaveActionKind.Upload);
    }

    [Fact]
    public void Program_paths_come_from_the_profile_only_and_a_project_that_names_one_is_told_so()
    {
        string profile = Path.Combine(_dir, "saveActions.json");
        string project = Path.Combine(_dir, "proj", ".textida", "saveActions.json");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(profile, """{ "toolPaths": { "phpstan": " C:\\tools\\phpstan.phar ", "eslint": "  ", "ruff": null }, "actions": [] }""");
        File.WriteAllText(project, """{ "toolPaths": { "phpstan": "C:\\evil\\phpstan.exe", "psalm": "C:\\evil\\psalm.exe" }, "actions": [] }""");

        var set = SaveActionConfig.Load(profile, SaveActionConfig.FindProjectFile(Path.Combine(_dir, "proj", "src")));

        Assert.Equal(new Dictionary<string, string> { ["phpstan"] = "C:\\tools\\phpstan.phar" }, set.ToolPaths);
        var error = Assert.Single(set.Errors);
        Assert.Contains("toolPaths", error);
        Assert.Contains("profile", error);
        Assert.Empty(SaveActionConfig.Load(Path.Combine(_dir, "none.json"), null).ToolPaths!);
    }

    [Fact]
    public async Task A_program_path_from_the_user_is_used_for_a_formatter_and_a_path_that_does_not_exist_is_reported()
    {
        var s = PrepareFormatter("shfmt", "#format");
        using var doc = s.Doc;
        var bare = s.Def with { Executable = ConfigFormatters.Find("shfmt")!.Executable }; // the name only: the copy is not in PATH

        var wrong = await ConfigFormatters.FormatAsync(doc, bare, TimeSpan.FromSeconds(60), toolPath: Path.Combine(_dir, "nothing-here"));
        Assert.Equal(FormatResult.Missing, wrong.Result);
        Assert.Contains("does not exist", wrong.Reason);
        Assert.Equal(s.Unchanged, Text(doc));

        var outcome = await ConfigFormatters.FormatAsync(doc, bare, TimeSpan.FromSeconds(60), toolPath: Path.GetDirectoryName(s.Tool));
        Assert.Equal(FormatResult.Formatted, outcome.Result);
        Assert.Equal(s.Unchanged.ToUpperInvariant(), Text(doc));
    }

    [Fact]
    public void Switching_an_action_changes_only_its_value_and_keeps_the_comments_of_the_file()
    {
        string profile = Path.Combine(_dir, "saveActions.json");
        File.WriteAllText(profile, """
            {
              // my limit
              "formatTimeoutSeconds": 7,
              "actions": [
                { "kind": "format", "id": "gofmt", "enabled": false }, /* keep this */
                { "kind": "tool", "id": "lint" },
              ]
            }
            """);

        SaveActionConfig.Edit(profile, SaveActionKind.Format, "gofmt", e => e.Enabled = true);
        SaveActionConfig.Edit(profile, SaveActionKind.Tool, "lint", e => e.OnlyManual = true);
        SaveActionConfig.Edit(profile, SaveActionKind.Lint, "phpstan", e => e.Enabled = true);

        string text = File.ReadAllText(profile);
        Assert.Contains("// my limit", text);
        Assert.Contains("/* keep this */", text);
        Assert.Contains("\"enabled\": true }", text);
        var set = SaveActionConfig.Load(profile, null);
        Assert.Empty(set.Errors);
        Assert.Equal(TimeSpan.FromSeconds(7), set.FormatTimeout);
        Assert.Equal([("format", "gofmt", true, false), ("tool", "lint", true, true), ("lint", "phpstan", true, false)],
            set.Actions.Select(a => (a.Kind.ToString().ToLowerInvariant(), a.Id, a.Enabled, a.OnlyManual)));
    }

    [Fact]
    public void An_action_is_added_to_a_file_without_a_list_and_a_new_file_is_written()
    {
        string withoutList = Path.Combine(_dir, "a.json");
        File.WriteAllText(withoutList, "{ /* only the limit */ \"formatTimeoutSeconds\": 3 }");
        SaveActionConfig.Edit(withoutList, SaveActionKind.Format, "gofmt", e => e.Enabled = true);
        Assert.Contains("/* only the limit */", File.ReadAllText(withoutList));
        var added = Assert.Single(SaveActionConfig.Load(withoutList, null).Actions);
        Assert.Equal((SaveActionKind.Format, "gofmt", true), (added.Kind, added.Id, added.Enabled));

        string missing = Path.Combine(_dir, "sub", "b.json");
        SaveActionConfig.Edit(missing, SaveActionKind.Upload, "", e => e.Enabled = true);
        var upload = Assert.Single(SaveActionConfig.Load(missing, null).Actions);
        Assert.Equal((SaveActionKind.Upload, true), (upload.Kind, upload.Enabled));
    }

    [Fact]
    public void A_project_names_its_default_server_and_a_file_maps_to_the_same_relative_path_below_the_server_folder()
    {
        string profile = Path.Combine(_dir, "saveActions.json");
        string projectFolder = Path.Combine(_dir, "site");
        string project = Path.Combine(projectFolder, ".textida", "saveActions.json");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(profile, """{ "actions": [ { "kind": "upload", "server": "Web", "remoteFolder": "/www" } ] }""");
        File.WriteAllText(project, """{ "actions": [ { "kind": "upload", "server": "Web", "remoteFolder": "/var/www/site/" } ] }""");

        var set = SaveActionConfig.Load(profile, project);

        Assert.Contains(set.Errors, e => e.Contains("not in the profile"));
        var server = Assert.Single(set.Actions).Server!;
        Assert.Equal(("Web", "/var/www/site/", projectFolder), (server.Connection, server.RemoteFolder, server.ProjectFolder));
        Assert.Equal("/var/www/site/src/a b.php", server.RemotePathOf(Path.Combine(projectFolder, "src", "a b.php")));
        Assert.Equal("/var/www/site/index.php", server.RemotePathOf(Path.Combine(projectFolder, "index.php")));
        Assert.Null(server.RemotePathOf(Path.Combine(projectFolder, ".textida", "saveActions.json")));
        Assert.Null(server.RemotePathOf(Path.Combine(_dir, "elsewhere.php")));
        Assert.Null(server.RemotePathOf(Path.Combine(_dir, "site-other", "a.php")));
        Assert.NotEqual(server.ApprovalKey("one"), server.ApprovalKey("two"));

        File.WriteAllText(project, """{ "actions": [ { "kind": "upload", "server": "Web", "remoteFolder": "www" } ] }""");
        var relative = SaveActionConfig.Load(profile, project);
        Assert.Contains(relative.Errors, e => e.Contains("remoteFolder"));
        Assert.DoesNotContain(relative.Actions, a => a.Server is not null);
    }

    [Fact]
    public void A_damaged_file_is_named_and_the_other_file_still_counts()
    {
        string profile = Path.Combine(_dir, "saveActions.json");
        File.WriteAllText(profile, "{ not json");
        var set = SaveActionConfig.Load(profile, null);
        Assert.Single(set.Errors);
        Assert.Empty(set.Actions);
    }
}

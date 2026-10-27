using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>A test that needs the real gofmt; it is skipped where the program is not on PATH.</summary>
public sealed class GofmtFactAttribute : FactAttribute
{
    public GofmtFactAttribute()
    {
        bool installed = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(d => d.Length > 0 && (File.Exists(Path.Combine(d, "gofmt")) || File.Exists(Path.Combine(d, "gofmt.exe"))));
        if (!installed) Skip = "gofmt is not installed here.";
    }
}

/// <summary>
/// P8.3.1: the order of a save with actions, with the test program of ToolEcho as the formatter (success, a syntax error, a missing program, a timeout).
/// The formatters of the catalog (gofmt, rustfmt, clang-format, RuboCop) are not started here; the P8.5.3 table that fixes the final list does not exist yet.
/// </summary>
public sealed class SaveActionsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-saveact-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

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

    [GofmtFact]
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
    public void A_damaged_file_is_named_and_the_other_file_still_counts()
    {
        string profile = Path.Combine(_dir, "saveActions.json");
        File.WriteAllText(profile, "{ not json");
        var set = SaveActionConfig.Load(profile, null);
        Assert.Single(set.Errors);
        Assert.Empty(set.Actions);
    }
}

using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Workbench.Clips;
using NksHub.Textida.Workbench.Commands;
using AdapterCarets = NksHub.Textida.AvaloniaEditAdapter.Carets;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P4.1.1 to P4.1.4 through the running window: a data plugin with clips and templates in the plugins folder of the profile, the list with a preview, the form
/// with typed inputs, insertion at every caret as one undo step, a clip made from the selection, the user variables and a new file from a template.
/// </summary>
[Collection("LocState")]
public class ClipTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    private const string Header = "\"schemaVersion\":1,\"id\":\"acme.clips\",\"name\":\"Acme clips\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1}";

    public ClipTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _app.Clock = () => new DateTime(2026, 10, 27, 13, 5, 9);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private string PluginsDirectory => Path.Combine(_app.ProfileDirectory, "plugins");

    /// <summary>Puts a data plugin into the plugins folder of the profile and reads the folder again, as a restart would.</summary>
    private void Install(string folder, string manifest)
    {
        var dir = Path.Combine(PluginsDirectory, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, PluginManifest.FileName), manifest, Encoding.UTF8);
        _app.ReloadClips();
    }

    private static string Manifest(string contributes) => "{" + Header + ",\"contributes\":{" + contributes + "}}";

    private DocTab Open(string text)
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = text;
        Dispatcher.UIThread.RunJobs();
        tab.Doc.FlushEol();
        tab.Editor.TextArea.Focus();
        tab.Editor.CaretOffset = 0;
        return tab;
    }

    private static int Steps(DocTab t) => t.Doc.Doc.UndoDepth - 1;
    private static string Text(DocTab t) => t.Editor!.Document.Text;
    private static string Bytes(DocTab t) => Encoding.UTF8.GetString(t.Doc.Doc.ReadBytes(new ByteRange(0, t.Doc.Doc.ByteLength)));
    private static void PlaceCarets(DocTab t, params int[] offsets) => t.Editor!.SetCarets(AdapterCarets.Create([.. offsets.Select(Sel.At)], offsets.Length - 1));
    private CommandOutcome Run(string id) => _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult();
    private void Mod(Key key) => TestApp.Press(_w, key, TestApp.Mod);

    private void Pick(string title)
    {
        _w.SetPaletteQuery(title);
        _w.RunPaletteSelection();
    }

    private IReadOnlyList<Sel> CaretsOf(DocTab t) => t.Editor!.GetSelections().OrderBy(s => s.Start).ToList();

    private const string Box = "\"clips\":[{\"id\":\"box\",\"title\":\"Box\",\"text\":\"<div>$|</div>\"," +
        "\"command\":\"acme.clips.box\"}],\"commands\":[{\"id\":\"acme.clips.box\",\"title\":\"Box\"}]";

    // ---- P4.1.1 clips as data ----

    [AvaloniaFact]
    public void A_data_plugin_with_clips_installs_without_a_process_and_its_clip_goes_into_every_caret_with_a_preview()
    {
        Install("acme", Manifest(Box));
        Assert.Empty(_app.Clips.Problems);
        var entry = Assert.Single(_app.Clips.Clips);
        Assert.Equal("acme.clips", entry.PluginId);
        Assert.True(_app.Workbench.Commands.HasHandler("acme.clips.box"));
        Assert.Null(PluginManifestLoader.Load(Path.Combine(PluginsDirectory, "acme"), 1, 1 << 20).Plugin!.Spec);

        var tab = Open("a\nb\nc");
        PlaceCarets(tab, 1, 3, 5);
        int steps = Steps(tab);
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.insertClip"));
        Assert.Equal("Box", Assert.Single(_w.QuickResults).Title);
        Assert.Equal("<div></div>", _w.PalettePreviewText);
        _w.RunPaletteSelection();

        Assert.Equal("a<div></div>\nb<div></div>\nc<div></div>", Text(tab));
        Assert.Equal(Text(tab), Bytes(tab));
        Assert.Equal([6, 19, 32], CaretsOf(tab).Select(c => c.Start));
        Assert.Equal(steps + 1, Steps(tab));
        Mod(Key.Z);
        Assert.Equal("a\nb\nc", Text(tab));
        Assert.Equal([1, 3, 5], CaretsOf(tab).Select(c => c.Start));
    }

    [AvaloniaFact]
    public void A_data_plugin_brings_a_setting_under_its_name_a_key_that_loses_to_a_builtin_one_and_a_tool_command()
    {
        Install("acme", Manifest("\"commands\":[{\"id\":\"acme.clips.run\",\"title\":\"Run\"}]," +
            "\"keybindings\":[{\"key\":\"mod+s\",\"command\":\"acme.clips.run\"}]," +
            "\"settings\":[{\"key\":\"acme.clips.width\",\"type\":\"integer\",\"default\":7,\"title\":\"Width\"}]," +
            "\"externalTools\":[{\"id\":\"echo\",\"title\":\"Echo\",\"executable\":\"echo\",\"command\":\"acme.clips.run\"}]"));
        Assert.Empty(_app.Clips.Problems);

        var wb = _app.Workbench;
        Assert.Equal(7, wb.Settings.GetInt("acme.clips.width"));
        Assert.Contains(wb.SearchIndex.Search("Acme clips", wb.Settings, wb.Commands, wb.Keymap), h => h.Id == "acme.clips.width" && h.Category == "Acme clips");
        var conflict = Assert.Single(wb.KeyConflictsOf("acme.clips"));
        Assert.Equal("nks.file.save", conflict.Winner.CommandId);
        Assert.NotNull(_app.Clips.FindTool("acme.clips", "echo"));
        Assert.True(wb.Commands.HasHandler("acme.clips.run"));
    }

    [AvaloniaFact]
    public void A_snippet_clip_indents_later_lines_selects_the_first_stop_mirrors_it_and_tab_walks_to_the_end()
    {
        Install("acme", Manifest("\"clips\":[{\"id\":\"sn\",\"title\":\"Sn\",\"text\":\"${1:foo} = $1;\\n$2 end\"}]"));
        var tab = Open("  x");
        tab.Editor!.CaretOffset = 3;
        Run("nks.edit.insertClip");
        _w.RunPaletteSelection();
        Assert.Equal("  xfoo = foo;\n   end", Text(tab).Replace("\r\n", "\n"));
        Assert.True(tab.Editor.InSnippet);
        Assert.Equal(new Sel(3, 6), CaretsOf(tab).Single());

        tab.Editor.TypeText("bar");
        Assert.Equal("  xbar = bar;\n   end", Text(tab).Replace("\r\n", "\n"));

        Assert.Equal(CommandOutcome.Handled, Run("nks.snippet.next"));
        Assert.Equal(Sel.At(Text(tab).IndexOf("end", StringComparison.Ordinal) - 1), CaretsOf(tab).Single());
        Assert.Equal(CommandOutcome.Handled, Run("nks.snippet.next"));
        Assert.False(tab.Editor.InSnippet);
        Assert.Equal(Text(tab).Length, tab.Editor.CaretOffset);
    }

    [AvaloniaFact]
    public void A_snippet_mirror_with_a_transform_shows_the_changed_text_of_its_stop_while_it_is_typed()
    {
        Install("acme", Manifest("""
            "clips":[{"id":"tf","title":"Tf","text":"${1:one_two} ${1/(\\w+)_(\\w+)/${1:/camelcase}${2:/pascalcase}/}"}]
            """));
        var tab = Open("");
        Run("nks.edit.insertClip");
        _w.RunPaletteSelection();
        Assert.Equal("one_two oneTwo", Text(tab));
        Assert.Equal(new Sel(0, 7), CaretsOf(tab).Single());

        tab.Editor!.TypeText("foo_bar");
        Assert.Equal("foo_bar fooBar", Text(tab));
        tab.Editor.TypeText("x");
        Assert.Equal("foo_barx fooBarx", Text(tab));
    }

    [AvaloniaFact]
    public void A_stop_inside_a_default_is_visited_after_its_outer_stop_and_the_mirrors_of_the_outer_stop_follow_it()
    {
        Install("acme", Manifest("""
            "clips":[{"id":"nest","title":"Nest","text":"<a ${1:href=\"${2:#}\"}>$0 | $1"}]
            """));
        var tab = Open("");
        Run("nks.edit.insertClip");
        _w.RunPaletteSelection();
        Assert.Equal("<a href=\"#\"> | href=\"#\"", Text(tab));
        Assert.Equal(new Sel(3, 11), CaretsOf(tab).Single());

        Assert.Equal(CommandOutcome.Handled, Run("nks.snippet.next"));
        Assert.Equal(new Sel(9, 10), CaretsOf(tab).Single());
        tab.Editor!.TypeText("/x");
        Assert.Equal("<a href=\"/x\"> | href=\"/x\"", Text(tab));

        Assert.Equal(CommandOutcome.Handled, Run("nks.snippet.next"));
        Assert.False(tab.Editor.InSnippet);
        Assert.Equal(Sel.At(Text(tab).IndexOf(">", StringComparison.Ordinal) + 1), CaretsOf(tab).Single());
    }

    [AvaloniaFact]
    public void A_clip_with_a_transform_that_runs_too_long_is_refused_with_a_message_and_writes_nothing()
    {
        // the slow text is the selection, so the plugin is read without a complaint and only the insertion meets the transform
        Install("acme", Manifest("""
            "clips":[{"id":"slow","title":"Slow","text":"a ${selection/^(x+)+$/_/}"}]
            """));
        const string slow = "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx!";
        Assert.Empty(_app.Clips.Problems);
        var tab = Open(slow);
        tab.Editor!.Select(0, slow.Length);
        Run("nks.edit.insertClip");
        _w.RunPaletteSelection();
        Assert.Equal(slow, Text(tab));
        Assert.False(tab.Editor.InSnippet);
        Assert.Contains(Loc.Get(K.ClipBadText, ("reason", "")).Split(':')[0], _w.StatusLeftText);
    }

    [AvaloniaFact]
    public void A_clip_whose_default_text_makes_a_transform_run_too_long_is_refused_when_the_plugin_is_read()
    {
        Install("acme", Manifest("""
            "clips":[{"id":"slow","title":"Slow","text":"${1:xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx!} ${1/^(x+)+$/_/}"}]
            """));
        Assert.Contains("took too long", Assert.Single(_app.Clips.Problems));
        Assert.Empty(_app.Clips.Clips);
        var tab = Open("keep");
        Run("nks.edit.insertClip");
        Assert.Equal("keep", Text(tab));
    }

    [AvaloniaFact]
    public void The_command_a_clip_names_inserts_it_and_a_selection_is_replaced()
    {
        Install("acme", Manifest(Box));
        var tab = Open("x SEL y");
        tab.Editor!.Select(2, 3);
        Assert.Equal(CommandOutcome.Handled, Run("acme.clips.box"));
        Assert.Equal("x <div></div> y", Text(tab));
        Assert.Equal(7, tab.Editor.CaretOffset);
    }

    [AvaloniaFact]
    public void A_clip_can_have_several_carets_and_each_selection_gets_its_own_selection_text_and_guid()
    {
        Install("acme", Manifest("\"clips\":[{\"id\":\"wrap\",\"title\":\"Wrap\",\"text\":\"[$|${selection}$|] ${guid}\"}]"));
        var tab = Open("ab cd");
        tab.Editor!.SetCarets(AdapterCarets.Create([new Sel(0, 2), new Sel(3, 5)], 1));
        Run("nks.edit.insertClip");
        _w.RunPaletteSelection();
        var text = Text(tab);
        var m = System.Text.RegularExpressions.Regex.Match(text, @"^\[ab\] ([0-9a-f-]{36}) \[cd\] ([0-9a-f-]{36})$");
        Assert.True(m.Success, text);
        Assert.NotEqual(m.Groups[1].Value, m.Groups[2].Value);
        Assert.Equal(4, tab.Editor.GetSelections().Count);
    }

    [AvaloniaFact]
    public void A_damaged_or_process_plugin_is_left_out_and_the_reason_is_shown()
    {
        Install("broken", "{ not json");
        Install("proc", "{" + Header.Replace("acme.clips", "acme.proc") + ",\"main\":{\"executable\":\"" + Environment.ProcessPath!.Replace("\\", "\\\\") + "\"}," +
            "\"contributes\":{\"commands\":[{\"id\":\"acme.proc.run\",\"title\":\"Run\"}]}}");
        Assert.Equal(2, _app.Clips.Problems.Count);
        Assert.Empty(_app.Clips.Clips);
        Open("t");
        Run("nks.edit.insertClip");
        Assert.Contains(Loc.Get(K.ClipLibraryProblem, ("reason", "")).Split(':')[0], _w.StatusLeftText);
        Assert.Contains(Loc.Get(K.ClipsEmpty), _w.PaletteEmptyText);
    }

    [AvaloniaFact]
    public void A_clip_is_refused_with_a_bad_template_a_bad_field_or_an_unknown_command()
    {
        string One(string clip) => "\"clips\":[" + clip + "]";
        var cases = new (string Clip, string Expect)[]
        {
            ("{\"id\":\"a\",\"title\":\"A\",\"text\":\"${oops\"}", "unclosed"),
            ("{\"id\":\"a\",\"title\":\"A\",\"text\":\"x\",\"fields\":[{\"name\":\"n\",\"type\":\"banana\"}]}", "unknown type"),
            ("{\"id\":\"a\",\"title\":\"A\",\"text\":\"x\",\"fields\":[{\"name\":\"n\",\"type\":\"choice\"}]}", "needs choices"),
            ("{\"id\":\"a\",\"title\":\"A\",\"text\":\"x\",\"fields\":[{\"name\":\"n\"},{\"name\":\"n\"}]}", "twice"),
            ("{\"id\":\"a\",\"title\":\"A\",\"text\":\"x\",\"command\":\"acme.clips.none\"}", "unknown command"),
        };
        foreach (var (clip, expect) in cases)
        {
            var result = PluginManifestLoader.Load(WriteTemp(Manifest(One(clip))), 1, 1 << 20);
            Assert.False(result.Succeeded, clip);
            Assert.Contains(result.Errors, e => e.Contains(expect, StringComparison.OrdinalIgnoreCase));
        }
    }

    private string WriteTemp(string manifest)
    {
        var dir = Path.Combine(_temp.Path, "m" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, PluginManifest.FileName), manifest);
        return dir;
    }

    // ---- P4.1.2 interactive clips and clip from a block ----

    private const string Colored = "\"clips\":[{\"id\":\"c\",\"title\":\"Colored\",\"text\":\"color: ${color}; size: ${size}; on ${when}\"," +
        "\"fields\":[{\"name\":\"color\",\"label\":\"Colour\",\"type\":\"color\",\"default\":\"#112233\"}," +
        "{\"name\":\"size\",\"type\":\"integer\",\"default\":\"12\"}," +
        "{\"name\":\"when\",\"type\":\"date\",\"default\":\"${date}\"}]}]";

    [AvaloniaFact]
    public void A_clip_form_starts_with_the_defaults_and_a_valid_answer_is_inserted_as_one_step()
    {
        Install("acme", Manifest(Colored));
        var tab = Open("x");
        PlaceCarets(tab, 0, 1);
        _dialogs.ClipForms.Enqueue(new Dictionary<string, string> { ["color"] = "#ABC", ["size"] = "-4", ["when"] = "2026-02-28" });
        int steps = Steps(tab);
        Run("nks.edit.insertClip");
        Pick("Colored");

        Assert.Equal("2026-10-27", _dialogs.ClipInitial.Single()["when"]);
        Assert.Equal("#112233", _dialogs.ClipInitial.Single()["color"]);
        Assert.Empty(_dialogs.ClipStates.Single().Problems);
        Assert.Equal("color: #ABC; size: -4; on 2026-02-28", _dialogs.ClipStates.Single().Preview);
        Assert.Equal("color: #ABC; size: -4; on 2026-02-28xcolor: #ABC; size: -4; on 2026-02-28", Text(tab));
        Assert.Equal(steps + 1, Steps(tab));
        Mod(Key.Z);
        Assert.Equal("x", Text(tab));
    }

    [AvaloniaTheory]
    [InlineData("color", "red", FieldProblem.NotColor)]
    [InlineData("color", "#12345", FieldProblem.NotColor)]
    [InlineData("color", "", FieldProblem.Empty)]
    [InlineData("size", "1.5", FieldProblem.NotInteger)]
    [InlineData("size", "ten", FieldProblem.NotInteger)]
    [InlineData("when", "27.10.2026", FieldProblem.NotDate)]
    [InlineData("when", "2026-02-30", FieldProblem.NotDate)]
    public void A_clip_form_refuses_an_invalid_value_and_changes_nothing(string field, string value, FieldProblem expected)
    {
        Install("acme", Manifest(Colored));
        var tab = Open("x");
        var answer = new Dictionary<string, string> { ["color"] = "#ABC", ["size"] = "4", ["when"] = "2026-02-28" };
        answer[field] = value;
        _dialogs.ClipForms.Enqueue(answer);
        int steps = Steps(tab);
        Run("nks.edit.insertClip");
        Pick("Colored");

        Assert.Equal(expected, _dialogs.ClipStates.Single().Problems[field]);
        Assert.Equal("x", Text(tab));
        Assert.Equal(steps, Steps(tab));
        Assert.Contains(ClipTexts.Problem(expected), _w.StatusLeftText);
    }

    [AvaloniaFact]
    public void A_cancelled_form_changes_nothing_and_choice_and_file_fields_are_checked()
    {
        Install("acme", Manifest("\"clips\":[{\"id\":\"p\",\"title\":\"Pick\",\"text\":\"${kind}:${path}\"," +
            "\"fields\":[{\"name\":\"kind\",\"type\":\"choice\",\"choices\":[\"a\",\"b\"]},{\"name\":\"path\",\"type\":\"file\"}]}]"));
        var tab = Open("x");
        Run("nks.edit.insertClip");
        Pick("Pick");
        Assert.Equal("a", _dialogs.ClipInitial.Single()["kind"]);
        Assert.Equal("x", Text(tab));

        var file = _temp.File("real.txt", "1");
        _dialogs.ClipForms.Enqueue(new Dictionary<string, string> { ["kind"] = "c", ["path"] = Path.Combine(_temp.Path, "missing.txt") });
        Run("nks.edit.insertClip");
        Pick("Pick");
        var problems = _dialogs.ClipStates.Single().Problems;
        Assert.Equal(FieldProblem.NotChoice, problems["kind"]);
        Assert.Equal(FieldProblem.NoFile, problems["path"]);
        Assert.Equal("x", Text(tab));

        _dialogs.ClipForms.Enqueue(new Dictionary<string, string> { ["kind"] = "b", ["path"] = file });
        Run("nks.edit.insertClip");
        Pick("Pick");
        Assert.Equal("b:" + file + "x", Text(tab));
    }

    [AvaloniaFact]
    public async Task The_real_form_marks_a_wrong_value_keeps_OK_dimmed_shows_the_preview_and_returns_the_values()
    {
        var dialogs = new AvaloniaDialogs();
        var fields = new List<ClipField>
        {
            new() { Name = "color", Label = "Colour", Type = "color", Default = "#112233" },
            new() { Name = "size", Type = "integer" },
        };
        bool okWhenWrong = true, okWhenRight = false;
        string noteWhenWrong = "", previewWhenRight = "";
        dialogs.Shown = w => Dispatcher.UIThread.Post(() =>
        {
            var color = w.GetVisualDescendants().OfType<TextBox>().First(t => AutomationProperties.GetName(t) == "Colour");
            var size = w.GetVisualDescendants().OfType<TextBox>().First(t => AutomationProperties.GetName(t) == "size");
            var ok = w.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == Loc.Get(K.DialogOk));
            color.Text = "red";
            size.Text = "5";
            Dispatcher.UIThread.RunJobs();
            okWhenWrong = ok.IsEnabled;
            noteWhenWrong = w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == ClipTexts.Problem(FieldProblem.NotColor)).Text!;
            color.Text = "#FF0000";
            Dispatcher.UIThread.RunJobs();
            okWhenRight = ok.IsEnabled;
            previewWhenRight = w.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text?.StartsWith("c=") == true).Text!;
            ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, DispatcherPriority.Background);

        var result = await dialogs.ClipFormAsync(_w, "Clip", fields, ClipFields.Defaults(fields, new VariableScope(new VariableContext())), values =>
            new ClipFormState(ClipFields.Validate(fields, values), $"c={values["color"]} s={values["size"]}", null));

        Assert.False(okWhenWrong);
        Assert.Equal(ClipTexts.Problem(FieldProblem.NotColor), noteWhenWrong);
        Assert.True(okWhenRight);
        Assert.Equal("c=#FF0000 s=5", previewWhenRight);
        Assert.Equal("#FF0000", result!["color"]);
        Assert.Equal("5", result["size"]);
    }

    [AvaloniaFact]
    public void A_selection_saved_as_a_clip_is_in_the_list_at_once_is_kept_literally_and_inserts_as_one_step()
    {
        var tab = Open("keep $x and ${y} $| here\nnext");
        tab.Editor!.Select(5, 18);
        string chosen = tab.Editor.SelectedText;
        _dialogs.Prompts.Enqueue("My snippet");
        Assert.Equal(CommandOutcome.Handled, Run("nks.edit.clipFromSelection"));
        Assert.Contains("My snippet", _w.StatusLeftText);

        var entry = Assert.Single(_app.Clips.Clips);
        Assert.Equal(ClipLibrary.UserPluginId, entry.PluginId);
        Assert.Equal("my-snippet", entry.Clip.Id);
        Assert.Equal(Variables.Escape(chosen), entry.Clip.Text);
        Assert.True(File.Exists(_app.Clips.UserManifestPath));
        Assert.True(PluginManifestLoader.Load(Path.GetDirectoryName(_app.Clips.UserManifestPath)!, 1, 1 << 20).Succeeded);

        PlaceCarets(tab, tab.Editor.Document.TextLength);
        int steps = Steps(tab);
        Run("nks.edit.insertClip");
        Assert.Equal("My snippet", Assert.Single(_w.QuickResults).Title);
        _w.RunPaletteSelection();
        Assert.Equal("keep $x and ${y} $| here\nnext" + chosen, Text(tab));
        Assert.Equal(steps + 1, Steps(tab));

        _dialogs.Prompts.Enqueue("My snippet");
        tab.Editor.Select(0, 4);
        Run("nks.edit.clipFromSelection");
        Assert.Equal(["my-snippet", "my-snippet-2"], _app.Clips.Clips.Select(c => c.Clip.Id));
    }

    [AvaloniaFact]
    public void Saving_without_a_selection_or_a_name_saves_nothing()
    {
        var tab = Open("text");
        Assert.NotEqual(CommandOutcome.Handled, Run("nks.edit.clipFromSelection"));
        _w.SaveSelectionAsClip();
        Assert.Contains(Loc.Get(K.ClipNoSelection), _w.StatusLeftText);
        tab.Editor!.Select(0, 2);
        _dialogs.Prompts.Enqueue("   ");
        Run("nks.edit.clipFromSelection");
        Assert.Empty(_app.Clips.Clips);
        Assert.False(File.Exists(_app.Clips.UserManifestPath));
    }

    [AvaloniaFact]
    public void A_damaged_user_library_is_not_overwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_app.Clips.UserManifestPath)!);
        File.WriteAllText(_app.Clips.UserManifestPath, "{ not json");
        var tab = Open("text");
        tab.Editor!.Select(0, 2);
        _dialogs.Prompts.Enqueue("Name");
        Run("nks.edit.clipFromSelection");
        Assert.Equal("{ not json", File.ReadAllText(_app.Clips.UserManifestPath));
        Assert.Contains(Loc.Get(K.ClipSaveFailed, ("reason", "")).Split(':')[0], _w.StatusLeftText);
    }

    // ---- P4.1.3 user variables ----

    private void WriteVariables(string json) => File.WriteAllText(Path.Combine(_app.ProfileDirectory, UserVariables.FileName), json);

    [AvaloniaFact]
    public void A_variable_comes_from_the_form_then_the_user_variables_then_the_built_in_ones()
    {
        Install("acme", Manifest("\"clips\":[{\"id\":\"who\",\"title\":\"Who\",\"text\":\"${author}|${date}|${file.name}\"}," +
            "{\"id\":\"ask\",\"title\":\"Ask\",\"text\":\"${author}|${date}\",\"fields\":[{\"name\":\"author\",\"default\":\"form default\"}]}]"));
        WriteVariables("{\"author\":\"Ann\",\"date\":\"my date\"}");
        var path = _temp.File("note.txt", "x");
        var tab = _w.OpenFile(path, null);
        tab.Editor!.TextArea.Focus();
        tab.Editor.CaretOffset = 0;

        Run("nks.edit.insertClip");
        Pick("Who");
        Assert.Equal("Ann|my date|note.txtx", Text(tab));

        tab.Editor.CaretOffset = 0;
        _dialogs.ClipForms.Enqueue(new Dictionary<string, string> { ["author"] = "Bob" });
        Run("nks.edit.insertClip");
        Pick("Ask");
        Assert.Equal("Bob|my dateAnn|my date|note.txtx", Text(tab));
    }

    [AvaloniaFact]
    public void A_variable_without_a_value_stops_the_insertion_and_a_damaged_variables_file_is_reported()
    {
        Install("acme", Manifest("\"clips\":[{\"id\":\"f\",\"title\":\"File\",\"text\":\"${file.name} ${nobody}\"}]"));
        var tab = Open("x");
        Run("nks.edit.insertClip");
        Pick("File");
        Assert.Equal("x", Text(tab));
        Assert.Contains("file.name", _w.StatusLeftText);
        Assert.Contains("nobody", _w.StatusLeftText);

        WriteVariables("{ not json");
        Run("nks.edit.insertClip");
        Pick("File");
        Assert.Equal("x", Text(tab));
        Assert.Contains(Loc.Get(K.ClipVariablesBroken, ("reason", "")).Split(':')[0], _w.StatusLeftText);
    }

    [AvaloniaFact]
    public void A_variable_gives_a_tool_argument_the_value_it_gives_a_clip()
    {
        const string template = "${project}|${date}|${selection}|${user.name}|${ file.name }|${datetime}";
        Install("acme", Manifest("\"clips\":[{\"id\":\"c\",\"title\":\"Clip\",\"text\":\"" + template + "\"}]"));
        WriteVariables("{\"project\":\"My Project\",\"date\":\"my date\"}");
        var path = _temp.File("note.txt", "part");
        var tab = _w.OpenFile(path, null);
        tab.Editor!.TextArea.Focus();
        tab.Editor.Select(0, 4);

        var tool = _w.ResolveTool(new ToolSpec("t", ToolTestKit.Dotnet, [template, "${File}"]));
        Run("nks.edit.insertClip");
        Pick("Clip");

        Assert.StartsWith("My Project|my date|part|", Text(tab));
        Assert.Equal([Text(tab), Path.GetFullPath(path)], tool.Args);
    }

    [AvaloniaFact]
    public void A_tool_argument_with_a_variable_that_has_no_value_stops_the_tool_like_a_clip_and_a_user_variable_gives_it_one()
    {
        var unsaved = Open("x");
        var spec = new ToolSpec("t", ToolTestKit.Dotnet, ["${file.dir}|${author}"]);
        WriteVariables("{\"author\":\"Ann\"}");
        var refused = Assert.Throws<ToolException>(() => _w.ResolveTool(spec, unsaved));
        Assert.Contains("file.dir", refused.Message, StringComparison.Ordinal);

        var path = _temp.File("note.txt", "x");
        var saved = _w.OpenFile(path, null);
        Assert.Equal([Path.GetDirectoryName(Path.GetFullPath(path)) + "|Ann"], _w.ResolveTool(spec, saved).Args);

        WriteVariables("{\"author\":\"Ann\",\"file.dir\":\"mine\"}");
        Assert.Equal(["mine|Ann"], _w.ResolveTool(spec, unsaved).Args);
    }

    [AvaloniaFact]
    public void The_variables_command_creates_the_file_and_opens_it_in_the_editor()
    {
        var path = Path.Combine(_app.ProfileDirectory, UserVariables.FileName);
        Assert.False(File.Exists(path));
        Assert.Equal(CommandOutcome.Handled, Run("nks.clips.editVariables"));
        Assert.True(File.Exists(path));
        Assert.Equal(path, _w.Active!.Path);
        Assert.Empty(UserVariables.Load(path).Values);
        Assert.Null(UserVariables.Load(path).Problem);
    }

    // ---- P8.2.1 variables of a project ----

    /// <summary>A folder of a project with a variables file and a file in it; returns the path of the file.</summary>
    private string Project(string json, string name = "proj")
    {
        string root = Directory.CreateDirectory(Path.Combine(_temp.Path, name)).FullName;
        string folder = Directory.CreateDirectory(Path.Combine(root, ProjectVariables.FolderName)).FullName;
        File.WriteAllText(Path.Combine(folder, UserVariables.FileName), json);
        return _temp.File(Path.Combine(name, "note.txt"), "x");
    }

    [AvaloniaFact]
    public void A_variable_of_the_project_wins_over_the_user_variable_in_a_clip_and_a_tool_but_only_in_that_project()
    {
        Install("acme", Manifest("\"clips\":[{\"id\":\"c\",\"title\":\"Clip\",\"text\":\"${author}|${client}\"}]"));
        WriteVariables("{\"author\":\"Ann\",\"client\":\"user client\"}");
        string inProject = Project("{\"client\":\"Acme\"}");
        var tab = _w.OpenFile(inProject, null);
        tab.Editor!.TextArea.Focus();
        tab.Editor.CaretOffset = 0;

        Run("nks.edit.insertClip");
        Pick("Clip");
        Assert.Equal("Ann|Acmex", Text(tab));
        Assert.Equal(["Ann|Acme"], _w.ResolveTool(new ToolSpec("t", ToolTestKit.Dotnet, ["${author}|${client}"]), tab).Args);

        var outside = _w.OpenFile(_temp.File("outside.txt", "y"), null);
        Assert.Equal(["Ann|user client"], _w.ResolveTool(new ToolSpec("t", ToolTestKit.Dotnet, ["${author}|${client}"]), outside).Args);
    }

    [AvaloniaFact]
    public void A_damaged_variables_file_of_the_project_stops_a_clip_and_names_the_file()
    {
        Install("acme", Manifest("\"clips\":[{\"id\":\"c\",\"title\":\"Clip\",\"text\":\"hi\"}]"));
        string file = Project("{ not json");
        var tab = _w.OpenFile(file, null);
        tab.Editor!.TextArea.Focus();
        tab.Editor.CaretOffset = 0;

        Run("nks.edit.insertClip");
        Pick("Clip");

        Assert.Equal("x", Text(tab));
        Assert.Contains(Loc.Get(K.ClipVariablesBroken, ("reason", "")).Split(':')[0], _w.StatusLeftText);
        Assert.Contains(Path.Combine(ProjectVariables.FolderName, UserVariables.FileName), _w.StatusLeftText);
    }

    [AvaloniaFact]
    public void The_project_variables_command_opens_the_file_of_the_project_and_creates_it_in_the_root_when_there_is_none()
    {
        // a project that has the file: that file is opened and left as it is
        string existing = Project("{\"a\":\"1\"}", "has");
        _w.OpenFile(existing, null);
        Assert.Equal(CommandOutcome.Handled, Run("nks.clips.editProjectVariables"));
        string file = Path.Combine(_temp.Path, "has", ProjectVariables.FolderName, UserVariables.FileName);
        Assert.Equal(file, _w.Active!.Path);
        Assert.Equal("{\"a\":\"1\"}", File.ReadAllText(file));

        // a project root (a .git folder) without it: the file is created there, empty and valid
        string root = Directory.CreateDirectory(Path.Combine(_temp.Path, "git-project")).FullName;
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        string deep = Directory.CreateDirectory(Path.Combine(root, "src", "deep")).FullName;
        _w.OpenFile(_temp.File(Path.Combine("git-project", "src", "deep", "a.txt"), "z"), null);
        Assert.Equal(CommandOutcome.Handled, Run("nks.clips.editProjectVariables"));
        string created = Path.Combine(root, ProjectVariables.FolderName, UserVariables.FileName);
        Assert.Equal(created, _w.Active!.Path);
        Assert.Empty(UserVariables.Load(created).Values);
        Assert.Null(UserVariables.Load(created).Problem);
        Assert.False(Directory.Exists(Path.Combine(deep, ProjectVariables.FolderName)));

        // no project above the file, and no file at all: nothing is created
        var outside = _w.OpenFile(_temp.File("loose.txt", "q"), null);
        Run("nks.clips.editProjectVariables");
        Assert.Equal(outside.Path, _w.Active!.Path);
        Assert.Equal(Loc.Get(K.ProjectVariablesNone), _w.StatusMessageText);
    }

    // ---- P4.1.4 file templates ----

    private const string Page = "\"templates\":[{\"id\":\"page\",\"title\":\"Page\",\"text\":\"<h1>${title}</h1>\\n<p>$|</p>\\n<footer>$|</footer>\"," +
        "\"fields\":[{\"name\":\"title\",\"default\":\"Untitled\"}]},{\"id\":\"plain\",\"title\":\"Plain\",\"text\":\"hello\"}]";

    [AvaloniaFact]
    public void New_from_a_template_opens_a_tab_with_its_text_and_the_carets_on_its_marks()
    {
        Install("acme", Manifest(Page));
        int tabs = _w.Tabs.Count();
        _dialogs.ClipForms.Enqueue(new Dictionary<string, string> { ["title"] = "Home" });
        Assert.Equal(CommandOutcome.Handled, Run("nks.file.newFromTemplate"));
        Assert.Equal(["Page", "Plain"], _w.QuickResults.Select(r => r.Title));
        Pick("Page");

        Assert.Equal(tabs + 1, _w.Tabs.Count());
        var tab = _w.Active!;
        var nl = tab.Doc.NewLine(tab.Surface!.Settings.ConfiguredEol);
        Assert.Equal("<h1>Home</h1>" + nl + "<p></p>" + nl + "<footer></footer>", Text(tab));
        Assert.Equal([("<h1>Home</h1>" + nl + "<p>").Length, ("<h1>Home</h1>" + nl + "<p></p>" + nl + "<footer>").Length], CaretsOf(tab).Select(c => c.Start));
        Assert.Null(tab.Path);
    }

    [AvaloniaFact]
    public void A_template_without_a_mark_puts_the_caret_at_the_start_and_one_with_unknown_variables_opens_nothing()
    {
        Install("acme", Manifest(Page + ",\"clips\":[]").Replace("\"templates\":[", "\"templates\":[{\"id\":\"named\",\"title\":\"Named\",\"text\":\"${file.name}\"},"));
        Run("nks.file.newFromTemplate");
        Pick("Plain");
        Assert.Equal("hello", Text(_w.Active!));
        Assert.Equal(0, _w.Active!.Editor!.CaretOffset);

        int tabs = _w.Tabs.Count();
        Run("nks.file.newFromTemplate");
        Pick("Named");
        Assert.Equal(tabs, _w.Tabs.Count());
        Assert.Contains("file.name", _w.StatusLeftText);
    }

    [AvaloniaFact]
    public void No_templates_says_so()
    {
        Run("nks.file.newFromTemplate");
        Assert.Empty(_w.QuickResults);
        Assert.Contains(Loc.Get(K.TemplatesEmpty), _w.PaletteEmptyText);
    }

    // ---- the closed list ----

    [AvaloniaFact]
    public void The_four_commands_have_a_handler_a_title_in_every_language_and_a_place_in_a_menu()
    {
        string[] ids = ["nks.edit.insertClip", "nks.edit.clipFromSelection", "nks.clips.editVariables", "nks.file.newFromTemplate"];
        var wb = _app.Workbench;
        var inMenus = wb.Commands.CommandsInMenus();
        foreach (var id in ids)
        {
            Assert.True(wb.Commands.TryGet(id, out var command), id);
            Assert.True(wb.Commands.HasHandler(id), id);
            Assert.Contains(id, inMenus);
            Assert.NotEqual(command.TitleKey, wb.Strings.Get(command.TitleKey));
        }
    }

    [AvaloniaFact]
    public void A_read_only_editor_refuses_a_clip()
    {
        Install("acme", Manifest(Box));
        var tab = Open("x");
        tab.Editor!.IsReadOnly = true;
        Assert.Equal(CommandOutcome.NotHandled, Run("nks.edit.insertClip"));
        Assert.Equal(CommandOutcome.NotHandled, Run("acme.clips.box"));
        Assert.Equal("x", Text(tab));
    }
}

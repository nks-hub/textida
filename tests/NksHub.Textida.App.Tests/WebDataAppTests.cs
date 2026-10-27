using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Rendering;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Colors;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>P4.6.1 and P4.6.4 in the app: colours with a swatch, a picker and a conversion plugin, and the key paths of YAML and JSON through the web data plugin.</summary>
[Collection("LocState")]
public class WebDataAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public WebDataAppTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private DocTab Open(string name, string content)
    {
        var tab = _w.OpenFile(_temp.File(name, content), null);
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        return tab;
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < 30_000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private static async Task<T> Pump<T>(Task<T> task)
    {
        await Until(() => task.IsCompleted, "a call");
        return await task;
    }

    private async Task RunAsync(string command)
    {
        var run = _app.Workbench.Commands.ExecuteAsync(command, new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => run.IsCompleted, command);
        Assert.Equal(CommandOutcome.Handled, await run);
        await Until(() => !_app.Plugins.IsBusy, "the plugin command to finish");
    }

    private static string Utf8(DocTab tab) => Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength)));

    // ---- colours ----

    private static List<(int Line, InlineObjectElement Swatch)> Swatches(DocTab tab)
    {
        var view = tab.Editor!.TextArea.TextView;
        view.EnsureVisualLines();
        return [.. view.VisualLines.SelectMany(l => l.Elements.OfType<InlineObjectElement>().Select(e => (l.FirstDocumentLine.LineNumber, e)))];
    }

    [AvaloniaFact]
    public void A_colour_in_css_has_a_swatch_in_its_colour_and_the_text_stays_as_it_is()
    {
        const string css = "a { color: #ff8800; }\nb { x: rgb(0 0 255 / 50%); y: 12px }\n";
        var tab = Open("a.css", css);
        var swatches = Swatches(tab);
        Assert.Equal([1, 2], swatches.Select(s => s.Line));
        Assert.All(swatches, s => Assert.Equal(0, s.Swatch.DocumentLength));
        var orange = (SolidColorBrush)((Avalonia.Controls.Border)swatches[0].Swatch.Element).Background!;
        Assert.Equal(Color.FromRgb(255, 136, 0), orange.Color);
        Assert.Equal(css, tab.Editor!.Document.Text);
        Assert.Equal(css, Utf8(tab));
        Assert.Empty(Swatches(Open("a.txt", css)));
    }

    [AvaloniaFact]
    public void The_picker_writes_the_new_colour_in_the_notation_of_the_old_one_as_one_undo_step()
    {
        const string css = "a { color: #FF8800; b: rgb(0 0 255); c: oklch(0.7 0.1 50) }";
        var tab = Open("a.css", css);
        var surface = tab.Editor!;
        var hex = ColorLiterals.Find(css)[0];
        Assert.True(surface.ReplaceColor(hex.Start, "#FF8800", hex, new Rgba(0x12, 0xAB, 0xEF)));
        Assert.Equal(css.Replace("#FF8800", "#12ABEF"), surface.Document.Text);
        _w.Undo();
        Assert.Equal(css, surface.Document.Text);

        var rgb = ColorLiterals.Find(css)[1];
        Assert.True(surface.ReplaceColor(rgb.Start, "rgb(0 0 255)", rgb, new Rgba(255, 0, 0, 0.5)));
        Assert.Contains("rgb(255 0 0 / 0.5)", surface.Document.Text);

        // the command opens the picker on the value at the caret, and nowhere else
        surface.CaretOffset = css.IndexOf("0 0 255", StringComparison.Ordinal);
        Assert.True(surface.PickColorAtCaret());
        surface.CaretOffset = css.IndexOf("a {", StringComparison.Ordinal);
        Assert.False(surface.PickColorAtCaret());

        // the text under the picker was edited meanwhile: the colour is not written over something else
        surface.Document.Replace(hex.Start, 1, "x");
        Assert.False(surface.ReplaceColor(hex.Start, "#FF8800", hex, new Rgba(1, 2, 3)));
    }

    [AvaloniaFact]
    public void The_picker_panel_keeps_sliders_preview_and_value_in_step()
    {
        var match = ColorLiterals.Find("#ff8800")[0];
        var panel = new ColorPickerPanel(match);
        Assert.Equal(new Rgba(255, 136, 0), panel.Color);
        Assert.Equal("#ff8800", panel.ValueBox.Text);

        panel.SetSliders(240, 100, 50, 100);
        Assert.Equal(new Rgba(0, 0, 255), panel.Color);
        Assert.Equal("#0000ff", panel.ValueBox.Text);

        panel.ValueBox.Text = "oklch(0.62796 0.25768 29.23)";
        Dispatcher.UIThread.RunJobs(); // the text box raises its change a moment later
        Assert.Equal(new Rgba(255, 0, 0), panel.Color);
        Assert.Equal(0, panel.Sliders.Hue, 0);
        Assert.Equal("oklch(0.62796 0.25768 29.23)", panel.ValueBox.Text); // the box is left alone while the user types

        panel.SetSliders(0, 100, 50, 50);
        Assert.Equal(new Rgba(255, 0, 0, 0.5), panel.Color);
        Assert.Equal("#ff000080", panel.ValueBox.Text);
    }

    [AvaloniaFact]
    public async Task Converting_a_colour_to_oklch_keeps_the_colour_and_undo_brings_the_old_text_back_in_one_step()
    {
        const string css = "a { color: #ff8800; }";
        var tab = Open("a.css", css);
        tab.Editor!.CaretOffset = css.IndexOf("ff8800", StringComparison.Ordinal) + 2;

        await RunAsync("nks.tools.colorToOklch");
        var converted = tab.Editor.Document.Text;
        var literal = ColorLiterals.Find(converted).Single();
        Assert.Equal(ColorFormat.Oklch, literal.Format);
        Assert.Equal(new Rgba(255, 136, 0), literal.Color);
        Assert.StartsWith("a { color: oklch(", converted);
        Assert.Equal(converted, Utf8(tab));

        _w.Undo();
        Assert.Equal(css, tab.Editor.Document.Text);
        _w.Redo();
        Assert.Equal(converted, tab.Editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task Converting_a_selection_changes_every_colour_in_it_and_nothing_outside()
    {
        const string css = "a { x: #ff0000; y: rgb(0 128 0); z: hsl(240 100% 50%) }";
        var tab = Open("a.css", css);
        tab.Editor!.Select(css.IndexOf("#ff0000", StringComparison.Ordinal), css.IndexOf("}", StringComparison.Ordinal) - css.IndexOf("#ff0000", StringComparison.Ordinal) - 1);

        await RunAsync("nks.tools.colorToHex");
        Assert.Equal("a { x: #ff0000; y: #008000; z: #0000ff }", tab.Editor.Document.Text);

        await RunAsync("nks.tools.colorToHex");
        Assert.True(_w.Notices.Any(n => n.Message.Contains("už je")), string.Join(" | ", _w.Notices.Select(n => n.Message)));

        tab.Editor.CaretOffset = 0;
        tab.Editor.TextArea.ClearSelection();
        await RunAsync("nks.tools.colorToRgb");
        Assert.True(_w.Notices.Any(n => n.Message.Contains("žádná barva")), string.Join(" | ", _w.Notices.Select(n => n.Message)));
    }

    // ---- key paths ----

    private const string En = "app:\n  title: Title\n  save: Save\n  only_en: x\nlist:\n  - a\n";
    private const string Cs = "app:\n  title: Titulek\n  save: Uložit\n  only_cs: y\nlist:\n  - a\n  - b\n";

    private static ByteRange KeyBytes(DocTab tab, string key)
    {
        var bytes = tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength));
        int at = Encoding.UTF8.GetString(bytes).IndexOf(key, StringComparison.Ordinal);
        return new ByteRange(Encoding.UTF8.GetByteCount(Encoding.UTF8.GetString(bytes)[..at]), Encoding.UTF8.GetByteCount(Encoding.UTF8.GetString(bytes)[..at]) + Encoding.UTF8.GetByteCount(key));
    }

    [AvaloniaFact]
    public async Task The_path_of_the_key_under_the_caret_is_read_through_the_plugin_and_shown_in_the_status_bar()
    {
        var tab = Open("en.yml", En);
        tab.Editor!.CaretOffset = En.IndexOf("Title", StringComparison.Ordinal) + 2;

        var answer = await Pump(_app.Plugins.QueryKeyPathAsync(_w, tab));
        Assert.NotNull(answer);
        Assert.Equal("app.title", answer.Path);
        Assert.Equal(tab.Doc.Doc.Revision, answer.Revision);
        Assert.Equal(KeyBytes(tab, "title"), answer.Key);
        Assert.Equal("app.title", _app.Plugins.Ui.Status[PluginServices.WebDataPluginId].Text);

        tab.Editor.CaretOffset = En.IndexOf("  - a", StringComparison.Ordinal) + 5;
        Assert.Equal("list[0]", (await Pump(_app.Plugins.QueryKeyPathAsync(_w, tab)))!.Path);

        // not a YAML or JSON tab: nothing is asked and the status item goes
        var plain = Open("notes.txt", En);
        _app.Plugins.TrackKeyPath(_w, plain);
        Assert.False(_app.Plugins.Ui.Status.ContainsKey(PluginServices.WebDataPluginId));
    }

    [AvaloniaFact]
    public async Task An_answer_for_an_older_revision_or_a_caret_that_moved_is_never_shown()
    {
        var tab = Open("en.yml", En);
        tab.Editor!.CaretOffset = En.IndexOf("Title", StringComparison.Ordinal) + 2;

        // the document changes while the plugin is reading it
        var late = _app.Plugins.QueryKeyPathAsync(_w, tab);
        tab.Editor.Document.Insert(0, "# note\n");
        Assert.Null(await Pump(late));
        Assert.False(_app.Plugins.Ui.Status.ContainsKey(PluginServices.WebDataPluginId));

        // the caret moves while the plugin is reading it
        tab.Editor.CaretOffset = En.IndexOf("Title", StringComparison.Ordinal) + 9;
        var moved = _app.Plugins.QueryKeyPathAsync(_w, tab);
        tab.Editor.CaretOffset = 0;
        Assert.Null(await Pump(moved));
        Assert.False(_app.Plugins.Ui.Status.ContainsKey(PluginServices.WebDataPluginId));

        // asked again for what is true now
        tab.Editor.CaretOffset = tab.Editor.Document.Text.IndexOf("save", StringComparison.Ordinal) + 1;
        Assert.Equal("app.save", (await Pump(_app.Plugins.QueryKeyPathAsync(_w, tab)))!.Path);
    }

    [AvaloniaFact]
    public async Task Copy_Key_Path_puts_the_path_on_the_clipboard()
    {
        var tab = Open("data.json", "{\"cs\": {\"app\": {\"title\": \"A\"}}}");
        tab.Editor!.CaretOffset = tab.Editor.Document.Text.IndexOf("\"A\"", StringComparison.Ordinal) + 1;
        await _w.Clipboard!.SetTextAsync("before");
        await RunAsync("nks.tools.copyKeyPath");
        await Until(() => _w.Clipboard.TryGetTextAsync().GetAwaiter().GetResult() != "before", "the clipboard");
        Assert.Equal("cs.app.title", await _w.Clipboard.TryGetTextAsync());
    }

    [AvaloniaFact]
    public async Task A_command_of_the_user_waits_for_the_status_bar_query_instead_of_being_refused()
    {
        var tab = Open("en.yml", En);
        tab.Editor!.CaretOffset = En.IndexOf("Title", StringComparison.Ordinal) + 2;
        await _w.Clipboard!.SetTextAsync("before");

        var background = _app.Plugins.QueryKeyPathAsync(_w, tab, background: true);
        Assert.False(_app.Plugins.IsBusy); // the status query is not a command
        var copy = _app.Plugins.CopyKeyPathAsync(_w, tab);
        Assert.True(await Pump(copy));
        await Pump(background);
        await Until(() => _w.Clipboard.TryGetTextAsync().GetAwaiter().GetResult() != "before", "the clipboard");
        Assert.Equal("app.title", await _w.Clipboard.TryGetTextAsync());
    }

    [AvaloniaFact]
    public void The_same_status_text_again_does_not_redraw_the_plugin_ui()
    {
        int changes = 0;
        _app.Plugins.Ui.Changed += () => changes++;
        var status = new NksHub.Textida.PluginHost.Ui.StatusModel("a.b", "tip");
        _app.Plugins.Ui.SetStatus("x", status);
        _app.Plugins.Ui.SetStatus("x", new NksHub.Textida.PluginHost.Ui.StatusModel("a.b", "tip"));
        Assert.Equal(1, changes);
        _app.Plugins.Ui.SetStatus("x", new NksHub.Textida.PluginHost.Ui.StatusModel("a.c", "tip"));
        _app.Plugins.Ui.SetStatus("x", null);
        _app.Plugins.Ui.SetStatus("x", null);
        Assert.Equal(3, changes);
    }

    [AvaloniaFact]
    public async Task Comparing_two_translation_files_lists_the_keys_each_one_lacks()
    {
        var en = Open("en.yml", En);
        var cs = Open("cs.yml", Cs);
        _w.SelectTab(en);

        var run = _app.Workbench.Commands.ExecuteAsync("nks.tools.compareKeys", new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => _w.IsPaletteOpen && _w.PaletteModeNow == PaletteMode.Choice, "the file pick");
        Assert.Equal(["cs.yml"], _w.QuickResults.Select(r => r.Title));
        _w.RunPaletteSelection();
        await Until(() => run.IsCompleted, "the command");
        await Until(() => _app.Output.Read(OutputChannel.Tasks).Any(l => l.Text.Contains("app.only_cs")), "the comparison");
        await Until(() => !_app.Plugins.IsBusy, "the plugin command to finish");

        var lines = _app.Output.Read(OutputChannel.Tasks).Select(l => l.Text).ToList();
        Assert.Equal(["Porovnání klíčů souborů en.yml a cs.yml", "Chybí v souboru cs.yml (1 klíč):", "    app.only_en", "Chybí v souboru en.yml (2 klíče):", "    app.only_cs", "    list[1]"], lines);
        Assert.True(_w.OutputPanelOpen);
        Assert.Equal(En, Utf8(en));
        Assert.Equal(Cs, Utf8(cs));
        Assert.False(_app.Plugins.Documents.IsExposed(cs.Doc.Doc.Id));
    }

    [AvaloniaFact]
    public async Task Go_to_Anything_with_a_path_and_a_click_in_the_outline_select_the_same_bytes_of_the_key()
    {
        var tab = Open("cs.yml", Cs);
        string path = "app.title";
        var key = KeyBytes(tab, "title");

        _w.ShowQuickOpen("@" + path);
        await Until(() => _w.QuickResults.Any(r => r.Title == path), "the keys");
        _w.RunPaletteSelection();
        var viaGoTo = tab.Editor!.ByteSelections()!.Items[0].ToRange();
        Assert.Equal(key, viaGoTo);
        Assert.Equal("title", tab.Editor.SelectedText);

        tab.Editor.TextArea.ClearSelection();
        tab.Editor.CaretOffset = 0;
        var symbols = _app.Plugins.TabSymbols(tab, out bool current);
        Assert.True(current);
        Assert.Contains(symbols, s => s.Name == "list[1]");
        string id = symbols.Single(s => s.Name == path).Id;
        Assert.True(_app.Plugins.RevealSymbol(PluginServices.WebDataPluginId, PluginServices.SymbolsPanelPrefix + tab.Doc.Doc.Id.ToString("N"), id));
        Assert.Equal(key, tab.Editor.ByteSelections()!.Items[0].ToRange());

        // the outline was not opened by Go to Anything, and the document stays visible to the plugin until its tab closes
        Assert.False(_w.PluginPanelOpen);
        Assert.True(_app.Plugins.Documents.IsExposed(tab.Doc.Doc.Id));
        var document = tab.Doc.Doc.Id;
        _w.CloseTab(tab);
        Assert.False(_app.Plugins.Documents.IsExposed(document));
    }

    [AvaloniaFact]
    public async Task A_key_that_was_edited_after_it_was_read_is_not_jumped_to_at_a_wrong_place()
    {
        var tab = Open("cs.yml", Cs);
        Assert.True(await Pump(_app.Plugins.IndexKeysAsync(_w, tab, reveal: false)));
        tab.Editor!.Document.Insert(0, "# c\n");
        var symbols = _app.Plugins.TabSymbols(tab, out bool current);
        Assert.False(current);
        string id = symbols.Single(s => s.Name == "app.save").Id;
        Assert.True(_app.Plugins.RevealTabSymbol(tab, id));
        Assert.Equal("save", tab.Editor.SelectedText); // the change log carries the range over the inserted comment

        tab.Editor.Document.Replace(tab.Editor.Document.Text.IndexOf("save", StringComparison.Ordinal), 4, "SAVE");
        Assert.False(_app.Plugins.RevealTabSymbol(tab, id));
    }
}

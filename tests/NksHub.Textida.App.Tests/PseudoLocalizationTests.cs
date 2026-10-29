using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Csv;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P9.2.2: the layout with texts longer than the translations. Every text of both catalogues is wrapped in a marker and, for the 40 % test, lengthened by a filler.
/// The windows, dialogs, panels and bars are then laid out headless and every label that shows such a text is measured: a label that needs more room than its slot
/// gives it (clipped, trimmed with an ellipsis, wrapped into a slot that is too low, or pushed out of the window) is reported with the text, the room needed and the room it has.
/// The German test uses the real German texts, which are the longest of the four languages.
/// </summary>
[Collection("LocState")]
public class PseudoLocalizationTests : IDisposable
{
    private const char Open = '⟦', Close = '⟧';

    private static readonly Regex Placeholder = new(@"\{[^{}]*\}");

    private readonly TempDir _temp = new();
    private readonly AvaloniaDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly SortedSet<string> _clipped = new(StringComparer.Ordinal);

    public PseudoLocalizationTests()
    {
        _app = TestApp.Prepare(_temp);
        _app.Dialogs = _dialogs;
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    /// <summary>The text between the markers, <paramref name="factor"/> times as long; the filler is made of short words so that a long text can wrap like a real one.</summary>
    internal static string Lengthen(string text, double factor)
    {
        int extra = (int)Math.Ceiling(Placeholder.Replace(text, "").Length * (factor - 1));
        var filler = new StringBuilder();
        while (filler.Length < extra) filler.Append(filler.Length % 3 == 0 ? ' ' : 'x');
        return Open + text + filler + Close;
    }

    /// <summary>Switches the whole program to the texts of <paramref name="language"/>, lengthened by <paramref name="factor"/>.</summary>
    private void UsePseudoTexts(string language, double factor)
    {
        _app.Workbench.UseStrings(new StringTable(TranslationGateTests.WorkbenchTexts(language).ToDictionary(kv => kv.Key, kv => Lengthen(kv.Value, factor))));
        Loc.UseTransformed(language, text => Lengthen(text, factor));
    }

    internal static void Layout(Visual root)
    {
        if (root is Layoutable layoutable) layoutable.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        if (root is Layoutable again) again.UpdateLayout();
    }

    internal static string Shorten(string text) => text.Length <= 70 ? text : text[..67] + "...";

    /// <summary>Why the label does not show all of its text, or null.</summary>
    internal static string? Problem(TextBlock block, string text, Visual root)
    {
        var padding = block.Padding;
        double width = block.Bounds.Width - padding.Left - padding.Right;
        double height = block.Bounds.Height - padding.Top - padding.Bottom;
        var typeface = new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch);
        FormattedText Format() => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, block.FontSize, Brushes.Black);

        if (block.TextWrapping == TextWrapping.NoWrap)
        {
            double needs = Format().Width;
            if (needs > width + 2) return $"needs {needs:0} px, the slot has {width:0} px";
        }
        else if (width > 1)
        {
            var wrapped = Format();
            wrapped.MaxTextWidth = width;
            if (wrapped.Height > height + 2) return $"needs {wrapped.Height:0} px of height, the slot has {height:0} px";
        }
        if (block.TranslatePoint(new Point(block.Bounds.Width, 0), root) is { } right && root.Bounds.Width > 0 && right.X > root.Bounds.Width + 2)
            return $"ends {right.X - root.Bounds.Width:0} px beyond the window";
        // a label that is not stretched takes the room it wants, so it can stick out of the slot its parent has (a button of a fixed width, a row in a narrow column)
        if (block.GetVisualParent() is Visual parent && parent.Bounds.Width > 1 && (block.Bounds.Right > parent.Bounds.Width + 2 || block.Bounds.Left < -2))
            return $"is {block.Bounds.Width:0} px wide in a parent that is {parent.Bounds.Width:0} px wide";
        return null;
    }

    /// <summary>
    /// Whether anything of the label reaches the screen. The one exception to "every label is measured" is by design: the 1 px live region the window announces to a screen reader with
    /// (opacity 0) holds the text of every notice and palette choice and cannot show it; a label drawn at full opacity is never skipped.
    /// </summary>
    internal static bool IsDrawn(TextBlock block) => block.GetSelfAndVisualAncestors().All(v => v.Opacity > 0);

    /// <summary>Lays out <paramref name="root"/> and measures every visible label that shows a marked text.</summary>
    private void Look(string surface, Visual root)
    {
        Layout(root);
        foreach (var block in root.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!block.IsEffectivelyVisible || !IsDrawn(block) || block.Text is not { Length: > 0 } text || !text.Contains(Open)) continue;
            if (Problem(block, text, root) is { } problem) _clipped.Add($"{surface}: \"{Shorten(text)}\" {problem}");
        }
    }

    private async Task InDialog(string surface, Func<object?> open)
    {
        Window? dialog = null;
        void Capture(Window w) => dialog = w;
        AvaloniaDialogs.DialogCreated += Capture;
        object? pending;
        try { pending = open(); }
        finally { AvaloniaDialogs.DialogCreated -= Capture; }
        Assert.NotNull(dialog);
        Look(surface, dialog!);
        dialog!.Close();
        if (pending is Task task) await task;
    }

    private async Task InWindow(string surface, Window window)
    {
        window.Show();
        Look(surface, window);
        window.Close();
        await Task.Yield();
    }

    private DocTab OpenHex()
    {
        var bytes = new byte[4000];
        new Random(5).NextBytes(bytes);
        bytes[0] = 0;
        _w.OpenFile(_temp.Bytes("a.bin", bytes), null);
        TestApp.Press(_w, Key.K, TestApp.Mod);
        TestApp.Press(_w, Key.H, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(_w.Active!.Hex);
        return _w.Active;
    }

    private void Run(string id) => _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult();

    [AvaloniaFact]
    public void The_markers_and_the_lengthening_work()
    {
        Assert.Equal("⟦Save⟧", Lengthen("Save", 1.0));
        Assert.Equal("⟦abcdefghij xx ⟧", Lengthen("abcdefghij", 1.4));
        // a placeholder is replaced by a value of its own length, so it adds nothing to the filler
        Assert.Equal(Lengthen("abcdefghij", 1.4).Length + 3, Lengthen("{n}abcdefghij", 1.4).Length);
    }

    [AvaloniaTheory]
    [InlineData("de", 1.0)]
    [InlineData("en", 1.4)]
    public async Task No_label_is_clipped_when_the_texts_are_longer(string language, double factor)
    {
        UsePseudoTexts(language, factor);

        // the main window: menu, tabs, status bar and the panels and bars around the text
        _w.NewDocument();
        Look("main window", _w);
        Run("nks.view.toggleToolbar");
        Look("toolbar", _w);
        Run("nks.view.toggleToolbar");
        _w.ToggleDocuments(true);
        Look("documents panel", _w);
        _w.ToggleDocuments(false);
        _w.ToggleFiles(true);
        Look("files panel", _w);
        _w.ToggleFiles(false);
        _w.ToggleOutput(true);
        Look("output panel", _w);
        _w.ToggleOutput(false);
        _w.ToggleAgentPanel(true);
        Look("agent panel", _w);
        _w.ToggleAgentPanel(false);
        _w.ShowFind(replace: true);
        Look("find and replace bar", _w);
        _w.FindBar.Open(files: true, replace: true);
        Look("find in files bar", _w);
        _w.FindBar.Close();
        _w.ShowPalette();
        Look("command palette", _w);
        _w.ClosePalette();
        _w.ShowNotice("restore", Loc.Plural(K.RestoreBar, 3, ("unsaved", 2)), [(Loc.Get(K.RestoreDetails), () => { })]);
        Look("recovery bar", _w);
        _w.DismissNotice("restore");
        _w.Active!.ShowBar(new InfoBar(Loc.Get(K.BarChanged, ("title", "notes.txt")), [(Loc.Get(K.BarReload), () => { }), (Loc.Get(K.BarKeepMine), () => { }), (Loc.Get(K.BarCompare), () => { })], warning: true));
        Look("file changed bar", _w);
        _w.Active.ShowBar(null);

        // the views of a tab
        OpenHex();
        Look("hex view", _w);
        Run("nks.hex.inspector");
        Look("hex data inspector", _w);
        Run("nks.hex.bookmarks.panel");
        Look("hex bookmarks panel", _w);
        _w.CompareFiles(_temp.File("left.txt", "one\ntwo\nthree\n"), _temp.File("right.txt", "one\n2\nthree\nfour\n"));
        Look("comparison", _w);
        _w.OpenFile(_temp.File("data.csv", "id,name,note\n1,Ann,first\n2,Bob,second\n"), null);
        Run("nks.view.table");
        Look("table view", _w);

        // windows of their own
        _app.OpenSettings();
        await InWindow("settings", _app.SettingsView!);
        _app.OpenHelp("start");
        await InWindow("help", _app.HelpView!);
        await InWindow("regular expression tester", new RegexTesterWindow(new SearchPattern(@"(\w+) (\w+)", true)));
        await InWindow("tool editor", new ToolEditorWindow(_app.Tools, _w.PreviewTool));
        await InWindow("toolbar editor", new ToolbarEditorWindow(_app.Workbench, _app.Toolbar));
        await InWindow("LOG pattern", new LogPatternWindow("%F(%L,%C)", LogPatternSyntax.Mask, "build\nProgram.cs(12,5): error CS1002: ; expected\n"));

        // dialogs
        (TextCodec, int)[] targets = [(TextCodec.Utf8, 0), (TextCodec.Utf8Bom, 3)];
        await InDialog("unsaved changes", () => _dialogs.ChooseAsync(_w, new ChoiceRequest(Loc.Get(K.DialogUnsavedTitle), Loc.Get(K.DialogUnsavedMessage, ("title", "notes.txt")),
            [new DialogButton(Loc.Get(K.DialogSave), IsDefault: true), new DialogButton(Loc.Get(K.DialogDontSave)), new DialogButton(Loc.Get(K.DialogKeepInJournal)), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)])));
        await InDialog("revision of the history", () => _dialogs.ChooseAsync(_w, new ChoiceRequest(Loc.Get(K.HistoryActionsTitle, ("time", "12:00")),
            Loc.Get(K.HistoryActionsMessage, ("source", Loc.Get(K.HistorySourceSave)), ("size", "1 KiB")),
            [new DialogButton(Loc.Get(K.HistoryCompare), IsDefault: true), new DialogButton(Loc.Get(K.HistoryRestoreFile)), new DialogButton(Loc.Get(K.HistoryRestoreSection)),
             new DialogButton(Loc.Get(K.HistorySetLabel)), new DialogButton(Loc.Get(K.HistoryPin)), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)])));
        await InDialog("question with a text field", () => _dialogs.PromptAsync(_w, Loc.Get(K.HexGotoTitle), Loc.Get(K.HexGotoLabel), "", _ => Loc.Get(K.HexGotoPreviewA11y)));
        await InDialog("secret field", () => _dialogs.PromptSecretAsync(_w, Loc.Get(K.HexGotoTitle), Loc.Get(K.HexGotoLabel)));
        await InDialog("save as", () => _dialogs.SaveAsAsync(_w, new SaveAsRequest(null, TextCodec.Utf8, targets)));
        await InDialog("choice of encoding", () => _dialogs.PickEncodingAsync(_w, Loc.Get(K.LossPickTitle), Loc.Get(K.LossPickLabel), targets, TextCodec.Utf8));
        await InDialog("about", () => _dialogs.ShowAboutAsync(_w));
        await InDialog("hex bookmark", () => _dialogs.HexBookmarkAsync(_w, new HexBookmarkForm("header", "#0F7B6C", "magic number", null, "0x0 - 0x4")));
        await InDialog("insert into lines", () => _dialogs.InsertIntoLinesAsync(_w, new LineInsertOptions(), _ => Loc.Get(K.LinesPreview)));
        await InDialog("sort lines", () => _dialogs.SortLinesAsync(_w, new SortOptions(), _ => Loc.Get(K.LinesPreview)));
        await InDialog("table region", () => _dialogs.TableRegionAsync(_w, CsvDialect.Csv, _ => Loc.Get(K.LinesPreview)));

        Assert.True(_clipped.Count == 0, $"{language} at {factor:P0}: {_clipped.Count} labels do not fit their slot:\n" + string.Join("\n", _clipped));
    }
}

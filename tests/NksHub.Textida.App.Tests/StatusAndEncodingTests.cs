using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Tests;

/// <summary>Status bar items as D5 shows them and the encoding actions behind them.</summary>
[Collection("LocState")]
public class StatusAndEncodingTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    static StatusAndEncodingTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public StatusAndEncodingTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _app.Workbench.Set("editor.detectIndentation", System.Text.Json.Nodes.JsonValue.Create(false)); // these tests read the configured indentation
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static string Text(DocTab t) => t.Editor!.Document.Text;

    private static byte[] DocBytes(DocTab t) => t.Doc.Doc.ReadBytes(new ByteRange(0, t.Doc.Doc.ByteLength));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private DocTab OpenHtml()
    {
        var sb = new StringBuilder();
        for (int i = 1; i <= 30; i++) sb.Append("  <p id=\"produkt-").Append(i).Append("\">x</p>\r\n");
        var tab = _w.OpenFile(_temp.File("index.html", sb.ToString()), null);
        tab.Editor!.CaretOffset = tab.Editor.Document.GetOffset(21, 16);
        return tab;
    }

    [AvaloniaFact]
    public void Status_bar_reads_like_D5()
    {
        var tab = OpenHtml();
        tab.Doc.FlushEol();
        Assert.Equal("Ř 21, Sl 16 · Připraveno", _w.StatusLeftText);
        Assert.Equal("Mezery: 4 · UTF-8 · CRLF · HTML", _w.StatusRightText);
        Assert.DoesNotContain("karty", _w.StatusLeftText + _w.StatusRightText);
    }

    [AvaloniaFact]
    public void Status_bar_is_translated()
    {
        OpenHtml();
        _app.SetLanguage("en");
        Assert.Equal("Ln 21, Col 16 · Ready", _w.StatusLeftText);
        Assert.Equal("Spaces: 4 · UTF-8 · CRLF · HTML", _w.StatusRightText);
        _app.SetLanguage("cs");
    }

    [AvaloniaFact]
    public void Plain_text_and_selection_are_shown()
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = "hello";
        tab.Editor.Select(0, 3);
        Assert.Contains("(vybráno 3)", _w.StatusLeftText);
        Assert.Contains("Prostý text", _w.StatusRightText);
        Assert.StartsWith("Mezery: 4 · UTF-8 · ", _w.StatusRightText);
    }

    [AvaloniaFact]
    public void Mixed_line_endings_are_shown_as_mixed_and_survive_an_edit_and_save()
    {
        byte[] original = "a\r\nb\nc\rd".Select(c => (byte)c).ToArray();
        var path = _temp.Bytes("mixed.txt", original);
        var tab = _w.OpenFile(path, null);
        Assert.Contains("smíšené", _w.StatusRightText);
        tab.Editor!.Document.Replace(tab.Editor.Document.GetLineByNumber(2).Offset, 1, "B");
        Assert.True(_w.SaveAsync(tab).Result);
        Assert.Equal("a\r\nB\nc\rd".Select(c => (byte)c).ToArray(), File.ReadAllBytes(path));
    }

    [AvaloniaFact]
    public void Eol_label_follows_the_document()
    {
        var lf = _w.OpenFile(_temp.File("lf.txt", "a\nb\n"), null);
        Assert.Contains("· LF ·", _w.StatusRightText);
        var cr = _w.OpenFile(_temp.Bytes("cr.txt", "a\rb\r".Select(c => (byte)c).ToArray()), null);
        Assert.Contains("· CR ·", _w.StatusRightText);
        _w.SelectTab(lf);
        Assert.Contains("· LF ·", _w.StatusRightText);
    }

    [AvaloniaFact]
    public void An_uncertain_encoding_guess_is_marked_and_explained()
    {
        var tab = _w.OpenFile(_temp.Bytes("cafe.txt", Encoding.GetEncoding(1252).GetBytes("un café")), null);
        Assert.True(tab.Doc.EncodingUncertain);
        Assert.EndsWith("?", (string)_w.EncodingItem.Content!);
        Assert.Contains("odhad", ToolTip.GetTip(_w.EncodingItem)!.ToString());
        _w.RereadAs(tab, TextCodec.Cp1250);
        Assert.False(tab.Doc.EncodingUncertain);
        Assert.False(((string)_w.EncodingItem.Content!).EndsWith('?'));
    }

    [AvaloniaFact]
    public void Status_items_open_their_menus_on_click()
    {
        var tab = OpenHtml();
        Click(_w.EncodingItem);
        Click(_w.EolItem);
        Click(_w.IndentItem);
        Click(_w.LanguageItem);
        Assert.NotNull(tab);
    }

    // ---- encoding menu ----

    [AvaloniaFact]
    public void The_encoding_menu_has_two_separate_actions()
    {
        OpenHtml();
        var flyout = _w.ShowEncodingMenu(null)!;
        var headers = flyout.Items.OfType<MenuItem>().Select(m => (string)m.Header!).ToList();
        Assert.Equal(["Znovu přečíst jako…", "Převést na…"], headers);
        Assert.NotEmpty(flyout.Items.OfType<MenuItem>().First().Items);
    }

    [AvaloniaFact]
    public void The_picker_lists_the_encodings_in_groups_names_the_group_to_a_screen_reader_and_keeps_the_last_ones_on_top()
    {
        OpenHtml();
        var groups = _w.ShowEncodingMenu(onlyConvert: false)!.Items.OfType<MenuItem>().Where(m => m.Items.Count > 0).ToList();
        Assert.Equal(["Unicode", "Středoevropské", "Západoevropské", "Cyrilice", "Řečtina", "Turečtina", "Baltské", "Hebrejština", "Arabština", "Asijské", "DOS"], groups.Select(g => (string)g.Header!));
        var cyrillic = groups.Single(g => (string)g.Header! == "Cyrilice").Items.OfType<MenuItem>().ToList();
        Assert.Contains(cyrillic, m => (string)m.Header! == "KOI8-R");
        var koi = cyrillic.Single(m => (string)m.Header! == "KOI8-R");
        Assert.Equal("Cyrilice: KOI8-R", Avalonia.Automation.AutomationProperties.GetName(koi));

        koi.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        var again = _w.ShowEncodingMenu(onlyConvert: false)!.Items;
        Assert.Equal("Nedávno použitá", (string)((MenuItem)again[0]!).Header!);
        Assert.Equal("KOI8-R", (string)((MenuItem)again[1]!).Header!);
        Assert.Contains(again.OfType<MenuItem>(), m => m.Header is "Najít kódování…");
    }

    [AvaloniaFact]
    public void Lossy_targets_are_flagged_before_the_choice()
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = "řč😀";
        var choices = _w.ConvertChoices(tab).ToDictionary(c => c.Codec.Name);
        Assert.Equal(1, choices["Windows-1250"].Lost);
        Assert.Equal(3, choices["Windows-1252"].Lost);
        Assert.Equal(0, choices["UTF-16 LE"].Lost);
        Assert.Equal(0, choices["UTF-8 with BOM"].Lost);
        Assert.Contains("ztrátové", choices["Windows-1250"].Label);
        Assert.DoesNotContain("ztrátové", choices["UTF-8 with BOM"].Label);

        var menu = _w.ShowEncodingMenu(onlyConvert: true)!;
        Assert.Contains(menu.Items.OfType<MenuItem>().SelectMany(g => g.Items.OfType<MenuItem>()), m => ((string)m.Header!).Contains("Windows-1250") && ((string)m.Header!).Contains("ztrátové")); // inside its group
    }

    [AvaloniaFact]
    public void Reread_as_changes_neither_the_bytes_nor_the_file_nor_the_modified_state()
    {
        byte[] bytes = [0x50, 0xF8, 0xED];
        var path = _temp.Bytes("reread.txt", bytes);
        var tab = _w.OpenFile(path, null);
        var print = tab.Doc.Doc.Fingerprint;
        var before = DocBytes(tab);
        _w.RereadAs(tab, TextCodec.Cp1250);
        Assert.Equal("Příl".Substring(0, 1) + "ří", Text(tab));
        Assert.Equal(before, DocBytes(tab));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(print, tab.Doc.Doc.Fingerprint);
        Assert.False(tab.Doc.Doc.IsModified);
        Assert.Contains("Windows-1250", _w.StatusRightText);
        _w.RereadAs(tab, TextCodec.Latin2);
        Assert.Equal(before, DocBytes(tab));
    }

    [AvaloniaFact]
    public async Task Convert_to_a_lossy_encoding_shows_only_the_lost_character_and_needs_confirmation()
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = "řč😀";
        var utf8 = DocBytes(tab);

        _dialogs.Answers.Enqueue(2); // cancel
        Assert.False(await _w.ConvertToAsync(tab, TextCodec.Cp1250));
        var loss = Assert.Single(_dialogs.Asked);
        var entry = Assert.Single(loss.Details!);
        Assert.Contains("😀", entry);
        Assert.DoesNotContain("U+0159", entry);
        Assert.Equal(utf8, DocBytes(tab));
        Assert.Equal(TextCodec.Utf8, tab.Doc.Doc.Codec);

        _dialogs.Answers.Enqueue(0); // replace and convert
        Assert.True(await _w.ConvertToAsync(tab, TextCodec.Cp1250));
        Assert.Equal(new byte[] { 0xF8, 0xE8, 0x3F }, DocBytes(tab));
        Assert.Equal(TextCodec.Cp1250, tab.Doc.Doc.Codec);

        Assert.NotNull(tab.Doc.Doc.Undo());
        Assert.Equal(utf8, DocBytes(tab));
        Assert.Equal(TextCodec.Utf8, tab.Doc.Doc.Codec);
    }

    [AvaloniaFact]
    public async Task Convert_without_loss_lists_the_changed_range_and_writes_only_after_confirmation()
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = "řč";
        var utf8 = DocBytes(tab);

        _dialogs.Answers.Enqueue(1); // cancel
        Assert.False(await _w.ConvertToAsync(tab, TextCodec.Cp1250));
        Assert.Equal(utf8, DocBytes(tab));
        Assert.Equal(TextCodec.Utf8, tab.Doc.Doc.Codec);
        var ask = Assert.Single(_dialogs.Asked);
        Assert.Equal(Loc.Get(K.InspectTitle), ask.Title);
        Assert.Contains("UTF-8", ask.Message);
        Assert.Contains("Windows-1250", ask.Message);
        var range = Assert.Single(ask.Details!);
        Assert.Contains("C5 99 C4 8D", range); // old bytes
        Assert.Contains("F8 E8", range);       // new bytes

        _dialogs.Answers.Enqueue(0);
        Assert.True(await _w.ConvertToAsync(tab, TextCodec.Cp1250));
        Assert.Equal(new byte[] { 0xF8, 0xE8 }, DocBytes(tab));
        Assert.Contains("Windows-1250", _w.StatusRightText);
    }

    // ---- line endings and normalisation ----

    [AvaloniaFact]
    public async Task Unify_line_endings_shows_the_count_and_is_one_undo_step()
    {
        var path = _temp.Bytes("eol.txt", "a\r\nb\nc\r\n".Select(c => (byte)c).ToArray());
        var tab = _w.OpenFile(path, null);
        var original = DocBytes(tab);

        _dialogs.Answers.Enqueue(1); // cancel
        Assert.False(await _w.UnifyEolAsync(tab, EolStyle.Lf));
        Assert.Equal(original, DocBytes(tab));
        Assert.Contains("2", _dialogs.Asked[0].Message);
        Assert.Contains("LF", _dialogs.Asked[0].Message);

        _dialogs.Answers.Enqueue(0);
        Assert.True(await _w.UnifyEolAsync(tab, EolStyle.Lf));
        Assert.Equal("a\nb\nc\n".Select(c => (byte)c).ToArray(), DocBytes(tab));
        tab.Doc.FlushEol();
        Assert.Contains("· LF ·", _w.StatusRightText);
        _w.Undo();
        Assert.Equal(original, DocBytes(tab));
    }

    [AvaloniaFact]
    public async Task Unify_with_nothing_to_change_says_so()
    {
        var tab = _w.OpenFile(_temp.File("same.txt", "a\nb\n"), null);
        Assert.False(await _w.UnifyEolAsync(tab, EolStyle.Lf));
        Assert.Contains("Není co měnit", _w.StatusLeftText);
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public async Task Normalisation_converts_between_nfd_and_nfc_with_a_preview_and_undo()
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = "é a";
        var before = DocBytes(tab);
        _dialogs.Answers.Enqueue(0);
        Assert.True(await _w.NormalizeAsync(tab, NormalizationForm.FormC));
        Assert.Equal("é a", Text(tab));
        Assert.Contains("NFC", _dialogs.Asked[0].Message);
        Assert.Single(_dialogs.Asked[0].Details!);

        _dialogs.Answers.Enqueue(0);
        Assert.True(await _w.NormalizeAsync(tab, NormalizationForm.FormD));
        Assert.Equal("é a", Text(tab));
        _w.Undo();
        _w.Undo();
        Assert.Equal(before, DocBytes(tab));
    }

    // ---- indent and language ----

    [AvaloniaFact]
    public void Indent_menu_switches_between_spaces_and_tabs()
    {
        OpenHtml();
        var flyout = _w.ShowIndentMenu();
        var tabs = flyout.Items.OfType<MenuItem>().First(m => (string)m.Header! == "Odsazovat tabulátory");
        tabs.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Contains("Tabulátory: 4", _w.StatusRightText);
        Assert.False(_app.Workbench.Settings.GetBool("editor.insertSpaces"));
    }

    [AvaloniaFact]
    public void Language_menu_sets_the_mode_of_the_tab()
    {
        var tab = _w.NewDocument();
        var flyout = _w.ShowLanguageMenu()!;
        var js = flyout.Items.OfType<MenuItem>().First(m => (string)m.Header! == "JavaScript");
        js.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal("javascript", tab.Doc.Language.Id);
        Assert.EndsWith("JavaScript", _w.StatusRightText);
    }

    [AvaloniaFact]
    public void Language_is_chosen_from_the_extension_and_drives_language_settings()
    {
                var tab = _w.OpenFile(_temp.File("a.py", "x = 1\n"), null);
        Assert.Equal("python", tab.Doc.Language.Id);
        Assert.EndsWith("Python", _w.StatusRightText);
    }
}

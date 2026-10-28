using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Tests;

/// <summary>P5.6.1 in the window: /regex/ and the wildcard * in the byte mode of the find bar.</summary>
[Collection("LocState")]
public class HexSearchAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public HexSearchAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private DocTab OpenHex(byte[] bytes)
    {
        _w.OpenFile(_temp.Bytes("search.bin", bytes), null);
        TestApp.Press(_w, Key.K, TestApp.Mod);
        TestApp.Press(_w, Key.H, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(_w.Active!.Hex);
        _w.Active.Hex!.Focus();
        Dispatcher.UIThread.RunJobs();
        return _w.Active;
    }

    /// <summary>Ctrl+F in the hex view opens the bar in byte mode; the query is typed into it and the first step is F3 (Enter does the same, see the test of the bar).</summary>
    private void Search(string query)
    {
        TestApp.Press(_w, Key.F, TestApp.Mod);
        Assert.True(_w.FindBar.HexMode);
        _w.FindBar.FindBox.Text = query;
        Dispatcher.UIThread.RunJobs();
        TestApp.Press(_w, Key.F3);
    }

    private void PumpUntil(Func<bool> condition)
    {
        for (int i = 0; i < 2000 && !condition(); i++)
        {
            Thread.Sleep(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition(), "status: " + _w.StatusLeftText);
    }

    [AvaloniaFact]
    public void A_regex_between_slashes_finds_what_a_plain_pass_finds_and_f3_steps_through_it()
    {
        var bytes = new byte[40_000];
        new Random(9).NextBytes(bytes);
        bytes[0] = 0;
        foreach (var (at, third) in new[] { (100, 1), (3000, 8), (20_000, 9), (39_990, 5) })
        {
            bytes[at] = 0x50; bytes[at + 1] = 0x4B; bytes[at + 2] = (byte)third;
        }
        var expected = new List<long>();
        for (int i = 0; i + 3 <= bytes.Length; i++)
            if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] is >= 1 and <= 8) expected.Add(i);
        var hex = OpenHex(bytes).Hex!;

        Search(@"/\x50\x4B[\x01-\x08]/");
        PumpUntil(() => hex.Editor.HasSelection && _w.StatusLeftText.Contains(expected.Count.ToString()));

        Assert.Equal(expected[0], hex.Editor.Selection.Start);
        Assert.Equal(3, hex.Editor.Selection.Length);
        var seen = new List<long> { hex.Editor.Selection.Start };
        for (int i = 1; i < expected.Count; i++)
        {
            long previous = hex.Editor.Selection.Start;
            TestApp.FindNext(_w);
            PumpUntil(() => hex.Editor.Selection.Start != previous);
            seen.Add(hex.Editor.Selection.Start);
        }
        Assert.Equal(expected, seen);
    }

    [AvaloniaFact]
    public void The_wildcard_follows_the_setting_n_and_uses_the_default_while_it_is_not_chosen()
    {
        var data = Encoding.ASCII.GetBytes("\0A12B A1234B");
        var hex = OpenHex(data).Hex!;

        Search("41 * 42");
        PumpUntil(() => hex.Editor.HasSelection);
        Assert.Equal((1L, 12L), (hex.Editor.Selection.Start, hex.Editor.Selection.End)); // * is greedy up to the default span, so it reaches the last B

        _app.Workbench.Set("hex.wildcardMaxBytes", 2);
        Search("41 * 42"); // the same text: opening the bar reads the query again with the new N
        PumpUntil(() => hex.Editor.Selection.End == 5);
        Assert.Equal((1L, 5L), (hex.Editor.Selection.Start, hex.Editor.Selection.End));

        Search("41 *{4} 42");
        PumpUntil(() => hex.Editor.Selection.Start == 6);
        Assert.Equal(6L, hex.Editor.Selection.Start);
        Assert.Equal(6L, hex.Editor.Selection.Length);
    }

    private static byte[] Data() => [0x00, 0x01, 0xAA, 0xBB, 0x02, 0xAA, 0xBB, 0x03];

    [AvaloniaFact]
    public void Replace_all_shows_the_preview_writes_one_undo_step_and_warns_about_the_length()
    {
        var tab = OpenHex(Data());
        var hex = tab.Hex!;
        hex.ToggleInsert();
        _dialogs.Prompts.Enqueue("AA BB");
        _dialogs.Prompts.Enqueue("C1 C2 C3");
        _dialogs.Answers.Enqueue(0);

        TestApp.Replace(_w);
        PumpUntil(() => tab.Doc.Doc.ByteLength == 10);

        var ask = Assert.Single(_dialogs.Asked);
        Assert.Equal(Loc.Get(K.HexReplaceTitle), ask.Title);
        Assert.Contains("2", ask.Message);
        Assert.Contains(Loc.Get(K.HexReplaceLength, ("delta", 2), ("old", 8), ("new", 10)), ask.Message);
        Assert.Equal(2, ask.Details!.Count);
        Assert.Contains("AA BB", ask.Details[0]);
        Assert.Contains("C1 C2 C3", ask.Details[0]);
        Assert.Equal([0x00, 0x01, 0xC1, 0xC2, 0xC3, 0x02, 0xC1, 0xC2, 0xC3, 0x03], tab.Doc.Doc.ReadBytes(new(0, 10)));

        _w.Undo();
        Assert.Equal(Data(), tab.Doc.Doc.ReadBytes(new(0, 8)));
    }

    [AvaloniaFact]
    public void Replace_in_overwrite_mode_refuses_a_change_of_length_and_a_changed_file_refuses_the_whole_replace()
    {
        var tab = OpenHex(Data());
        var hex = tab.Hex!;
        Assert.False(hex.Insert);
        _dialogs.Prompts.Enqueue("AA BB");
        _dialogs.Prompts.Enqueue("C1 C2 C3");
        TestApp.Replace(_w);
        PumpUntil(() => _w.StatusLeftText.Contains(Loc.Get(K.HexReplaceOverwrite)));
        Assert.Empty(_dialogs.Asked);
        Assert.Equal(Data(), tab.Doc.Doc.ReadBytes(new(0, 8)));

        _dialogs.Prompts.Enqueue("AA BB");
        _dialogs.Prompts.Enqueue("DD EE");
        _dialogs.Answers.Enqueue(0);
        _dialogs.WhileAsking = () => { _dialogs.WhileAsking = null; Assert.True(hex.Editor.Write(new(0, 1), [0x7F]).Applied); }; // the file changes while the preview is open
        TestApp.Replace(_w);
        PumpUntil(() => _w.StatusLeftText.Contains(Loc.Get(K.HexReplaceStale)));
        Assert.Equal([0x7F, 0x01, 0xAA, 0xBB, 0x02, 0xAA, 0xBB, 0x03], tab.Doc.Doc.ReadBytes(new(0, 8)));
    }

    [AvaloniaFact]
    public void The_replacement_has_to_be_exact_bytes()
    {
        var charset = HexCharset.All[0];
        Assert.Equal([0xDE, 0xAD], MainWindow.ParseReplacement("DE AD", charset));
        Assert.Empty(MainWindow.ParseReplacement("  ", charset));
        Assert.Equal("ab"u8.ToArray(), MainWindow.ParseReplacement("\"ab\"", HexCharset.All.First(c => c.Codec.Kind == NksHub.Textida.Core.Encodings.CodecKind.Utf8)));
        Assert.Throws<FormatException>(() => MainWindow.ParseReplacement("DE ??", charset));
        Assert.Throws<FormatException>(() => MainWindow.ParseReplacement("DE * AD", charset));
    }

    [AvaloniaFact]
    public void The_query_forms_are_told_apart()
    {
        var charset = HexCharset.All[0];
        Assert.IsType<BytePattern>(MainWindow.ParseHexQuery("50 4B ?? 04", charset));
        Assert.IsType<BytePattern>(MainWindow.ParseHexQuery("\"abc\"", charset));
        Assert.IsType<BytePattern>(MainWindow.ParseHexQuery("0x1F40 i32", charset));
        Assert.IsType<ByteRegex>(MainWindow.ParseHexQuery("50 * 04", charset, 3));
        Assert.IsType<ByteRegex>(MainWindow.ParseHexQuery("/ab+c/", charset, 3));
        Assert.Throws<FormatException>(() => MainWindow.ParseHexQuery("50 * 04", charset));
    }

    [AvaloniaFact]
    public void Ctrl_F_in_a_hex_view_opens_the_bar_in_byte_mode_with_the_length_of_a_match_and_enter_steps_through_the_matches()
    {
        var bytes = new byte[64];
        foreach (int at in new[] { 10, 30 }) new byte[] { 0x50, 0x4B, 0x03, 0x04 }.CopyTo(bytes, at);
        var hex = OpenHex(bytes).Hex!;
        var bar = _w.FindBar;

        TestApp.Press(_w, Key.F, TestApp.Mod);
        Assert.True(bar.IsVisible && bar.HexMode);
        Assert.Empty(_dialogs.Prompts); // no window asks for the pattern any more
        Assert.All(new Control[] { bar.CaseToggle, bar.RegexToggle, bar.WordToggle, bar.SelectionToggle, bar.ReplaceBox, bar.ListButton }, c => Assert.False(c.IsVisible));
        Assert.Equal(Loc.Get(K.FindHexWatermark), bar.FindBox.PlaceholderText);
        Assert.True(bar.FindBox.IsFocused);

        bar.FindBox.Text = "50 4B ?? 04";
        Assert.Equal(Loc.Get(K.HexFindPreview, ("min", 4), ("max", 4)), bar.CountText.Text);
        TestApp.Press(_w, Key.Enter);
        PumpUntil(() => hex.Editor.HasSelection && bar.CountText.Text == Loc.Get(K.HexFindCount, ("index", 1), ("total", 2)));
        Assert.Equal((10L, 4L), (hex.Editor.Selection.Start, hex.Editor.Selection.Length));

        TestApp.Press(_w, Key.Enter);
        PumpUntil(() => hex.Editor.Selection.Start == 30);
        PumpUntil(() => bar.CountText.Text == Loc.Get(K.HexFindCount, ("index", 2), ("total", 2)));
        TestApp.Press(_w, Key.Enter, RawInputModifiers.Shift);
        PumpUntil(() => hex.Editor.Selection.Start == 10);

        TestApp.Press(_w, Key.Escape);
        Assert.False(bar.IsVisible);
        TestApp.Press(_w, Key.F3); // the query is kept: F3 searches it without the bar
        PumpUntil(() => hex.Editor.Selection.Start == 30);
        Assert.False(bar.IsVisible);
    }

    [AvaloniaFact]
    public void A_query_that_is_refused_is_told_in_the_bar_and_a_step_does_not_search_for_the_query_before_it()
    {
        var bytes = new byte[32];
        bytes[8] = 0x41;
        var hex = OpenHex(bytes).Hex!;
        TestApp.Press(_w, Key.F, TestApp.Mod);
        var bar = _w.FindBar;

        bar.FindBox.Text = "41";
        Assert.Equal(Loc.Get(K.HexFindPreview, ("min", 1), ("max", 1)), bar.CountText.Text);
        Assert.DoesNotContain("error", bar.CountText.Classes);

        bar.FindBox.Text = "ZZ";
        Assert.StartsWith(Loc.Get(K.HexFindInvalid, ("reason", "")), bar.CountText.Text);
        Assert.Contains("error", bar.CountText.Classes);
        TestApp.Press(_w, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        Assert.False(hex.Editor.HasSelection);
        Assert.Empty(_dialogs.Prompts);

        bar.FindBox.Text = "";
        Assert.Equal("", bar.CountText.Text ?? "");
        Assert.DoesNotContain("error", bar.CountText.Classes);
    }

    [AvaloniaFact]
    public void The_bar_is_the_byte_search_only_in_a_hex_view_and_closes_when_the_view_changes_kind()
    {
        _w.OpenFile(_temp.File("first.txt", "abc abc"), null);
        Dispatcher.UIThread.RunJobs();
        TestApp.Press(_w, Key.F, TestApp.Mod);
        _w.FindBar.FindBox.Text = "abc";
        Assert.False(_w.FindBar.HexMode);

        OpenHex([0, 1, 2, 3]);
        Assert.False(_w.FindBar.IsVisible); // a bar for text does not follow the tab to a hex view
        TestApp.Press(_w, Key.F, TestApp.Mod);
        Assert.True(_w.FindBar.IsVisible && _w.FindBar.HexMode);
        Assert.Equal("", _w.FindBar.FindBox.Text ?? ""); // the text query is not the byte query
        _w.FindBar.FindBox.Text = "00 01";
        TestApp.Press(_w, Key.Escape);
        Assert.False(_w.FindBar.IsVisible);
        TestApp.Press(_w, Key.F, TestApp.Mod);
        Assert.Equal("00 01", _w.FindBar.FindBox.Text); // the byte search keeps its query for the next time

        _w.OpenFile(_temp.File("plain.txt", "abc abc"), null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(_w.FindBar.IsVisible);
        TestApp.Press(_w, Key.F, TestApp.Mod);
        Assert.True(_w.FindBar.IsVisible);
        Assert.False(_w.FindBar.HexMode);
        Assert.Equal("abc", _w.FindBar.FindBox.Text); // and the text search its own
        Assert.True(_w.FindBar.RegexToggle.IsVisible);

        _w.SelectTab(_w.Tabs.First(t => t.Hex is not null));
        Dispatcher.UIThread.RunJobs();
        Assert.False(_w.FindBar.IsVisible);
    }

    [AvaloniaFact]
    public void Find_in_files_is_never_the_byte_mode_and_ctrl_h_in_hex_still_asks_in_windows()
    {
        var hex = OpenHex([1, 2, 3, 4]).Hex!;
        _w.FindBar.Open(files: true, replace: false, hex: true);
        Assert.False(_w.FindBar.HexMode);
        _w.FindBar.Close();

        _dialogs.Prompts.Enqueue("01");
        _dialogs.Prompts.Enqueue("09");
        _dialogs.Answers.Enqueue(0);
        TestApp.Press(_w, Key.H, TestApp.Mod);
        PumpUntil(() => hex.Editor.Document.ReadBytes(new(0, 1))[0] == 9);
        Assert.False(_w.FindBar.IsVisible);
    }
}

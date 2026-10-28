using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P3.5.8 and P9.1.2: what the hex grid and the map of the file tell a screen reader, read from the same automation peers the reader reads, the announcements that
/// need no focus, and the keyboard paths to every part of the hex view. What NVDA and VoiceOver say about it needs a person with a screen reader.
/// </summary>
[Collection("LocState")]
public class HexAccessibilityTests : IDisposable
{
    private const string Env = "# keys\nAPI_KEY=hunter2\nPLAIN=x\n";

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public HexAccessibilityTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
    }

    public void Dispose()
    {
        _app.Contrast.Restore();
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private CommandOutcome Run(string id) => _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: false)).AsTask().GetAwaiter().GetResult();

    private void Press(Key key, RawInputModifiers mods = RawInputModifiers.None) => TestApp.Press(_w, key, mods);

    private void Chord(Key second)
    {
        Press(Key.K, TestApp.Mod);
        Press(second, TestApp.Mod);
    }

    private HexView OpenHex(byte[] bytes, string name = "data.bin")
    {
        _w.OpenFile(_temp.Bytes(name, bytes), null);
        return SwitchToHex();
    }

    private HexView SwitchToHex()
    {
        Chord(Key.H);
        Dispatcher.UIThread.RunJobs();
        var hex = _w.Active!.Hex!;
        Assert.NotNull(hex);
        hex.SetCharset(HexCharset.Windows1252);
        hex.SetBytesPerRow(16);
        hex.Focus();
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return hex;
    }

    private void PumpUntil(Func<bool> condition)
    {
        for (int i = 0; i < 2000 && !condition(); i++)
        {
            Thread.Sleep(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition(), "announced: " + _w.AnnouncedText + " / status: " + _w.StatusLeftText);
    }

    private static HexAutomationPeer PeerOf(HexView hex) => Assert.IsType<HexAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(hex));

    /// <summary>40 bytes: N K S, a NUL, a space, a control byte, a tilde and zeros; three rows of 16.</summary>
    private static byte[] Sample()
    {
        var bytes = new byte[40];
        "NKS\0 \u0001~"u8.ToArray().CopyTo(bytes, 0);
        return bytes;
    }

    // ---- the grid ----

    [AvaloniaFact]
    public void The_grid_reads_offset_byte_value_and_character_of_several_cells_with_their_row_in_english()
    {
        var hex = OpenHex(Sample());
        var peer = PeerOf(hex);
        Loc.SetLanguage("en");

        (long At, string Value, string Help)[] cells =
        [
            (0, "offset 0x00000000, byte 4E, value 78, character N", "row 1 of 3, position 1 of 16, hex column, Edit mode: OVERWRITE"),
            (3, "offset 0x00000003, byte 00, value 0, character control character", "row 1 of 3, position 4 of 16, hex column, Edit mode: OVERWRITE"),
            (4, "offset 0x00000004, byte 20, value 32, character space", "row 1 of 3, position 5 of 16, hex column, Edit mode: OVERWRITE"),
            (5, "offset 0x00000005, byte 01, value 1, character control character", "row 1 of 3, position 6 of 16, hex column, Edit mode: OVERWRITE"),
            (6, "offset 0x00000006, byte 7E, value 126, character ~", "row 1 of 3, position 7 of 16, hex column, Edit mode: OVERWRITE"),
            (21, "offset 0x00000015, byte 00, value 0, character control character", "row 2 of 3, position 6 of 16, hex column, Edit mode: OVERWRITE"),
            (40, "offset 0x00000028, end of file", "row 3 of 3, position 9 of 16, hex column, Edit mode: OVERWRITE"),
        ];
        foreach (var (at, value, help) in cells)
        {
            hex.MoveTo(at);
            Assert.Equal(value, peer.Value);
            Assert.Equal(help, peer.GetHelpText());
        }

        hex.MoveTo(1);
        hex.ToggleColumn();
        Assert.Equal("row 1 of 3, position 2 of 16, character column, Edit mode: OVERWRITE", peer.GetHelpText());
        hex.ToggleInsert();
        Assert.Equal("row 1 of 3, position 2 of 16, character column, Edit mode: INSERT", peer.GetHelpText());
    }

    [AvaloniaFact]
    public void The_grid_is_read_in_czech_and_every_language_fills_every_placeholder()
    {
        var hex = OpenHex(Sample());
        var peer = PeerOf(hex);
        hex.MoveTo(4);
        Assert.Equal("offset 0x00000004, bajt 20, hodnota 32, znak mezera", peer.Value);
        Assert.Equal("řádek 1 z 3, pozice 5 z 16, sloupec hex, Režim úprav: PŘEPIS", peer.GetHelpText());

        hex.Select(2, 6);
        foreach (var language in A11yAudit.Languages)
        {
            Loc.SetLanguage(language);
            Assert.DoesNotContain('{', peer.Value);
            Assert.DoesNotContain('{', peer.GetHelpText());
            Assert.Contains("0x00000002", peer.Value); // the selection starts at the third byte
            Assert.Contains("0x00000005", peer.Value); // and its last byte is the sixth
        }
    }

    [AvaloniaFact]
    public void A_selection_is_read_with_its_length_and_its_first_and_last_offset_and_a_changed_byte_says_so()
    {
        var hex = OpenHex(Sample());
        var peer = PeerOf(hex);
        Loc.SetLanguage("en");

        hex.Select(2, 6);
        Assert.Equal("offset 0x00000006, byte 7E, value 126, character ~, 4 bytes selected from 0x00000002 to 0x00000005", peer.Value);
        hex.Select(2, 3);
        Assert.Equal("offset 0x00000003, byte 00, value 0, character control character, 1 byte selected from 0x00000002 to 0x00000002", peer.Value);

        hex.MoveTo(5);
        TestApp.Type(_w, "1"); // the high digit of 01 becomes 1
        Assert.Equal("offset 0x00000005, byte 11, value 17, character control character, changed", peer.Value);
    }

    [AvaloniaFact]
    public void The_grid_peer_is_a_value_that_follows_the_view_being_read_only()
    {
        var hex = OpenHex(Sample());
        var peer = PeerOf(hex);
        Assert.False(((IValueProvider)peer).IsReadOnly);
        hex.ReadOnly = true;
        Assert.True(((IValueProvider)peer).IsReadOnly);
        Assert.Equal(AutomationControlType.Edit, peer.GetAutomationControlType());
    }

    // ---- a hidden value ----

    [AvaloniaFact]
    public void A_hidden_value_is_read_as_hidden_in_every_cell_and_in_a_selection_and_never_as_bytes()
    {
        _w.OpenFile(_temp.File(".env", Env), null);
        var hex = SwitchToHex();
        Assert.True(hex.SecretsHidden);
        var peer = PeerOf(hex);
        int value = Env.IndexOf("hunter2", StringComparison.Ordinal);
        foreach (var language in A11yAudit.Languages)
        {
            Loc.SetLanguage(language);
            for (int i = value; i < value + 7; i++)
            {
                hex.MoveTo(i);
                Assert.Equal(Loc.Get(K.HexA11yHidden, ("offset", hex.OffsetText(i))), peer.Value);
                Assert.Equal(HexAutomationPeer.Position(hex), peer.GetHelpText()); // the help text says where the caret is, not what is under it
            }
        }

        Loc.SetLanguage("en");
        hex.MoveTo(value - 1);
        Assert.Equal($"offset {hex.OffsetText(value - 1)}, byte 3D, value 61, character =", peer.Value);
        hex.MoveTo(value + 7);
        Assert.StartsWith($"offset {hex.OffsetText(value + 7)}, byte 0A", peer.Value);

        hex.Select(value - 4, value + 7); // KEY=hunter2
        Assert.StartsWith($"offset {hex.OffsetText(value + 7)}, byte 0A", peer.Value);
        hex.Select(value - 4, value + 3);
        Assert.Equal($"offset {hex.OffsetText(value + 3)}, hidden value, 7 bytes selected from {hex.OffsetText(value - 4)} to {hex.OffsetText(value + 2)}", peer.Value);
        Assert.DoesNotContain("hunter", peer.Value);

        hex.ToggleSecrets();
        hex.MoveTo(value);
        Assert.Equal($"offset {hex.OffsetText(value)}, byte 68, value 104, character h", peer.Value);
    }

    // ---- announcements ----

    [AvaloniaFact]
    public void A_change_of_the_mode_and_of_the_column_is_announced_without_the_keyboard_moving()
    {
        var hex = OpenHex(Sample());

        Press(Key.Tab);
        Assert.Equal(HexColumn.Text, hex.Editor.Column);
        Assert.Equal("sloupec znaků", _w.AnnouncedText);
        Press(Key.Tab);
        Assert.Equal("sloupec hex", _w.AnnouncedText);

        Assert.Equal(CommandOutcome.Handled, Run("nks.hex.toggleInsert"));
        Assert.True(hex.Insert);
        Assert.Equal("Režim úprav: VKLÁDÁNÍ", _w.AnnouncedText);
        hex.ToggleInsert();
        Assert.Equal("Režim úprav: PŘEPIS", _w.AnnouncedText);

        Loc.SetLanguage("en");
        Assert.Equal(CommandOutcome.Handled, Run("nks.hex.toggleColumn"));
        Assert.Equal("character column", _w.AnnouncedText);
        Assert.Equal(AutomationLiveSetting.Polite, _w.AnnouncedUrgency);
    }

    [AvaloniaFact]
    public void The_result_of_a_search_and_a_search_without_a_result_are_announced()
    {
        var bytes = new byte[2000];
        (bytes[100], bytes[101], bytes[900], bytes[901]) = (0x41, 0x42, 0x41, 0x42);
        var hex = OpenHex(bytes);

        _dialogs.Prompts.Enqueue("41 42");
        Press(Key.F, TestApp.Mod);
        PumpUntil(() => _w.AnnouncedText == Loc.Get(K.HexFindCount, ("index", 1), ("total", 2)));
        Assert.Equal((100L, 102L), (hex.Editor.Selection.Start, hex.Editor.Selection.End));

        Press(Key.F3);
        PumpUntil(() => _w.AnnouncedText == Loc.Get(K.HexFindCount, ("index", 2), ("total", 2)));

        _dialogs.Prompts.Enqueue("DE AD BE EF");
        Press(Key.F, TestApp.Mod);
        PumpUntil(() => _w.AnnouncedText == Loc.Get(K.HexFindNone));
    }

    [AvaloniaFact]
    public void An_edit_in_a_view_that_is_read_only_is_refused_aloud_and_changes_nothing()
    {
        var bytes = Sample();
        var tab = _w.OpenFile(_temp.Bytes("locked.bin", bytes), null);
        tab.Doc.UserReadOnly = true; // as Open Special, Read Only does
        var hex = SwitchToHex();
        Assert.True(hex.ReadOnly);

        string refused = Loc.Get(K.HexReadOnly);
        foreach (var edit in new Action[] { () => TestApp.Type(_w, "1"), () => Press(Key.Delete), () => Press(Key.Back), () => hex.UndoRedo(redo: false), () => _ = hex.PasteAsync() })
        {
            _w.Announce("-");
            edit();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(refused, _w.AnnouncedText);
        }
        Assert.Equal(bytes, tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength)));
        Assert.False(tab.Doc.Doc.IsModified);
    }

    [AvaloniaFact]
    public void The_grids_of_a_binary_comparison_refuse_an_edit_in_the_live_note_of_the_window()
    {
        OpenHex(Sample());
        _w.CompareHexWithSaved(_w.Active!);
        Dispatcher.UIThread.RunJobs();
        var window = Assert.Single(_w.OwnedWindows.OfType<HexCompareWindow>());
        PumpUntil(() => window.Running?.IsCompleted == true);

        window.LeftView.TypeNibble(2);
        Assert.Equal(Loc.Get(K.HexReadOnly), window.NoteText);
        window.RightView.TypeText("x");
        Assert.Equal(Loc.Get(K.HexReadOnly), window.NoteText);
    }

    // ---- the keyboard ----

    [AvaloniaFact]
    public void Tab_in_a_panel_beside_the_grid_does_not_switch_the_column_and_Select_to_Offset_selects_from_the_keyboard()
    {
        var hex = OpenHex(Sample());
        hex.Inspector.FocusList();
        Dispatcher.UIThread.RunJobs();
        Assert.True(hex.Inspector.Rows.IsKeyboardFocusWithin);
        Press(Key.Tab);
        Assert.Equal(HexColumn.Hex, hex.Editor.Column);
        hex.Inspector.IsVisible = false;
        hex.Focus();

        hex.MoveTo(4);
        _dialogs.Prompts.Enqueue("+0x10");
        Chord(Key.A);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal((4L, 20L), (hex.Editor.Selection.Start, hex.Editor.Selection.End));
        Assert.Contains(hex.OffsetText(20), _dialogs.Previews[^1]);

        _dialogs.Prompts.Enqueue("end");
        Assert.Equal(CommandOutcome.Handled, Run("nks.hex.selectTo"));
        Assert.Equal((4L, 40L), (hex.Editor.Selection.Start, hex.Editor.Selection.End)); // a selection may reach the end of the file in overwrite mode too
        Assert.Same(hex, _w.FocusManager!.GetFocusedElement());

        // go to offset stays strict: the end of the file is not a place to stand on in overwrite mode
        hex.MoveTo(0);
        _dialogs.Prompts.Enqueue("end");
        Run("nks.search.gotoLine");
        Assert.Equal(0, hex.Caret);
        Assert.NotNull(MainWindow.ResolveOffset(hex, "end").Problem);
        Assert.Null(MainWindow.ResolveOffset(hex, "end", select: true).Problem);
    }

    // ---- the map of the file ----

    [AvaloniaFact]
    public void The_map_is_a_named_slider_that_the_keyboard_reaches_and_moves_and_a_reader_can_set()
    {
        var bytes = new byte[200_000];
        var hex = OpenHex(bytes);
        var map = hex.Minimap;
        var peer = Assert.IsType<HexMinimapAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(map));
        Assert.True(map.Focusable);
        Assert.Equal(AutomationControlType.Slider, peer.GetAutomationControlType());
        foreach (var language in A11yAudit.Languages)
        {
            Loc.SetLanguage(language);
            Assert.Equal(Loc.Get(K.HexMapA11y), peer.GetName());
            Assert.DoesNotContain('{', peer.GetHelpText());
        }
        Loc.SetLanguage("en");

        // the command shows the map when it is hidden and gives it the keyboard
        hex.SetMinimapVisible(false);
        Assert.Equal(CommandOutcome.Handled, Run("nks.hex.minimap.focus"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(map.IsVisible);
        Assert.Same(map, _w.FocusManager!.GetFocusedElement());

        var range = (IRangeValueProvider)peer;
        Assert.Equal((0d, 100d), (range.Minimum, range.Maximum));
        double start = range.Value;
        Press(Key.End);
        Assert.Equal(hex.LastIndex, hex.Caret);
        Assert.True(range.Value > start, "the map follows the grid to the end of the file");
        Press(Key.Home);
        Assert.Equal(0, hex.Caret);
        Press(Key.Down);
        Assert.Equal(hex.VisibleBytes, hex.Caret);
        long before = hex.Caret;
        Press(Key.PageDown);
        Assert.Equal(before + bytes.Length / 10, hex.Caret);
        Press(Key.PageUp);
        Assert.Equal(before, hex.Caret);

        string help = peer.GetHelpText()!;
        Assert.Contains(Loc.Get(K.HexMapEntropy), help);
        Assert.Contains(hex.OffsetText(hex.FirstByte), help);

        range.SetValue(50);
        Assert.InRange(hex.FirstByte, bytes.Length * 0.45, bytes.Length * 0.55);

        Press(Key.Escape);
        Assert.Same(hex, _w.FocusManager!.GetFocusedElement());
        Chord(Key.F); // the chord of the command
        Assert.Same(map, _w.FocusManager.GetFocusedElement());
    }

    // ---- the panels ----

    [AvaloniaFact]
    public void The_inspector_the_bookmarks_and_the_structure_are_named_lists_with_named_rows_in_every_language()
    {
        byte[] iend = [0, 0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0, 0, 0, 0];
        var hex = OpenHex([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. iend], "pic.png");
        hex.Marks.Add(new ByteRange(8, 12), "alpha", "#0F7B6C", "the length", "uint32 BE");
        _w.ToggleInspector();
        _w.ToggleHexBookmarkPanel();
        _w.ToggleHexTemplate();
        Dispatcher.UIThread.RunJobs();
        hex.MoveTo(8);

        (Avalonia.Controls.Control List, string Key, AutomationControlType Role)[] lists =
        [
            (hex.Inspector.Rows, K.HexInspectorTitle, AutomationControlType.List),
            (hex.BookmarkList.Rows, K.HexBmA11yList, AutomationControlType.List),
            (hex.Structure.Tree, K.KsyTitle, AutomationControlType.Tree),
        ];
        foreach (var language in A11yAudit.Languages)
        {
            Loc.SetLanguage(language);
            Dispatcher.UIThread.RunJobs();
            foreach (var (list, key, role) in lists)
            {
                Assert.True(list.IsEffectivelyVisible && list.Focusable, $"{language}: {key} is not a stop of the keyboard");
                Assert.Equal((Loc.Get(key), role), A11yAudit.Read(list));
            }
            Assert.NotEmpty(hex.Inspector.RowTexts);
            Assert.NotEmpty(hex.BookmarkList.RowTexts);
            Assert.All(hex.Inspector.RowTexts.Concat(hex.BookmarkList.RowTexts), text => Assert.False(string.IsNullOrWhiteSpace(text) || text.Contains('{'), $"{language}: row '{text}'"));
        }
    }

    // ---- high contrast ----

    [AvaloniaFact]
    public void In_high_contrast_the_classes_of_bytes_get_a_style_as_well_as_a_colour_and_the_grid_still_draws()
    {
        Assert.Equal(HexView.HexCue.Zero, HexView.ByteCue(0x00));
        Assert.Equal(HexView.HexCue.Full, HexView.ByteCue(0xFF));
        Assert.All(new byte[] { 0x01, 0x09, 0x1F, 0x7F }, b => Assert.Equal(HexView.HexCue.Control, HexView.ByteCue(b)));
        Assert.All(new byte[] { 0x20, 0x41, 0x7E, 0x80, 0xFE }, b => Assert.Equal(HexView.HexCue.None, HexView.ByteCue(b)));

        var bytes = new byte[160];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i % 5 switch { 0 => 0x00, 1 => 0xFF, 2 => 0x01, 3 => 0x41, _ => 0x7F });
        var hex = OpenHex(bytes);
        _w.UpdateLayout();
        Assert.NotNull(_w.CaptureRenderedFrame());
        Assert.Equal(0, hex.CueRuns); // no cue while the colours are the program's own

        _app.Contrast.Apply(ContrastColors.Black);
        Dispatcher.UIThread.RunJobs();
        _w.UpdateLayout();
        Assert.True(HexView.HighContrast);
        Assert.NotNull(_w.CaptureRenderedFrame());
        Assert.True(hex.CueRuns > 0, "no run of bytes got a style");

        _app.Contrast.Restore();
        Dispatcher.UIThread.RunJobs();
        _w.UpdateLayout();
        Assert.NotNull(_w.CaptureRenderedFrame());
        Assert.Equal(0, hex.CueRuns);
    }
}

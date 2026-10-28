using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Automation.Peers;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Startup;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Tests;

/// <summary>The hex view through the keyboard path of the window: switching, editing, the inspector, search, jump, save and the reader's description.</summary>
[Collection("LocState")]
public class HexTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    static HexTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public HexTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        Current = _w;
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static byte[] Sample(int length = 4096)
    {
        var bytes = new byte[length];
        new Random(5).NextBytes(bytes);
        bytes[0] = 0; // a NUL early on makes it a binary file
        return bytes;
    }

    private void Press(Key key, RawInputModifiers mods = RawInputModifiers.None) => TestApp.Press(_w, key, mods);

    private void HexChord(Key second = Key.H)
    {
        Press(Key.K, TestApp.Mod);
        Press(second, TestApp.Mod);
    }

    private DocTab OpenHex(byte[] bytes, string name = "data.bin")
    {
        var tab = _w.OpenFile(_temp.Bytes(name, bytes), null);
        HexChord();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(_w.Active!.Hex);
        _w.Active.Hex!.Focus();
        Dispatcher.UIThread.RunJobs();
        return _w.Active;
    }

    private static void Pump(Task task)
    {
        for (int i = 0; i < 2000 && !task.IsCompleted; i++)
        {
            Thread.Sleep(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static byte[] Bytes(DocTab tab) => tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength));

    // ---- switching ----

    [AvaloniaFact]
    public void A_binary_file_offers_hex_in_a_bar_and_the_chord_switches_the_view_and_back()
    {
        var bytes = Sample();
        var tab = _w.OpenFile(_temp.Bytes("a.bin", bytes), null);
        var bar = Assert.IsType<InfoBar>(tab.Bar);
        Assert.Equal(2, bar.Buttons.Count());
        Assert.Equal("Otevřít v hex", bar.Buttons.First().Content);

        HexChord();
        var hex = _w.Active!;
        Assert.NotNull(hex.Hex);
        Assert.True(hex.Doc.IsHex);
        Assert.Null(hex.Bar);
        Assert.Equal("HEX", hex.Doc.SizeBadge);
        Assert.Equal(bytes, Bytes(hex));

        HexChord();
        Assert.NotNull(_w.Active!.Surface);
        Assert.False(_w.Active.Doc.IsHex);
        Assert.Equal(bytes, Bytes(_w.Active));
    }

    [AvaloniaFact]
    public void A_text_file_is_not_offered_hex()
    {
        var tab = _w.OpenFile(_temp.File("a.txt", "hello\n"), null);
        Assert.Null(tab.Bar);
    }

    [AvaloniaFact]
    public void Switching_keeps_identity_revision_undo_the_unsaved_state_and_every_odd_byte_and_writes_nothing()
    {
        var tab = _w.NewDocument();
        var doc = tab.Doc.Doc;
        byte[] content = [0xEF, 0xBB, 0xBF, (byte)'a', 0xFF, 0xFE, (byte)'\r', (byte)'\n', (byte)'b', (byte)'\r', (byte)'c', (byte)'\n', 0xC3, 0x28, (byte)'z'];
        Assert.True(doc.Apply([ByteEdit.InsertAt(0, content)], new EditOptions { RawBytes = true }).Applied);
        var id = doc.Id;
        long revision = doc.Revision;
        int undo = doc.UndoDepth;
        var before = Directory.GetFileSystemEntries(_temp.Path);

        HexChord();
        Assert.NotNull(_w.Active!.Hex);
        Assert.Equal(id, _w.Active.Doc.Doc.Id);
        Assert.Equal(revision, _w.Active.Doc.Doc.Revision);
        Assert.True(_w.Active.Doc.Doc.IsModified);
        Assert.Equal(undo, _w.Active.Doc.Doc.UndoDepth);
        Assert.Equal(content, Bytes(_w.Active));

        HexChord();
        var back = _w.Active!;
        Assert.NotNull(back.Surface);
        Assert.Same(doc, back.Doc.Doc);
        Assert.Equal(id, back.Doc.Doc.Id);
        Assert.Equal(revision, back.Doc.Doc.Revision);
        Assert.True(back.Doc.Doc.IsModified);
        Assert.Equal(undo, back.Doc.Doc.UndoDepth);
        Assert.Equal(content, Bytes(back));
        Assert.Null(back.Doc.Doc.FilePath);
        Assert.Equal(before, Directory.GetFileSystemEntries(_temp.Path));
        Assert.NotNull(back.Doc.Doc.Undo());
        Assert.Equal([], Bytes(back));
    }

    [AvaloniaFact]
    public void A_selection_goes_through_the_bytes_in_both_directions()
    {
        var tab = _w.OpenFile(_temp.Bytes("t.txt", "The quick brown fox a€b jumps over"u8.ToArray()), null);
        _w.Active!.Bar?.ToString();
        tab.Surface!.SetSelections([new NksHub.Textida.AvaloniaEditAdapter.Sel(21, 22)]);
        HexChord();
        var hex = _w.Active!.Hex!;
        Assert.Equal(new ByteRange(21, 24), hex.Editor.Selection);
        hex.Select(26, 27);
        HexChord();
        var sel = _w.Active!.Surface!.GetSelections()[0];
        Assert.Equal((24, 25), (sel.Anchor, sel.Caret));
    }

    [AvaloniaFact]
    public void Switching_to_hex_in_a_utf16_file_keeps_the_bytes_and_returns_to_utf16()
    {
        var bytes = new UnicodeEncoding(false, true).GetPreamble().Concat(Encoding.Unicode.GetBytes("ab\r\nžluť")).ToArray();
        var tab = _w.OpenFile(_temp.Bytes("u16.txt", bytes), null);
        Assert.Equal("UTF-16 LE with BOM", tab.Doc.Doc.Codec.Name);
        HexChord();
        Assert.Equal(bytes, Bytes(_w.Active!));
        Assert.True(_w.Active!.Hex!.Editor.Document.Replace(new ByteRange(3, 4), new byte[] { 0x01 }, new EditOptions { RawBytes = true }).Applied);
        HexChord();
        Assert.NotNull(_w.Active!.Surface);
        Assert.Equal("UTF-16 LE with BOM", _w.Active.Doc.Doc.Codec.Name);
        Assert.Equal(0x01, Bytes(_w.Active)[3]);
    }

    [AvaloniaFact]
    public void After_hex_edits_of_a_paged_file_the_text_view_and_the_find_bar_read_the_edited_bytes()
    {
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        // over the 1 MiB threshold, so the file opens paged
        byte[] original = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 6000).Select(i => $"row {i} {(i % 5 == 0 ? "needle" : "hay")} č {new string('.', 200)}\n")));
        var starts = new List<int> { 0 };
        for (int i = 0; i < original.Length; i++) if (original[i] == '\n') starts.Add(i + 1);
        var opened = _w.OpenFile(_temp.Bytes("rows.txt", original), null);
        Assert.NotNull(opened.Paged);

        HexChord();
        var hex = _w.Active!.Hex!;
        hex.Focus();
        Dispatcher.UIThread.RunJobs();
        hex.MoveTo(starts[1]);
        TestApp.Type(_w, "6E6565646C65"); // "needle" over "row 1 "
        Press(Key.Insert);
        hex.MoveTo(starts[3]);
        Press(Key.Tab);
        TestApp.Type(_w, "needle");
        Press(Key.Tab);
        TestApp.Type(_w, "0A");
        byte[] want = [.. original[..starts[1]], .. "needle"u8.ToArray(), .. original[(starts[1] + 6)..starts[3]], .. "needle\n"u8.ToArray(), .. original[starts[3]..]];
        Assert.Equal(want, Bytes(_w.Active!));

        HexChord();
        var text = _w.Active!;
        Assert.False(text.Doc.IsHex);
        Assert.NotNull(text.Paged);
        var paged = text.Paged;
        Assert.Equal(want, Bytes(text));
        _w.UpdateLayout();
        PumpUntil(() => paged.Index.Complete);
        string wanted = Encoding.UTF8.GetString(want);
        var lines = new List<string>();
        for (int from = 0, i; from < wanted.Length; from = i + 1) lines.Add(wanted[from..((i = wanted.IndexOf('\n', from)) + 1)]);
        Assert.Equal(lines.Count + 1, paged.Index.Lines);
        using (var surface = new PagedTextView.TextSurface(paged))
        {
            long first = surface.FirstLine;
            Assert.InRange(3L, first, first + surface.LineCount - 1);
            Assert.Equal(string.Concat(lines.Skip((int)first).Take(surface.LineCount)), surface.GetText(0, surface.Length));
        }

        Press(Key.F, TestApp.Mod);
        Assert.True(_w.FindBar.IsVisible);
        _w.FindBar.FindBox.Text = "needle";
        Dispatcher.UIThread.RunJobs();
        PumpUntil(() => !_w.FindIsRunning);
        var expected = new List<(long, int)>();
        for (int at = 0, i; (i = want.AsSpan(at).IndexOf("needle"u8)) >= 0; at += i + 6)
            expected.Add((at + i, want.AsSpan(0, at + i).Count((byte)'\n') + 1));
        Assert.Equal(expected, _w.FindHits.OrderBy(h => h.Range.Start).Select(h => (h.Range.Start, h.Line)).ToList());
        Assert.Contains(_w.FindHits, h => h.Range.Start == starts[1]);
        Assert.Contains(_w.FindHits, h => h.Range.Start == starts[3]);
    }

    // ---- editing by keyboard ----

    [AvaloniaFact]
    public void Typing_digits_overwrites_and_one_run_is_one_undo_step_with_the_keyboard_alone()
    {
        var bytes = Sample();
        var tab = OpenHex(bytes);
        TestApp.Type(_w, "4e4B50");
        var now = Bytes(tab);
        Assert.Equal([0x4E, 0x4B, 0x50], now[..3]);
        Assert.Equal(bytes[3..], now[3..]);
        Assert.Contains("PŘEPIS", _w.HexStatusRightText);

        Press(Key.Z, TestApp.Mod);
        Assert.Equal(bytes, Bytes(tab));
        Press(Key.Y, TestApp.Mod);
        Assert.Equal([0x4E, 0x4B, 0x50], Bytes(tab)[..3]);
    }

    [AvaloniaFact]
    public void The_insert_key_switches_the_mode_in_view_and_typing_inserts()
    {
        var bytes = Sample();
        var tab = OpenHex(bytes);
        Press(Key.Insert);
        Assert.Contains("VKLÁDÁNÍ", _w.HexStatusRightText);
        TestApp.Type(_w, "A5");
        Assert.Equal(bytes.Length + 1, tab.Doc.Doc.ByteLength);
        Assert.Equal(0xA5, Bytes(tab)[0]);
        Press(Key.Insert);
        Assert.Contains("PŘEPIS", _w.HexStatusRightText);
    }

    [AvaloniaFact]
    public void Typing_in_the_character_column_writes_the_bytes_of_the_charset()
    {
        var tab = OpenHex(Sample());
        Press(Key.Tab);
        Assert.Equal(HexColumn.Text, tab.Hex!.Editor.Column);
        tab.Hex.SetCharset(HexCharset.Windows1250);
        TestApp.Type(_w, "ž");
        Assert.Equal(0x9E, Bytes(tab)[0]);
        Press(Key.Tab);
        Assert.Equal(HexColumn.Hex, tab.Hex.Editor.Column);
    }

    [AvaloniaFact]
    public void Delete_in_overwrite_mode_asks_and_zero_or_delete_is_the_users_answer()
    {
        var bytes = Sample();
        var tab = OpenHex(bytes);
        _dialogs.Answers.Enqueue(0);
        Press(Key.Delete);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(_dialogs.Asked);
        Assert.Equal(["Vynulovat", "Smazat", "Zrušit"], _dialogs.Asked[0].Buttons.Select(b => b.Label));
        Assert.Equal(0, Bytes(tab)[0]);
        Assert.Equal(bytes.Length, tab.Doc.Doc.ByteLength);

        _dialogs.Answers.Enqueue(1);
        Press(Key.Delete);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(bytes.Length - 1, tab.Doc.Doc.ByteLength);

        _dialogs.Answers.Enqueue(2);
        Press(Key.Delete);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(bytes.Length - 1, tab.Doc.Doc.ByteLength);
    }

    [AvaloniaFact]
    public void Moving_by_byte_group_row_and_page_and_selecting_all_work_without_the_mouse()
    {
        var tab = OpenHex(Sample(4096));
        var hex = tab.Hex!;
        Press(Key.Right);
        Assert.Equal(1, hex.Caret);
        Press(Key.Down);
        Assert.Equal(1 + hex.BytesPerRow, hex.Caret);
        Press(Key.Right, RawInputModifiers.Control);
        Assert.Equal(1 + hex.BytesPerRow + 4, hex.Caret);
        Press(Key.PageDown);
        Assert.Equal(1 + hex.BytesPerRow + 4 + (hex.VisibleRows - 1) * hex.BytesPerRow, hex.Caret);
        Press(Key.End);
        Assert.Equal(hex.Caret % hex.BytesPerRow, hex.BytesPerRow - 1);
        Press(Key.Home, TestApp.Mod);
        Assert.Equal(0, hex.Caret);
        Press(Key.Right, RawInputModifiers.Shift);
        Press(Key.Right, RawInputModifiers.Shift);
        Assert.Equal(new ByteRange(0, 2), hex.Editor.Selection);
        Press(Key.A, TestApp.Mod);
        Assert.Equal(new ByteRange(0, 4096), hex.Editor.Selection);
        Press(Key.End, TestApp.Mod);
        Assert.Equal(4095, hex.Caret);
    }

    // ---- jump, search ----

    [AvaloniaFact]
    public void Go_to_offset_takes_expressions_and_alt_left_comes_back()
    {
        var tab = OpenHex(Sample(10_000));
        var hex = tab.Hex!;
        _dialogs.Prompts.Enqueue("end-512");
        Press(Key.G, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(10_000 - 512, hex.Caret);
        _dialogs.Prompts.Enqueue("0x1F40");
        Press(Key.G, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0x1F40, hex.Caret);
        Press(Key.Left, RawInputModifiers.Alt);
        Assert.Equal(10_000 - 512, hex.Caret);
        _dialogs.Prompts.Enqueue("50%");
        Press(Key.G, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(5000, hex.Caret);
        _dialogs.Prompts.Enqueue("end+1");
        Press(Key.G, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(5000, hex.Caret);
        Assert.NotEqual("", _w.StatusMessageText);
    }

    [AvaloniaFact]
    public void Go_to_offset_previews_the_target_before_it_is_confirmed()
    {
        var bytes = Sample(10_000);
        var tab = OpenHex(bytes);
        var hex = tab.Hex!;
        Assert.Equal("", MainWindow.PreviewOffset(hex, " "));
        Assert.Equal($"→ 0x00001F40 (8000) · bajt {bytes[0x1F40]:X2}", MainWindow.PreviewOffset(hex, "0x1F40"));
        Assert.Equal($"→ 0x{10_000 - 512:X8} ({10_000 - 512}) · bajt {bytes[10_000 - 512]:X2}", MainWindow.PreviewOffset(hex, "end-512"));
        Assert.Contains("mimo soubor", MainWindow.PreviewOffset(hex, "end")); // overwrite has no byte at the end
        hex.ToggleInsert();
        Assert.Equal("→ 0x00002710 (10000) · konec souboru", MainWindow.PreviewOffset(hex, "end"));
        Assert.Contains("Neplatný výraz", MainWindow.PreviewOffset(hex, "end+1")); // the expression itself refuses a target past the end
        hex.ToggleInsert();
        Assert.Contains("Neplatný výraz", MainWindow.PreviewOffset(hex, "0x"));

        _dialogs.Prompts.Enqueue("4K");
        Press(Key.G, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4096, hex.Caret);
        Assert.Equal($"→ 0x00001000 (4096) · bajt {bytes[4096]:X2}", Assert.Single(_dialogs.Previews));
    }

    [AvaloniaFact]
    public void The_binary_offer_follows_the_setting_always_ask_never()
    {
        var bytes = Sample();
        _app.Workbench.Set("files.binaryAsHex", "never");
        var never = _w.OpenFile(_temp.Bytes("never.bin", bytes), null);
        Assert.Null(never.Bar);
        Assert.Null(never.Hex);

        _app.Workbench.Set("files.binaryAsHex", "always");
        var always = _w.OpenFile(_temp.Bytes("always.bin", bytes), null);
        Assert.NotNull(always.Hex);
        Assert.True(always.Doc.IsHex);
        Assert.Contains(always, _w.Tabs);
        Assert.Same(always, _w.Active);

        _app.Workbench.Set("files.binaryAsHex", "ask");
        var ask = _w.OpenFile(_temp.Bytes("ask.bin", bytes), null);
        Assert.IsType<InfoBar>(ask.Bar);
        Assert.Null(ask.Hex);
    }

    [AvaloniaFact]
    public void Switching_text_the_encoding_cannot_hold_to_hex_opens_the_inspector_and_cancel_keeps_the_text_view()
    {
        var path = _temp.Bytes("cp1250.txt", [0x61, 0x62]);
        var tab = _w.OpenFile(path, null);
        tab.Doc.Doc.ReinterpretAs(Core.Encodings.TextCodec.Cp1252);
        tab.Editor!.Document.Insert(1, "č");

        _dialogs.Answers.Enqueue(2); // cancel
        HexChord();
        Dispatcher.UIThread.RunJobs();
        var ask = Assert.Single(_dialogs.Asked);
        Assert.Equal(Loc.Get(K.LossTitle), ask.Title);
        Assert.Equal(Loc.Get(K.LossHexShow), ask.Buttons[0].Label);
        Assert.Equal(Loc.Get(K.LossChangeEncoding), ask.Buttons[1].Label);
        Assert.Contains("→ 3F", Assert.Single(ask.Details!));
        Assert.Null(_w.Active!.Hex);

        _dialogs.Answers.Enqueue(0); // show in hex
        HexChord();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(_w.Active!.Hex);
        Assert.Equal(new byte[] { 0x61, 0x3F, 0x62 }, Bytes(_w.Active));
    }

    [AvaloniaFact]
    public void Searching_for_a_masked_pattern_finds_the_same_matches_as_a_plain_pass_and_f3_steps()
    {
        var bytes = Sample(50_000);
        foreach (int at in new[] { 100, 3000, 30_000, 49_990 }) new byte[] { 0x50, 0x4B, 0x03, 0x04 }.CopyTo(bytes, at);
        var expected = new List<long>();
        for (int i = 0; i + 4 <= bytes.Length; i++)
            if (bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 3] == 0x04) expected.Add(i);
        var tab = OpenHex(bytes);
        var hex = tab.Hex!;
        Press(Key.F, TestApp.Mod);
        Assert.True(_w.FindBar.IsVisible && _w.FindBar.HexMode);
        _w.FindBar.FindBox.Text = "50 4B ?? 04";
        Dispatcher.UIThread.RunJobs();
        Press(Key.F3);
        // the count comes after the selection; a bare digit would already match the caret status ("výběr 4 B")
        PumpUntil(() => hex.Editor.HasSelection && _w.StatusLeftText.Contains($"1 z {expected.Count}"));
        Assert.Equal(expected[0], hex.Editor.Selection.Start);
        Assert.Equal(4, hex.Editor.Selection.Length);
        var seen = new List<long> { hex.Editor.Selection.Start };
        for (int i = 1; i < expected.Count; i++)
        {
            long previous = hex.Editor.Selection.Start;
            Press(Key.F3);
            PumpUntil(() => hex.Editor.Selection.Start != previous);
            seen.Add(hex.Editor.Selection.Start);
        }
        Assert.Equal(expected, seen);
        Press(Key.F3);
        PumpUntil(() => hex.Editor.Selection.Start == expected[0]);
        Press(Key.F3, RawInputModifiers.Shift);
        PumpUntil(() => hex.Editor.Selection.Start == expected[^1]);
    }

    private static MainWindow? Current;

    private static void PumpUntil(Func<bool> condition)
    {
        for (int i = 0; i < 2000 && !condition(); i++)
        {
            Thread.Sleep(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition(), "status: " + Current?.StatusLeftText);
    }

    // ---- inspector ----

    [AvaloniaFact]
    public void A_big_endian_uint32_written_in_the_inspector_is_one_undo_step()
    {
        var bytes = Sample();
        var tab = OpenHex(bytes);
        var panel = tab.Hex!.Inspector;
        HexChord(Key.I);
        Dispatcher.UIThread.RunJobs();
        Assert.True(panel.IsVisible);
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.True(panel.Rows.IsKeyboardFocusWithin);
        Assert.Contains(panel.RowTexts, t => t.StartsWith("uint32: "));

        TestApp.Press(_w, Key.B);
        Assert.True(panel.BigEndian);
        panel.Rows.SelectedIndex = 5; // uint32
        Press(Key.Enter);
        Assert.True(panel.IsEditing);
        panel.EditBox.Text = "300000000000";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.Get(K.HexInspectorRange, ("range", "0..4294967295")), panel.ErrorShown);
        panel.EditBox.Text = "0xCAFEBABE";
        Press(Key.Enter);
        Assert.False(panel.IsEditing);
        Assert.Equal([0xCA, 0xFE, 0xBA, 0xBE], Bytes(tab)[..4]);
        Assert.Contains(panel.RowTexts, t => t == "uint32: 3405691582");
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);

        Press(Key.Z, TestApp.Mod);
        Assert.Equal(bytes[..4], Bytes(tab)[..4]);
        Assert.Equal(0, tab.Doc.Doc.UndoDepth);
    }

[AvaloniaFact]
    public void A_value_cut_short_by_the_end_of_the_file_is_shown_as_incomplete_and_refused()
    {
        var tab = OpenHex(Sample(10));
        var panel = tab.Hex!.Inspector;
        tab.Hex.MoveTo(7);
        HexChord(Key.I);
        Assert.Contains(panel.RowTexts, t => t.StartsWith("uint32: ") && t.Contains("4"));
        Assert.DoesNotContain(panel.RowTexts, t => t == "uint32: 0");
        panel.Rows.SelectedIndex = 5;
        Press(Key.Enter);
        Assert.False(panel.IsEditing);
    }

    // ---- save ----

    [AvaloniaFact]
    public void Ctrl_s_saves_an_overwrite_in_place_and_a_length_change_as_a_new_file_and_undo_survives_both()
    {
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1)); // a mapped file, which is what an in-place save needs
        var bytes = Sample(3 << 20);
        var tab = OpenHex(bytes);
        string path = tab.Path!;
        TestApp.Type(_w, "FFEE");
        Press(Key.S, TestApp.Mod);
        PumpUntil(() => !tab.Doc.Doc.IsModified && !tab.Doc.Doc.IsSaving);
        var expected = (byte[])bytes.Clone();
        expected[0] = 0xFF;
        expected[1] = 0xEE;
        Assert.Equal(expected, File.ReadAllBytes(path));
        PumpUntil(() => _w.StatusLeftText.Contains("2 B"));
        Assert.False(File.Exists(path + ".textida-patch"));

        Press(Key.Insert);
        tab.Hex!.MoveTo(10);
        TestApp.Type(_w, "A5");
        Press(Key.S, TestApp.Mod);
        PumpUntil(() => !tab.Doc.Doc.IsModified && !tab.Doc.Doc.IsSaving);
        Assert.Equal(expected.Length + 1, new FileInfo(path).Length);

        Press(Key.Z, TestApp.Mod);
        Assert.Equal(expected, Bytes(tab));
        Assert.True(tab.Doc.Doc.IsModified);
        Press(Key.S, TestApp.Mod);
        PumpUntil(() => !tab.Doc.Doc.IsModified && !tab.Doc.Doc.IsSaving);
        Assert.Equal(expected, File.ReadAllBytes(path));
    }

    [AvaloniaFact]
    public void Escape_cancels_a_running_save_and_leaves_the_file_as_it_was()
    {
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        var bytes = Sample(3 << 20);
        var tab = OpenHex(bytes);
        var doc = tab.Doc.Doc;
        TestApp.Type(_w, "FF");
        using var inFlush = new ManualResetEventSlim();
        doc.InPlaceHooks = new PatchHooks { BeforeFlush = ct => { inFlush.Set(); ct.WaitHandle.WaitOne(10_000); } };
        Press(Key.S, TestApp.Mod);
        PumpUntil(() => inFlush.IsSet);
        Assert.True(doc.IsSaving);
        TestApp.Type(_w, "11");
        Assert.Equal(0xFF, Bytes(tab)[0]);
        Press(Key.Escape);
        PumpUntil(() => !doc.IsSaving);
        Assert.Equal(bytes, File.ReadAllBytes(tab.Path!));
        Assert.True(doc.IsModified);
    }

    /// <summary>A mapped file with one byte inserted, so Ctrl+S streams a new file; the save stops once the temporary file is written.</summary>
    private (DocTab Tab, byte[] Original, byte[] Saved, ManualResetEventSlim Gate) StreamSaveHeldOpen()
    {
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        var original = Sample(3 << 20);
        var tab = OpenHex(original);
        var doc = tab.Doc.Doc;
        Press(Key.Insert);
        tab.Hex!.MoveTo(10);
        TestApp.Type(_w, "A5");
        var saved = Bytes(tab);
        Assert.Equal(original.Length + 1, saved.Length);
        var written = new ManualResetEventSlim();
        var gate = new ManualResetEventSlim();
        doc.StreamSaveStep = (step, _) =>
        {
            if (step != "temp-writing") return; // runs on the worker, so the window stays usable
            written.Set();
            gate.Wait(20_000);
        };
        Press(Key.S, TestApp.Mod);
        PumpUntil(() => written.IsSet);
        Assert.True(doc.EditsDuringSave);
        return (tab, original, saved, gate);
    }

    [AvaloniaFact]
    public void Typing_and_deleting_during_a_streamed_save_leave_the_file_at_the_saved_revision_and_the_editor_unsaved()
    {
        var (tab, original, saved, gate) = StreamSaveHeldOpen();
        var doc = tab.Doc.Doc;
        long savedRevision = doc.Revision;
        TestApp.Type(_w, "7B");
        Press(Key.Delete);
        var edited = Bytes(tab);
        Assert.NotEqual(saved, edited);
        Assert.True(doc.Revision > savedRevision);
        Assert.True(doc.IsSaving);
        Assert.Contains(Loc.Get(K.HexSaving), _w.StatusLeftText);

        gate.Set();
        PumpUntil(() => !doc.IsSaving);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(saved)), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tab.Path!))));
        Assert.Equal(edited, Bytes(tab));
        Assert.True(doc.IsModified);
        Assert.Equal(savedRevision, doc.SavedRevision);
        PumpUntil(() => _w.StatusLeftText.Contains(Loc.Get(K.HexSavedEditsPending)));

        for (int i = 0; i < 5 && doc.IsModified; i++) Press(Key.Z, TestApp.Mod);
        Assert.Equal(saved, Bytes(tab));
        Assert.False(doc.IsModified);
        Press(Key.Z, TestApp.Mod);
        Assert.Equal(original, Bytes(tab));
    }

    [AvaloniaFact]
    public void Undo_and_redo_during_a_streamed_save_do_not_touch_the_file_and_the_state_after_it_is_right()
    {
        var (tab, original, saved, gate) = StreamSaveHeldOpen();
        var doc = tab.Doc.Doc;
        Press(Key.Z, TestApp.Mod);
        Assert.Equal(original, Bytes(tab));
        Assert.True(doc.IsSaving);

        gate.Set();
        PumpUntil(() => !doc.IsSaving);
        Assert.Equal(saved, File.ReadAllBytes(tab.Path!)); // the undo moved the editor only
        Assert.Equal(original, Bytes(tab));
        Assert.True(doc.IsModified);

        Press(Key.Y, TestApp.Mod);
        Assert.Equal(saved, Bytes(tab));
        Assert.False(doc.IsModified);
        Press(Key.Z, TestApp.Mod);
        Assert.Equal(original, Bytes(tab));
        Assert.True(doc.IsModified);
    }

    [AvaloniaFact]
    public void A_fill_during_a_streamed_save_is_refused_with_the_busy_message()
    {
        var (tab, _, saved, gate) = StreamSaveHeldOpen();
        var doc = tab.Doc.Doc;
        Assert.Equal(ConflictReason.Saving, doc.Fill(new ByteRange(0, 4), new byte[] { 1 }, 4).Conflict);
        gate.Set();
        PumpUntil(() => !doc.IsSaving);
        Assert.Equal(saved, Bytes(tab));
        Assert.False(doc.IsModified);
    }

    // ---- the reader ----

    [AvaloniaFact]
    public void The_screen_reader_value_names_offset_byte_value_and_character_in_the_ui_language()
    {
        var bytes = new byte[64];
        "NKS\0PK\x03\x04"u8.ToArray().CopyTo(bytes, 0);
        var tab = OpenHex(bytes);
        var hex = tab.Hex!;
        var peer = Assert.IsType<HexAutomationPeer>(ControlAutomationPeer.CreatePeerForElement(hex));
        Assert.Equal("offset 0x00000000, bajt 4E, hodnota 78, znak N", peer.Value);
        Press(Key.Right);
        Assert.Equal("offset 0x00000001, bajt 4B, hodnota 75, znak K", peer.Value);
        Press(Key.Right);
        Press(Key.Right);
        Assert.Equal("offset 0x00000003, bajt 00, hodnota 0, znak řídicí znak", peer.Value);
        Press(Key.Right, RawInputModifiers.Shift);
        Press(Key.Right, RawInputModifiers.Shift);
        Assert.EndsWith("vybrány 2 bajty od 0x00000003 do 0x00000004", peer.Value);
        TestApp.Type(_w, "1");
        Assert.EndsWith("změněno", peer.Value);
        Loc.SetLanguage("en");
        hex.MoveTo(1);
        Assert.Equal("offset 0x00000001, byte 4B, value 75, character K", peer.Value);
        hex.MoveTo(64, extend: false);
        Assert.Equal("offset 0x00000040, end of file", peer.Value);
        Assert.Equal(AutomationControlType.Edit, peer.GetAutomationControlType());
        Assert.Equal("Hex editor", peer.GetName());
    }

    // ---- drawing ----

    [AvaloniaFact]
    public void The_grid_draws_rows_with_offsets_and_the_view_survives_a_theme_change()
    {
        var tab = OpenHex(Sample(20_000));
        _w.UpdateLayout();
        var frame = _w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(tab.Hex!.VisibleRows >= 10);
        foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Dark, Avalonia.Styling.ThemeVariant.Light })
        {
            _app.RequestedThemeVariant = theme;
            _w.UpdateLayout();
            Assert.NotNull(_w.CaptureRenderedFrame());
        }
    }

    [AvaloniaFact]
    [Trait("Category", "Perf")]
    public void Scrolling_a_64_by_16_viewport_draws_within_the_budget()
    {
        var tab = OpenHex(Sample(20_000_000), "big.bin");
        var hex = tab.Hex!;
        _w.Width = 1800;
        _w.Height = 1300;
        _w.UpdateLayout();
        var times = new List<double>();
        var random = new Random(1);
        for (int i = 0; i < 300; i++)
        {
            hex.MoveTo(random.NextInt64(0, 19_000_000));
            hex.Center(hex.Caret);
            _w.UpdateLayout();
            _w.CaptureRenderedFrame();
            times.Add(hex.RenderMs);
        }
        times.Sort();
        double p95 = times[(int)(times.Count * 0.95)];
        Assert.True(hex.VisibleRows >= 40, $"viewport only {hex.VisibleRows} rows");
        Assert.True(p95 <= 33, $"p95 {p95:0.0} ms over {hex.VisibleRows} rows");
    }

    [AvaloniaFact]
    public void Open_request_with_hex_and_offset_opens_the_file_on_that_byte()
    {
        string path = _temp.Bytes("o.bin", Sample(20_000));
        var task = _app.OpenRequestAsync(new IpcRequest { Files = [new IpcFile { Path = path, Hex = true, Offset = 0x1F40 }] });
        Pump(task);
        var hex = _w.Active!.Hex!;
        Assert.Equal(0x1F40, hex.Caret);
        Assert.Contains(0x1F40.ToString("X8"), _w.StatusLeftText);
    }
}

public class HexCommandLineTests
{
    [Fact]
    public void The_command_line_takes_hex_and_offset_like_line()
    {
        string cwd = Path.GetTempPath();
        var a = CommandLine.Parse(["--hex", "a.bin", "b.bin"], cwd);
        Assert.Equal([true, false], a.Files.Select(f => f.Hex));
        var b = CommandLine.Parse(["a.bin", "--hex", "--offset", "0x1F40"], cwd);
        Assert.True(b.Files[0].Hex);
        Assert.Equal(0x1F40, b.Files[0].Offset);
        var c = CommandLine.Parse(["--offset=8000", "a.bin"], cwd);
        Assert.Equal((true, 8000L), (c.Files[0].Hex, c.Files[0].Offset));
        Assert.NotNull(CommandLine.Parse(["--offset", "x", "a.bin"], cwd).Error);
        Assert.NotNull(CommandLine.Parse(["--hex"], cwd).Error);
        Assert.Null(CommandLine.Parse(["--line", "3", "a.bin"], cwd).Files[0].Offset);
    }
}

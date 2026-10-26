using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Keys;

namespace NksHub.Textida.App.Tests;

/// <summary>The editor surface: AvaloniaEdit over the document contract, typing, keys through the registry, selections and clipboard.</summary>
[Collection("LocState")]
public class EditorTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    static EditorTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public EditorTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private DocTab Open(string text)
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = text;
        Dispatcher.UIThread.RunJobs();
        tab.Editor.TextArea.Focus();
        tab.Editor.CaretOffset = 0;
        return tab;
    }

    private static string Text(DocTab t) => t.Editor!.Document.Text;

    private static string Bytes(DocTab t) => Encoding.UTF8.GetString(t.Doc.Doc.ReadBytes(new ByteRange(0, t.Doc.Doc.ByteLength)));

    private void Press(Key key, RawInputModifiers mods = RawInputModifiers.None) => TestApp.Press(_w, key, mods);

    private void Mod(Key key, bool shift = false) => TestApp.Press(_w, key, TestApp.Mod | (shift ? RawInputModifiers.Shift : RawInputModifiers.None));

    [AvaloniaFact]
    public void Surface_is_AvaloniaEdit_over_the_document_with_line_numbers_and_current_line()
    {
        var tab = Open("one\ntwo");
        Assert.IsAssignableFrom<TextEditor>(tab.Editor);
        Assert.True(tab.Editor!.ShowLineNumbers);
        Assert.True(tab.Editor.Options.HighlightCurrentLine);
        Assert.Same(tab.Doc.TextBackend!.TextDocument, tab.Editor.Document);
        Assert.Contains("Cascadia", tab.Editor.FontFamily.Name);
        Assert.Equal(14, tab.Editor.FontSize);
        Assert.Contains(tab.Editor.TextArea.LeftMargins, m => m is LineNumberMargin);
        Assert.NotNull(tab.Editor.TextArea.TextView.CurrentLineBackground);
    }

    [AvaloniaFact]
    public void Shift_right_selects_a_character_and_typing_replaces_it()
    {
        var tab = Open("Příliš");
        Press(Key.Home, RawInputModifiers.Control);
        Press(Key.Right);
        Press(Key.Right, RawInputModifiers.Shift);
        Assert.Equal("ř", tab.Editor!.SelectedText);
        TestApp.Type(_w, "r");
        Assert.Equal("Příliš".Replace("ř", "r"), Text(tab));
    }

    [AvaloniaFact]
    public void A_new_or_opened_tab_has_the_keyboard_without_a_click()
    {
        var tab = _w.NewDocument();
        Dispatcher.UIThread.RunJobs();
        Assert.Same(tab.Editor!.TextArea, _w.FocusManager!.GetFocusedElement());
        TestApp.Type(_w, "typed at once");
        Assert.Equal("typed at once", Text(tab));

        var opened = _w.OpenFile(_temp.File("focus.txt", "abc"), null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(opened.Editor!.TextArea, _w.FocusManager!.GetFocusedElement());
        Assert.True(_w.EditorHasFocus);
    }

    [AvaloniaFact]
    public void Surface_follows_the_theme_light_and_dark()
    {
        var tab = Open("x");
        _app.SetTheme("light");
        Dispatcher.UIThread.RunJobs();
        var light = ((Avalonia.Media.ISolidColorBrush)tab.Editor!.Background!).Color;
        _app.SetTheme("dark");
        Dispatcher.UIThread.RunJobs();
        var dark = ((Avalonia.Media.ISolidColorBrush)tab.Editor.Background!).Color;
        Assert.NotEqual(light, dark);
        Assert.True(dark.R < 60, "dark editor background");
        Assert.Equal(Avalonia.Media.FontWeight.Normal, tab.Editor.FontWeight);
        _app.SetTheme("system");
    }

    [AvaloniaFact]
    public void Typing_reaches_the_document_bytes_and_undo_takes_a_run_back_in_one_step()
    {
        var tab = Open("");
        TestApp.Type(_w, "hello");
        Assert.Equal("hello", Bytes(tab));
        Assert.True(tab.Doc.HasUnsavedWork);
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
        Mod(Key.Z);
        Assert.Equal("", Text(tab));
        Mod(Key.Y);
        Assert.Equal("hello", Text(tab));
        Assert.Equal(5, tab.Editor!.CaretOffset);
    }

    [AvaloniaFact]
    public void One_changed_character_in_a_windows_1250_file_changes_one_byte()
    {
        byte[] original = [0x50, 0xF8, 0xED, 0x6C, 0x69, 0x9A, 0x0D, 0x0A, 0x32];
        var path = _temp.Bytes("cp.txt", original);
        var tab = _w.OpenFile(path, null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        tab.Editor.Select(1, 1);
        TestApp.Type(_w, "r");
        Mod(Key.S);
        var saved = File.ReadAllBytes(path);
        var expected = (byte[])original.Clone();
        expected[1] = 0x72;
        Assert.Equal(expected, saved);
        Assert.False(tab.Doc.Doc.IsModified);
    }

    [AvaloniaFact]
    public void Line_commands_run_from_the_keymap_and_each_is_one_undo_step()
    {
        var tab = Open("a\nb\nc");
        tab.Editor!.CaretOffset = 2; // on b
        Press(Key.Down, RawInputModifiers.Alt);
        Assert.Equal("a\nc\nb", Text(tab));
        Press(Key.Up, RawInputModifiers.Alt);
        Assert.Equal("a\nb\nc", Text(tab));
        Mod(Key.D, shift: true);
        Assert.Equal("a\nb\nb\nc", Text(tab));
        Mod(Key.Z);
        Assert.Equal("a\nb\nc", Text(tab));
        Mod(Key.K, shift: true);
        Assert.Equal("a\nc", Text(tab));
        Mod(Key.Z);
        Assert.Equal("a\nb\nc", Text(tab));
    }

    [AvaloniaFact]
    public void Join_lines_and_select_line_have_keys()
    {
        var tab = Open("a\n   b\nc");
        Mod(Key.J, shift: true);
        Assert.Equal("a b\nc", Text(tab));
        Mod(Key.L);
        Assert.Equal("a b\n", tab.Editor!.SelectedText);
        Mod(Key.L);
        Assert.Equal("a b\nc", tab.Editor.SelectedText);
    }

    [AvaloniaFact]
    public void Tab_inserts_to_the_next_stop_and_indents_a_multi_line_selection()
    {
        var tab = Open("ab\ncd");
        tab.Editor!.CaretOffset = 2;
        Press(Key.Tab);
        Assert.Equal("ab  \ncd", Text(tab));
        tab.Editor.Select(0, Text(tab).Length);
        Press(Key.Tab);
        Assert.Equal("    ab  \n    cd", Text(tab));
        Press(Key.Tab, RawInputModifiers.Shift);
        Assert.Equal("ab  \ncd", Text(tab));
        Mod(Key.Z);
        Mod(Key.Z);
        Assert.Equal("ab  \ncd", Text(tab));
    }

    [AvaloniaFact]
    public void Tab_writes_a_tab_character_when_the_setting_says_so()
    {
        _app.Workbench.Set("editor.insertSpaces", System.Text.Json.Nodes.JsonValue.Create(false));
        var tab = Open("x");
        Press(Key.Tab);
        Assert.Equal("\tx", Text(tab));
    }

    [AvaloniaFact]
    public void Case_commands_run_from_a_two_chord_sequence()
    {
        var tab = Open("hello world");
        tab.Editor!.Select(0, 5);
        Mod(Key.K);
        Mod(Key.U);
        Assert.Equal("HELLO world", Text(tab));
        Mod(Key.K);
        Mod(Key.L);
        Assert.Equal("hello world", Text(tab));
    }

    [AvaloniaFact]
    public void Enter_keeps_indentation_the_line_ending_of_the_document_and_continues_comments()
    {
        var tab = Open("  // note");
        tab.Editor!.CaretOffset = tab.Editor.Document.TextLength;
        tab.Doc.Language = LanguageModes.ById("csharp");
        Press(Key.Enter);
        Assert.Equal("  // note" + tab.Doc.NewLine(null) + "  // ", Text(tab));

        var crlf = Open("  x\r\ny");
        crlf.Editor!.CaretOffset = 3;
        crlf.Doc.FlushEol();
        Press(Key.Enter);
        Assert.Equal("  x\r\n  \r\ny", Text(crlf));
        Mod(Key.Z);
        Assert.Equal("  x\r\ny", Text(crlf));
    }

    [AvaloniaFact]
    public void Enter_in_an_empty_comment_ends_it_and_comments_can_be_switched_off()
    {
        var tab = Open("// ");
        tab.Doc.Language = LanguageModes.ById("javascript");
        tab.Editor!.CaretOffset = 3;
        Press(Key.Enter);
        Assert.Equal(tab.Doc.NewLine(null), Text(tab));

        _app.Workbench.Set("editor.continueComments", System.Text.Json.Nodes.JsonValue.Create(false));
        var plain = Open("// x");
        plain.Doc.Language = LanguageModes.ById("javascript");
        plain.Editor!.CaretOffset = 4;
        Press(Key.Enter);
        Assert.Equal("// x" + plain.Doc.NewLine(null), Text(plain));
    }

    [AvaloniaFact]
    public void Paired_characters_are_added_typed_over_and_wrapped()
    {
        var tab = Open("");
        TestApp.Type(_w, "(");
        Assert.Equal("()", Text(tab));
        Assert.Equal(1, tab.Editor!.CaretOffset);
        TestApp.Type(_w, "a)");
        Assert.Equal("(a)", Text(tab));
        Assert.Equal(3, tab.Editor.CaretOffset);

        tab.Editor.Select(1, 1);
        Assert.Equal("a", tab.Editor.SelectedText);
        TestApp.Type(_w, "\"");
        Assert.Equal("(\"a\")", Text(tab));
        Assert.Equal("a", tab.Editor.SelectedText);

        _app.Workbench.Set("editor.autoClosePairs", System.Text.Json.Nodes.JsonValue.Create(false));
        var off = Open("");
        TestApp.Type(_w, "[");
        Assert.Equal("[", Text(off));
    }

    [AvaloniaFact]
    public void Backspace_between_a_pair_removes_both()
    {
        var tab = Open("");
        TestApp.Type(_w, "{");
        Assert.Equal("{}", Text(tab));
        Press(Key.Back);
        Assert.Equal("", Text(tab));
    }

    [AvaloniaFact]
    public void F8_starts_a_block_selection_and_typing_edits_every_row_in_one_undo_step()
    {
        var tab = Open("abc\ndef\nghi");
        tab.Editor!.CaretOffset = 1;
        Press(Key.F8);
        Assert.True(tab.Editor.BlockMode);
        Press(Key.Down);
        Press(Key.Down);
        Assert.IsType<RectangleSelection>(tab.Editor.TextArea.Selection);
        Assert.Equal(3, tab.Editor.GetSelections().Count);

        TestApp.Type(_w, "X");
        Assert.Equal("aXbc\ndXef\ngXhi", Text(tab));
        Mod(Key.Z);
        Assert.Equal("abc\ndef\nghi", Text(tab));
    }

    [AvaloniaFact]
    public void Block_selection_widens_with_shift_arrows_and_applies_case_to_the_rectangle()
    {
        var tab = Open("abcd\nefgh\nijkl");
        tab.Editor!.CaretOffset = 1;
        Press(Key.F8);
        Press(Key.Down);
        Press(Key.Right);
        Press(Key.Right);
        Press(Key.Down);
        var rows = tab.Editor.GetSelections();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(2, r.Length));
        Mod(Key.K);
        Mod(Key.U);
        Assert.Equal("aBCd\neFGh\niJKl", Text(tab));
        Mod(Key.Z);
        Assert.Equal("abcd\nefgh\nijkl", Text(tab));
        Press(Key.Escape);
        Assert.False(tab.Editor.BlockMode);
    }

    [AvaloniaFact]
    public void Alt_drag_makes_a_rectangular_selection()
    {
        var tab = Open("abcdef\nabcdef\nabcdef");
        _w.UpdateLayout();
        var view = tab.Editor!.TextArea.TextView;
        view.EnsureVisualLines();
        Point At(int line, int column) => view.TranslatePoint(view.GetVisualPosition(new TextViewPosition(line, column), VisualYPosition.LineMiddle), _w)!.Value;
        var from = At(1, 2);
        var to = At(3, 5);
        _w.MouseDown(from, MouseButton.Left, RawInputModifiers.Alt);
        _w.MouseMove(new Point(to.X, from.Y + 1), RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
        _w.MouseMove(to, RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
        _w.MouseUp(to, MouseButton.Left, RawInputModifiers.Alt);
        Assert.IsType<RectangleSelection>(tab.Editor.TextArea.Selection);
        Assert.Equal(3, tab.Editor.GetSelections().Count);
    }

    [AvaloniaFact]
    public void Paste_follows_the_line_endings_of_the_target()
    {
        var lf = Open("x\ny");
        lf.Doc.FlushEol();
        lf.Editor!.CaretOffset = 1;
        _w.PasteText(lf.Editor, "a\r\nb");
        Assert.Equal("xa\nb\ny", Text(lf));

        var crlf = Open("x\r\ny");
        crlf.Doc.FlushEol();
        crlf.Editor!.CaretOffset = 1;
        _w.PasteText(crlf.Editor, "a\nb");
        Assert.Equal("xa\r\nb\r\ny", Text(crlf));
        Mod(Key.Z);
        Assert.Equal("x\r\ny", Text(crlf));
    }

    [AvaloniaFact]
    public async Task Paste_command_reads_the_clipboard_and_cut_copy_use_it()
    {
        var tab = Open("hello");
        tab.Editor!.Select(0, 5);
        Mod(Key.C);
        await Task.Delay(150); // the copy reaches the clipboard asynchronously
        Dispatcher.UIThread.RunJobs();
        tab.Editor.TextArea.ClearSelection();
        tab.Editor.CaretOffset = 5;
        var probe = await _w.Clipboard!.TryGetTextAsync();
        Assert.Equal("hello", probe);
        var direct = await _w.PasteAsync();
        Assert.Equal(CommandOutcome.Handled, direct);
        Assert.Equal("hellohello", Text(tab));
        Mod(Key.Z);
        Mod(Key.V);
        for (int i = 0; i < 40 && Text(tab) == "hello"; i++)
        {
            await Task.Delay(25);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal("hellohello", Text(tab));
        tab.Editor.Select(0, 5);
        Mod(Key.X);
        Assert.Equal("hello", Text(tab));
    }

    [AvaloniaFact]
    public void Multi_line_paste_into_a_block_puts_one_line_in_each_row()
    {
        var tab = Open("..\n..\n..\n..");
        tab.Editor!.CaretOffset = 1;
        Press(Key.F8);
        Press(Key.Down);
        _w.PasteText(tab.Editor, "A\nB\nC");
        Assert.Equal(".A.\n.B.\n.C.\n..", Text(tab));
        Mod(Key.Z);
        Assert.Equal("..\n..\n..\n..", Text(tab));
    }

    [AvaloniaFact]
    public void Single_line_paste_into_a_block_goes_into_every_row()
    {
        var tab = Open("ab\ncd\nef");
        tab.Editor!.CaretOffset = 1;
        Press(Key.F8);
        Press(Key.Down);
        Press(Key.Down);
        _w.PasteText(tab.Editor, "-");
        Assert.Equal("a-b\nc-d\ne-f", Text(tab));
    }

    [AvaloniaFact]
    public async Task Dropped_file_opens_in_a_new_tab()
    {
        var file = _temp.File("dropped.txt", "dropped text");
        var item = Avalonia.Input.DataTransferItem.CreateFile((await _w.StorageProvider.TryGetFileFromPathAsync(new Uri(file)))!);
        var transfer = new DataTransfer();
        transfer.Add(item);
        Open("existing");
        _w.DragDrop(new Point(200, 200), Avalonia.Input.Raw.RawDragEventType.DragEnter, transfer, DragDropEffects.Copy);
        _w.DragDrop(new Point(200, 200), Avalonia.Input.Raw.RawDragEventType.DragOver, transfer, DragDropEffects.Copy);
        _w.DragDrop(new Point(200, 200), Avalonia.Input.Raw.RawDragEventType.Drop, transfer, DragDropEffects.Copy);
        Assert.Contains(_w.Tabs, t => t.Path == file);
        Assert.Equal(2, _w.Tabs.Count());
    }

    [AvaloniaFact]
    public async Task Unbound_key_with_a_command_that_declines_falls_back_to_the_next_command()
    {
        var tab = Open("x");
        var ctx = new ShellContext(_w, forMenu: false);
        // Tab without a multi-line selection is declined by nks.edit.tab and handled by its fallback nks.edit.insertIndent
        var outcome = await _app.Workbench.Commands.ExecuteAsync("nks.edit.tab", ctx);
        Assert.Equal(CommandOutcome.Handled, outcome);
        Assert.StartsWith("    ", Text(tab));
    }

    [AvaloniaFact]
    public void Automation_hook_lets_the_accessibility_library_supply_the_peer()
    {
        var provider = new RecordingProvider();
        EditorAutomationHook.Provider = provider;
        try
        {
            var tab = Open("x");
            _ = Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(tab.Editor!);
            Assert.Same(tab.Editor, provider.Asked);
        }
        finally { EditorAutomationHook.Provider = null; }
    }

    [AvaloniaFact]
    public void Typing_in_a_one_mebibyte_file_stays_within_33_ms_at_p95()
    {
        var sb = new StringBuilder();
        for (int i = 0; sb.Length < 1 << 20; i++) sb.Append("line ").Append(i).Append(" lorem ipsum dolor sit amet consectetur\n");
        var tab = _w.OpenFile(_temp.File("one-mib.txt", sb.ToString()), null);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        tab.Editor.CaretOffset = tab.Editor.Document.GetOffset(2000, 1);
        _w.UpdateLayout();
        for (int i = 0; i < 30; i++) { TestApp.Type(_w, "w"); Dispatcher.UIThread.RunJobs(); } // warm up

        var times = new List<double>();
        for (int i = 0; i < 300; i++)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            TestApp.Type(_w, "x");
            _w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            times.Add(System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
        }
        times.Sort();
        double p95 = times[(int)(times.Count * 0.95)];
        Assert.True(p95 <= 33, $"typing p95 {p95:0.0} ms (median {times[times.Count / 2]:0.0} ms)");
        Assert.Equal(1, tab.Doc.Doc.UndoDepth); // 330 keystrokes at one place are one undo step
    }

    private sealed class RecordingProvider : IEditorAutomationProvider
    {
        public Control? Asked { get; private set; }

        public Avalonia.Automation.Peers.AutomationPeer? CreatePeer(Control editor, Document document)
        {
            Asked = editor;
            return null;
        }
    }
}

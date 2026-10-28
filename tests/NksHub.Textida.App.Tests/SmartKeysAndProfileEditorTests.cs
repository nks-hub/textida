using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;

namespace NksHub.Textida.App.Tests;

/// <summary>P2.2.2 smart keys for web files and P2.2.6 the "pspad" profile, on the running editor with real key and mouse input.</summary>
[Collection("LocState")]
public class SmartKeysAndProfileEditorTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public SmartKeysAndProfileEditorTests()
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

    private DocTab Open(string text, string language = "plaintext")
    {
        var tab = _w.NewDocument();
        tab.Doc.ChooseLanguage(LanguageModes.ById(language));
        tab.Editor!.Document.Text = text;
        Dispatcher.UIThread.RunJobs();
        tab.Doc.FlushEol();
        tab.Editor.TextArea.Focus();
        tab.Editor.CaretOffset = 0;
        return tab;
    }

    private static string Text(DocTab t) => t.Editor!.Document.Text;

    private static string Bytes(DocTab t) => Encoding.UTF8.GetString(t.Doc.Doc.ReadBytes(new ByteRange(0, t.Doc.Doc.ByteLength)));

    private static int Steps(DocTab t) => t.Doc.Doc.UndoDepth - 1;

    private void Press(Key key, RawInputModifiers mods = RawInputModifiers.None) => TestApp.Press(_w, key, mods);

    private void Mod(Key key, bool shift = false) => TestApp.Press(_w, key, TestApp.Mod | (shift ? RawInputModifiers.Shift : RawInputModifiers.None));

    private void Setting(string key, bool value) => _app.Workbench.Set(key, JsonValue.Create(value));

    private static void End(DocTab t) => t.Editor!.CaretOffset = t.Editor.Document.TextLength;

    // ---- </ ----

    [AvaloniaFact]
    public void Typing_a_slash_after_an_open_tag_completes_the_closing_tag()
    {
        var tab = Open("<div><p>x</p>", "html");
        End(tab);
        TestApp.Type(_w, "</");
        Assert.Equal("<div><p>x</p></div>", Text(tab));
        Assert.Equal("<div><p>x</p></div>", Bytes(tab));
        Assert.Equal(tab.Editor!.Document.TextLength, tab.Editor.CaretOffset);
    }

    [AvaloniaFact]
    public void Typing_a_slash_inside_a_string_or_with_the_setting_off_completes_nothing()
    {
        var tab = Open("<div title=\"a", "html");
        End(tab);
        TestApp.Type(_w, "</");
        Assert.Equal("<div title=\"a</", Text(tab));

        var off = Open("<div>", "html");
        End(off);
        Setting("editor.smartKeys.closeTag", false);
        TestApp.Type(_w, "</");
        Assert.Equal("<div></", Text(off));
    }

    // ---- renaming ----

    [AvaloniaFact]
    public void Retyping_a_tag_name_renames_the_closing_tag_and_one_undo_takes_the_whole_rename_back()
    {
        var tab = Open("<div class=\"a\">x</div>", "html");
        tab.Editor!.Select(1, 3);
        TestApp.Type(_w, "section");
        Assert.Equal("<section class=\"a\">x</section>", Text(tab));
        Assert.Equal("<section class=\"a\">x</section>", Bytes(tab));
        Assert.Equal(1, Steps(tab));
        Mod(Key.Z);
        Assert.Equal("<div class=\"a\">x</div>", Text(tab));
    }

    [AvaloniaFact]
    public void Editing_the_closing_tag_renames_the_opening_one_and_the_setting_switches_it_off()
    {
        var tab = Open("<ul><li>a</li></ul>", "html");
        tab.Editor!.CaretOffset = 18; // after "ul" of </ul>
        TestApp.Type(_w, "x");
        Assert.Equal("<ulx><li>a</li></ulx>", Text(tab));

        var off = Open("<ul>a</ul>", "html");
        Setting("editor.smartKeys.renameTag", false);
        off.Editor!.CaretOffset = 3;
        TestApp.Type(_w, "x");
        Assert.Equal("<ulx>a</ul>", Text(off));
    }

    // ---- pasting into a string ----

    [AvaloniaFact]
    public void Pasting_into_a_string_escapes_the_quotes_and_pasting_elsewhere_does_not()
    {
        var tab = Open("var s = \"\";", "javascript");
        tab.Editor!.CaretOffset = 9;
        _w.PasteText(tab.Editor, "say \"hi\"");
        Assert.Equal("var s = \"say \\\"hi\\\"\";", Text(tab));

        var plain = Open("var s = ;", "javascript");
        plain.Editor!.CaretOffset = 8;
        _w.PasteText(plain.Editor, "say \"hi\"");
        Assert.Equal("var s = say \"hi\";", Text(plain));

        var off = Open("var s = \"\";", "javascript");
        Setting("editor.smartKeys.escapeInString", false);
        off.Editor!.CaretOffset = 9;
        _w.PasteText(off.Editor, "a\"b");
        Assert.Equal("var s = \"a\"b\";", Text(off));
    }

    // ---- JSON comma ----

    [AvaloniaFact]
    public void Enter_after_a_json_value_adds_the_comma_when_another_value_follows()
    {
        var tab = Open("{\n  \"a\": 1\n  \"b\": 2\n}", "json");
        tab.Editor!.CaretOffset = 10; // after the 1
        Press(Key.Enter);
        Assert.Equal("{\n  \"a\": 1,\n  \n  \"b\": 2\n}", Text(tab));
        Assert.Equal("{\n  \"a\": 1,\n  \n  \"b\": 2\n}", Bytes(tab));
        Assert.Equal(14, tab.Editor.CaretOffset);
        Mod(Key.Z);
        Assert.Equal("{\n  \"a\": 1\n  \"b\": 2\n}", Text(tab));

        var last = Open("{\n  \"a\": 1\n}", "json");
        last.Editor!.CaretOffset = 10;
        Press(Key.Enter);
        Assert.Equal("{\n  \"a\": 1\n  \n}", Text(last));
    }

    // ---- -> in PHP ----

    [AvaloniaFact]
    public void A_minus_right_after_a_php_variable_adds_the_arrow()
    {
        var tab = Open("<?php $obj", "php");
        End(tab);
        TestApp.Type(_w, "-");
        Assert.Equal("<?php $obj->", Text(tab));
        Assert.Equal(12, tab.Editor!.CaretOffset);

        var plain = Open("<?php 5", "php");
        End(plain);
        TestApp.Type(_w, "-");
        Assert.Equal("<?php 5-", Text(plain));
    }

    // ---- BEGIN / END ----

    [AvaloniaFact]
    public void Enter_after_begin_in_sql_adds_the_end_line_and_leaves_the_caret_between()
    {
        var tab = Open("BEGIN", "sql");
        End(tab);
        Press(Key.Enter);
        string nl = tab.Doc.NewLine(null);
        Assert.Equal($"BEGIN{nl}    {nl}END", Text(tab));
        Assert.Equal(5 + nl.Length + 4, tab.Editor!.CaretOffset);
        Mod(Key.Z);
        Assert.Equal("BEGIN", Text(tab));
    }

    // ---- PHP variable by double click ----

    [AvaloniaFact]
    public void A_double_click_on_a_php_variable_selects_its_name_without_the_dollar_sign()
    {
        var tab = Open("<?php echo $userName;", "php");
        _w.UpdateLayout();
        var view = tab.Editor!.TextArea.TextView;
        view.EnsureVisualLines();
        var point = view.TranslatePoint(view.GetVisualPosition(new TextViewPosition(1, 15), VisualYPosition.LineMiddle), _w)!.Value; // inside userName
        _w.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        _w.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        _w.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        _w.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Assert.Equal("userName", tab.Editor.SelectedText);
    }

    // ---- profile ----

    private void UsePspad() => _app.Workbench.Set("editor.profile", BehaviourProfiles.Pspad);

    [AvaloniaFact]
    public void Switching_the_profile_changes_the_keys_and_back_restores_them()
    {
        var tab = Open("one\ntwo\nthree");
        tab.Editor!.CaretOffset = 5;
        UsePspad();
        Mod(Key.Y); // Ctrl+Y deletes the line in PSPad
        Assert.Equal("one\nthree", Text(tab));
        _app.Workbench.Set("editor.profile", BehaviourProfiles.Default);
        Mod(Key.Z);
        Assert.Equal("one\ntwo\nthree", Text(tab));
        TestApp.Redo(_w); // and is redo again
        Assert.Equal("one\nthree", Text(tab));
        Assert.Equal(BehaviourProfiles.Default, _app.Workbench.Profile);
    }

    [AvaloniaFact]
    public void The_profile_menu_commands_switch_the_profile_and_show_which_one_is_on()
    {
        Open("x");
        Assert.Equal(CommandOutcome.Handled, CommandOutcomeOf("nks.settings.profile.pspad"));
        Assert.Equal(BehaviourProfiles.Pspad, _app.Workbench.Profile);
        Assert.Equal(CommandOutcome.Handled, CommandOutcomeOf("nks.settings.profile.default"));
        Assert.Equal(BehaviourProfiles.Default, _app.Workbench.Profile);
    }

    private CommandOutcome CommandOutcomeOf(string id) =>
        _app.Workbench.Commands.ExecuteAsync(id, new ShellContext(_w, forMenu: false)).AsTask().GetAwaiter().GetResult();

    [AvaloniaFact]
    public void Home_and_End_toggle_between_the_text_and_the_edge_of_the_line_in_the_pspad_profile()
    {
        UsePspad();
        var tab = Open("    foo");
        End(tab);
        int[] home = new int[3];
        for (int i = 0; i < 3; i++) { Press(Key.Home); home[i] = tab.Editor!.CaretOffset; }
        Assert.Equal([4, 0, 4], home);

        var end = Open("foo   ");
        End(end);
        int[] ends = new int[3];
        for (int i = 0; i < 3; i++) { Press(Key.End); ends[i] = end.Editor!.CaretOffset; }
        Assert.Equal([3, 6, 3], ends);
        Press(Key.Home, RawInputModifiers.Shift);
        Assert.Equal("foo", end.Editor!.SelectedText);
    }

    [AvaloniaFact]
    public void Backspace_at_the_end_of_an_indentation_removes_back_to_the_tab_stop_in_the_pspad_profile()
    {
        var tab = Open("        x");
        tab.Editor!.CaretOffset = 8;
        Press(Key.Back);
        Assert.Equal("       x", Text(tab)); // the default removes one space
        Mod(Key.Z);

        UsePspad();
        var pspad = Open("        x");
        pspad.Editor!.CaretOffset = 8;
        Press(Key.Back);
        Assert.Equal("    x", Text(pspad));
        Assert.Equal("    x", Bytes(pspad));
        Press(Key.Back);
        Assert.Equal("x", Text(pspad));
        Mod(Key.Z); // both Backspaces are one typing run
        Assert.Equal("        x", Text(pspad));
    }

    [AvaloniaFact]
    public void Paired_characters_follow_the_rules_of_the_original_in_the_pspad_profile()
    {
        var tab = Open(")");
        tab.Editor!.CaretOffset = 0;
        TestApp.Type(_w, "(");
        Assert.Equal("())", Text(tab)); // the default closes before a closing bracket

        UsePspad();
        var pspad = Open(")");
        pspad.Editor!.CaretOffset = 0;
        TestApp.Type(_w, "(");
        Assert.Equal("()", Text(pspad)); // nothing blank follows: no pair

        var quote = Open("a");
        End(quote);
        TestApp.Type(_w, "\"");
        Assert.Equal("a\"", Text(quote)); // a line of two characters gets none

        var typedOver = Open("x)");
        typedOver.Editor!.CaretOffset = 1;
        TestApp.Type(_w, ")");
        Assert.Equal("x)", Text(typedOver)); // a closing character that is there is typed over, though nothing added it
        Assert.Equal(2, typedOver.Editor.CaretOffset);
    }

    [AvaloniaFact]
    public void F8_sets_a_mark_and_the_second_F8_selects_up_to_the_caret_in_the_pspad_profile()
    {
        UsePspad();
        var tab = Open("hello world");
        tab.Editor!.CaretOffset = 2;
        Press(Key.F8);
        Assert.False(tab.Editor.BlockMode);
        for (int i = 0; i < 3; i++) Press(Key.Right);
        Press(Key.F8);
        Assert.Equal("llo", tab.Editor.SelectedText);
        Assert.Equal(5, tab.Editor.CaretOffset);
        Assert.False(tab.Editor.HasMark); // the mark is used up: the next F8 sets a new one
        Press(Key.F8);
        Assert.True(tab.Editor.HasMark);
    }

    [AvaloniaFact]
    public void F8_starts_a_block_selection_in_the_default_profile_and_Ctrl_Shift_C_does_in_pspad()
    {
        var tab = Open("abc\ndef");
        Press(Key.F8);
        Assert.True(tab.Editor!.BlockMode);
        Press(Key.Escape);

        UsePspad();
        var pspad = Open("abc\ndef");
        Mod(Key.C, shift: true);
        Assert.True(pspad.Editor!.BlockMode);
    }

    [AvaloniaFact]
    public void Colliding_keys_of_the_original_do_what_the_original_does_in_the_pspad_profile()
    {
        var tab = Open("a\nb\nc");
        tab.Editor!.CaretOffset = 2;
        Press(Key.Down, RawInputModifiers.Alt);
        Assert.Equal("a\nc\nb", Text(tab)); // the default: move the line
        Mod(Key.Z);

        UsePspad();
        var pspad = Open("a\nb\nc");
        pspad.Editor!.CaretOffset = 2;
        Press(Key.Down, RawInputModifiers.Alt);
        Assert.Equal("a\nb\nc", Text(pspad)); // free for bookmarks there
        bool wrap = _w.Options.WordWrap;
        Mod(Key.W); // Ctrl+W is word wrap there, not close
        Assert.Contains(pspad, _w.Tabs);
        Assert.Equal(!wrap, _w.Options.WordWrap);
        Mod(Key.I, shift: true); // indent
        Assert.Equal("a\n    b\nc", Text(pspad));
    }

    [AvaloniaFact]
    public void Alt_drag_makes_a_block_selection_in_the_pspad_profile_and_carets_in_the_default_one()
    {
        UsePspad();
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
        Assert.False(tab.Editor.IsMulti);
    }
}

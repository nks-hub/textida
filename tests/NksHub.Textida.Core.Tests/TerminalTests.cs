using System.Diagnostics;
using System.Text;
using NksHub.Textida.Core.Terminal;

namespace NksHub.Textida.Core.Tests;

/// <summary>PZ.6.1: the screen a terminal draws, the keys it sends, and a real shell on a pseudo terminal (ConPTY here, a pty elsewhere).</summary>
public class TerminalTests
{
    private static TerminalScreen Screen(string output, int columns = 20, int rows = 5)
    {
        var screen = new TerminalScreen(columns, rows);
        screen.Feed(output);
        return screen;
    }

    [Fact]
    public void Text_lines_and_the_cursor_follow_the_controls()
    {
        var screen = Screen("one\r\ntwo\r\nab\bX");

        Assert.Equal("one\ntwo\naX", screen.ScreenText());
        Assert.Equal((2, 2), (screen.CursorX, screen.CursorY));
    }

    [Fact]
    public void Lines_wrap_at_the_right_edge_and_the_top_line_goes_to_the_scrollback_when_the_screen_scrolls()
    {
        var screen = Screen("0123456789012345678901234\r\n2\r\n3\r\n4\r\n5", columns: 20, rows: 4);

        Assert.Equal(2, screen.ScrollbackCount); // the wrapped first line took two rows and the fifth line pushed them up
        Assert.Equal("01234567890123456789", screen.TextOfLine(0));
        Assert.Equal("2\n3\n4\n5", screen.ScreenText().Replace("\n\n", "\n").TrimStart('\n').Replace("01234\n", ""));
    }

    [Fact]
    public void Cursor_addressing_erasing_and_colours_are_applied()
    {
        var screen = Screen("\u001b[2J\u001b[3;5HHi\u001b[1;31mR\u001b[0mN\u001b[1;1Hx\u001b[K");

        Assert.Equal("x\n\n    HiRN", screen.ScreenText());
        var row = screen.Line(screen.ScrollbackCount + 2);
        Assert.Equal((CellAttributes.Bold, TerminalColor.Indexed(1)), (row[6].Attributes, row[6].Foreground));
        Assert.Equal((CellAttributes.None, TerminalColor.Default), (row[7].Attributes, row[7].Foreground));
        screen.Feed("\u001b[3;9H\u001b[38;2;10;20;30mZ\u001b[48;5;200mY");
        var y = screen.Line(screen.ScrollbackCount + 2)[9];
        Assert.Equal(TerminalColor.Rgb(10, 20, 30), screen.Line(screen.ScrollbackCount + 2)[8].Foreground);
        Assert.Equal(TerminalColor.Indexed(200), y.Background);
    }

    [Fact]
    public void The_alternate_screen_keeps_the_main_screen_and_the_replies_a_program_waits_for_are_given()
    {
        var screen = Screen("main text");
        var replies = new List<string>();
        screen.Reply = replies.Add;

        screen.Feed("\u001b[?1049h\u001b[Hfull screen\u001b[?25l\u001b[6n\u001b[?2004h\u001b[?1h");
        Assert.Equal("full screen", screen.ScreenText());
        Assert.False(screen.CursorVisible);
        Assert.True(screen.BracketedPaste && screen.ApplicationCursor);
        Assert.Equal(["\u001b[1;12R"], replies);

        screen.Feed("\u001b[?1049l");
        Assert.Equal("main text", screen.ScreenText());
        screen.Feed("\u001b[c");
        Assert.Contains("\u001b[?62;c", replies);
    }

    [Fact]
    public void A_title_in_an_osc_sequence_is_kept_and_a_sequence_cut_in_the_middle_of_a_chunk_is_finished_in_the_next()
    {
        var screen = new TerminalScreen(20, 3);
        var bytes = Encoding.UTF8.GetBytes("\u001b]0;my title\u0007é\u001b[31mred");
        screen.Feed(bytes.AsSpan(0, 5));
        screen.Feed(bytes.AsSpan(5, 15)); // ends inside the two bytes of the letter
        screen.Feed(bytes.AsSpan(20));

        Assert.Equal("my title", screen.Title);
        Assert.Equal("éred", screen.ScreenText());
    }

    [Fact]
    public void Resizing_keeps_the_text_and_the_cursor_line_and_inserting_lines_works_inside_a_scroll_region()
    {
        var screen = Screen("a\r\nb\r\nc\r\nd", columns: 10, rows: 4);
        screen.Resize(6, 2);
        Assert.Equal(("c\nd", 2, 1), (screen.ScreenText(), screen.ScrollbackCount, screen.CursorY));
        screen.Resize(12, 4);
        Assert.Equal((12, 4), (screen.Columns, screen.Rows));

        var region = Screen("\u001b[1;3r\u001b[2;1Hx\u001b[L", columns: 5, rows: 4);
        Assert.Equal("\n\nx", region.ScreenText());
    }

    [Theory]
    [InlineData(TerminalKey.Up, TerminalModifiers.None, false, "\u001b[A")]
    [InlineData(TerminalKey.Up, TerminalModifiers.None, true, "\u001bOA")]
    [InlineData(TerminalKey.Left, TerminalModifiers.Ctrl, false, "\u001b[1;5D")]
    [InlineData(TerminalKey.Delete, TerminalModifiers.None, false, "\u001b[3~")]
    [InlineData(TerminalKey.PageUp, TerminalModifiers.Shift | TerminalModifiers.Alt, false, "\u001b[5;4~")]
    [InlineData(TerminalKey.F5, TerminalModifiers.None, false, "\u001b[15~")]
    [InlineData(TerminalKey.F1, TerminalModifiers.None, false, "\u001bOP")]
    [InlineData(TerminalKey.Enter, TerminalModifiers.None, false, "\r")]
    [InlineData(TerminalKey.Backspace, TerminalModifiers.None, false, "\u007f")]
    [InlineData(TerminalKey.Tab, TerminalModifiers.Shift, false, "\u001b[Z")]
    public void Special_keys_send_the_xterm_sequences(TerminalKey key, TerminalModifiers modifiers, bool application, string expected) =>
        Assert.Equal(expected, TerminalKeys.Sequence(key, modifiers, application));

    [Fact]
    public void Control_characters_and_bracketed_paste()
    {
        Assert.Equal("\u0003", TerminalKeys.Control('c'));
        Assert.Equal("\u001b", TerminalKeys.Control('['));
        Assert.Null(TerminalKeys.Control('1'));
        Assert.Equal("\u001b[200~a\nb\u001b[201~", TerminalKeys.Paste("a\r\nb\u001b[201~", bracketed: true));
        Assert.Equal("a\rb", TerminalKeys.Paste("a\r\nb", bracketed: false));
    }

    [Fact]
    public void A_command_with_quotes_is_split_into_the_program_and_its_arguments()
    {
        Assert.Equal(["C:\\Program Files\\Git\\bin\\bash.exe", "--login", "-i"], PseudoTerminal.SplitCommand("\"C:\\Program Files\\Git\\bin\\bash.exe\" --login -i"));
        Assert.Empty(PseudoTerminal.SplitCommand("   "));
    }

    private static async Task<bool> Until(Func<bool> condition, int timeoutMs = 20_000)
    {
        for (var sw = Stopwatch.StartNew(); sw.ElapsedMilliseconds < timeoutMs; await Task.Delay(50))
            if (condition()) return true;
        return condition();
    }

    [Fact]
    public async Task A_real_shell_runs_takes_input_follows_a_resize_and_ends_with_the_session()
    {
        if (!OperatingSystem.IsWindows()) return; // the pty of macOS and Linux is checked on those systems
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        using var session = TerminalSession.Start(cmd, ["/q"], Path.GetTempPath(), null, 80, 20);
        Assert.True(await Until(() => session.Screen.ScreenText().Contains('>')), "the prompt: " + session.Screen.ScreenText());

        session.Send("echo hello from the terminal" + TerminalKeys.Sequence(TerminalKey.Enter, TerminalModifiers.None, false));
        Assert.True(await Until(() => session.Screen.ScreenText().Split('\n').Count(l => l.Contains("hello from the terminal")) >= 2), "the echo: " + session.Screen.ScreenText());

        session.Resize(100, 30);
        Assert.Equal((100, 30), (session.Screen.Columns, session.Screen.Rows));
        session.Send("mode con" + TerminalKeys.Sequence(TerminalKey.Enter, TerminalModifiers.None, false));
        Assert.True(await Until(() => session.Screen.ScreenText().Contains("Columns:") && session.Screen.ScreenText().Contains("100")), "the console follows the resize: " + session.Screen.ScreenText());

        int id = session.ProcessId;
        Assert.True(session.Running);
        session.Send("exit" + TerminalKeys.Sequence(TerminalKey.Enter, TerminalModifiers.None, false));
        Assert.True(await Until(() => !session.Running), "the shell ends on exit");
        Assert.Equal(0, session.ExitCode);

        using var again = TerminalSession.Start(cmd, ["/q"], Path.GetTempPath(), null, 80, 20);
        int second = again.ProcessId;
        again.Dispose(); // closing the terminal ends the process
        Assert.True(await Until(() => !Process.GetProcesses().Any(p => p.Id == second)), "the process is gone after Dispose");
        Assert.NotEqual(id, second);
    }
}

using System.Text;

namespace NksHub.Textida.Core.Terminal;

public enum TerminalKey { Enter, Backspace, Tab, Escape, Up, Down, Left, Right, Home, End, PageUp, PageDown, Insert, Delete, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12 }

[Flags]
public enum TerminalModifiers { None = 0, Shift = 1, Alt = 2, Ctrl = 4 }

/// <summary>What a terminal sends to a program for a key (xterm conventions, which every shell and editor in a terminal expects).</summary>
public static class TerminalKeys
{
    /// <summary>The bytes for a special key; <paramref name="applicationCursor"/> is the mode a full-screen program sets, in which the arrows and Home/End use the <c>ESC O</c> form.</summary>
    public static string Sequence(TerminalKey key, TerminalModifiers modifiers, bool applicationCursor)
    {
        int mod = 1 + ((modifiers & TerminalModifiers.Shift) != 0 ? 1 : 0) + ((modifiers & TerminalModifiers.Alt) != 0 ? 2 : 0) + ((modifiers & TerminalModifiers.Ctrl) != 0 ? 4 : 0);
        bool plain = mod == 1;
        string arrow(char final) => plain ? (applicationCursor ? "\u001bO" : "\u001b[") + final : $"\u001b[1;{mod}{final}";
        string tilde(int code) => plain ? $"\u001b[{code}~" : $"\u001b[{code};{mod}~";
        string function(char final) => plain ? "\u001bO" + final : $"\u001b[1;{mod}{final}";
        return key switch
        {
            TerminalKey.Enter => (modifiers & TerminalModifiers.Alt) != 0 ? "\u001b\r" : "\r",
            TerminalKey.Backspace => (modifiers & TerminalModifiers.Ctrl) != 0 ? "\b" : (modifiers & TerminalModifiers.Alt) != 0 ? "\u001b\u007f" : "\u007f",
            TerminalKey.Tab => (modifiers & TerminalModifiers.Shift) != 0 ? "\u001b[Z" : "\t",
            TerminalKey.Escape => "\u001b",
            TerminalKey.Up => arrow('A'),
            TerminalKey.Down => arrow('B'),
            TerminalKey.Right => arrow('C'),
            TerminalKey.Left => arrow('D'),
            TerminalKey.Home => arrow('H'),
            TerminalKey.End => arrow('F'),
            TerminalKey.Insert => tilde(2),
            TerminalKey.Delete => tilde(3),
            TerminalKey.PageUp => tilde(5),
            TerminalKey.PageDown => tilde(6),
            TerminalKey.F1 => function('P'),
            TerminalKey.F2 => function('Q'),
            TerminalKey.F3 => function('R'),
            TerminalKey.F4 => function('S'),
            TerminalKey.F5 => tilde(15),
            TerminalKey.F6 => tilde(17),
            TerminalKey.F7 => tilde(18),
            TerminalKey.F8 => tilde(19),
            TerminalKey.F9 => tilde(20),
            TerminalKey.F10 => tilde(21),
            TerminalKey.F11 => tilde(23),
            _ => tilde(24),
        };
    }

    /// <summary>The control character for Ctrl with a letter or one of <c>@ [ \ ] ^ _ ?</c> and space; null for a key that has none.</summary>
    public static string? Control(char c)
    {
        c = char.ToUpperInvariant(c);
        if (c is >= 'A' and <= 'Z') return ((char)(c - 'A' + 1)).ToString();
        return c switch { '@' or ' ' => "\0", '[' => "\u001b", '\\' => "\u001c", ']' => "\u001d", '^' => "\u001e", '_' => "\u001f", '?' => "\u007f", _ => null };
    }

    /// <summary>Text to paste: wrapped in the bracketed paste markers when the program asked for them (it then knows the text was not typed), else with line breaks as Enter. A marker inside the text is removed.</summary>
    public static string Paste(string text, bool bracketed)
    {
        text = text.Replace("\u001b[200~", "", StringComparison.Ordinal).Replace("\u001b[201~", "", StringComparison.Ordinal).ReplaceLineEndings("\n");
        return bracketed ? "\u001b[200~" + text + "\u001b[201~" : text.Replace("\n", "\r", StringComparison.Ordinal);
    }
}

/// <summary>
/// A shell on a pseudo terminal with the screen its output draws (PZ.6.1). The output is fed to the <see cref="Screen"/> on the thread of the reader and <see cref="Changed"/> is raised after each chunk; what the screen has to answer
/// (the cursor position a program asks for) goes back to the program at once. <see cref="Dispose"/> ends the shell and everything it started.
/// </summary>
public sealed class TerminalSession : IDisposable
{
    private readonly IPseudoTerminal _pty;
    private bool _disposed;

    private TerminalSession(IPseudoTerminal pty, TerminalScreen screen)
    {
        _pty = pty;
        Screen = screen;
        screen.Reply = reply => _pty.Write(Encoding.UTF8.GetBytes(reply));
        pty.OutputReceived += chunk =>
        {
            screen.Feed(chunk.Span);
            Changed?.Invoke();
        };
        pty.Exited += code =>
        {
            ExitCode = code;
            Ended?.Invoke(code);
            Changed?.Invoke();
        };
    }

    public TerminalScreen Screen { get; }
    public int ProcessId => _pty.ProcessId;
    public bool Running => !_disposed && !_pty.HasExited;
    public int? ExitCode { get => field ?? _pty.ExitCode; private set; }

    /// <summary>Raised on a background thread after the screen changed.</summary>
    public event Action? Changed;

    /// <summary>Raised on a background thread when the shell has ended.</summary>
    public event Action<int>? Ended;

    public static TerminalSession Start(string program, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string>? environment, int columns, int rows)
    {
        var screen = new TerminalScreen(columns, rows);
        var pty = PseudoTerminal.Start(program, arguments, workingDirectory, environment, columns, rows);
        return new TerminalSession(pty, screen);
    }

    /// <summary>Sends typed text, or a sequence of <see cref="TerminalKeys"/>, to the program.</summary>
    public void Send(string text)
    {
        if (_disposed || text.Length == 0) return;
        _pty.Write(Encoding.UTF8.GetBytes(text));
    }

    public void Resize(int columns, int rows)
    {
        if (_disposed) return;
        Screen.Resize(columns, rows);
        _pty.Resize(Screen.Columns, Screen.Rows);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pty.Dispose();
    }
}

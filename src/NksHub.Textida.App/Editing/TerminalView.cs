using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Terminal;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The screen of a <see cref="TerminalSession"/> drawn in cells (PZ.6.1), and the keyboard of the shell: keys and typed text go to the program, the size of the control sets the size of the terminal, the wheel and Shift+Page keys scroll the
/// scrollback, a drag selects text and Ctrl+Shift+C (⌘C on macOS) copies it, Ctrl+Shift+V, Shift+Insert and ⌘V paste. Every key goes to the shell while the terminal has the focus, except the few the window keeps (<see cref="IsWindowKey"/>).
/// </summary>
internal sealed class TerminalView : Control
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");
    private const double FontSize = 13;

    private static readonly uint[] Ansi =
    [
        0x000000, 0xCD3131, 0x0DBC79, 0xE5E510, 0x2472C8, 0xBC3FBC, 0x11A8CD, 0xE5E5E5,
        0x666666, 0xF14C4C, 0x23D18B, 0xF5F543, 0x3B8EEA, 0xD670D6, 0x29B8DB, 0xFFFFFF,
    ];

    private readonly Typeface _face = new(Mono);
    private TerminalSession? _session;
    private double _cellWidth = 8, _cellHeight = 16;
    private int _scroll;
    private (int Line, int Column)? _anchor, _caret;
    private bool _dragging, _redrawQueued;
    private long _drawnVersion = -1;

    public TerminalView()
    {
        Focusable = true;
        IsTabStop = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        MeasureCell();
        GotFocus += (_, _) => InvalidateVisual();
        LostFocus += (_, _) => InvalidateVisual();
        AutomationProperties.SetName(this, Loc.Get(K.TerminalPanel));
    }

    public TerminalSession? Session => _session;

    /// <summary>Raised on the interface thread when the shell ended.</summary>
    public event Action<int>? SessionEnded;

    public int Columns => Bounds.Width < _cellWidth ? 80 : Math.Max(2, (int)(Bounds.Width / _cellWidth));
    public int Rows => Bounds.Height < _cellHeight ? 24 : Math.Max(1, (int)(Bounds.Height / _cellHeight));

    /// <summary>Shows a session; the view takes over its size.</summary>
    public void Attach(TerminalSession session)
    {
        Detach();
        _session = session;
        _scroll = 0;
        _anchor = _caret = null;
        session.Changed += OnChanged;
        session.Ended += OnEnded;
        session.Resize(Columns, Rows);
        InvalidateVisual();
    }

    public void Detach()
    {
        if (_session is not { } session) return;
        session.Changed -= OnChanged;
        session.Ended -= OnEnded;
        _session = null;
    }

    private void OnEnded(int code) => Dispatcher.UIThread.Post(() => SessionEnded?.Invoke(code));

    private void OnChanged()
    {
        if (_redrawQueued) return;
        _redrawQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _redrawQueued = false;
            InvalidateVisual();
        }, DispatcherPriority.Background);
    }

    // ---- size ----

    private void MeasureCell()
    {
        var sample = new FormattedText(new string('M', 20), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _face, FontSize, Brushes.Black);
        _cellWidth = Math.Max(1, sample.Width / 20);
        _cellHeight = Math.Max(1, Math.Ceiling(sample.Height));
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 640 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 240 : availableSize.Height);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _session?.Resize(Columns, Rows);
        InvalidateVisual();
    }

    // ---- drawing ----

    private bool Dark => ActualThemeVariant == ThemeVariant.Dark;

    private IBrush DefaultForeground => Dark ? Brushes.Gainsboro : Brushes.Black;
    private Color DefaultBackground => Dark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Color.FromRgb(0xFA, 0xFA, 0xFA);

    private Color ColorOf(TerminalColor color, bool foreground)
    {
        if (color.IsRgb)
        {
            var (r, g, b) = color.RgbValue;
            return Color.FromRgb(r, g, b);
        }
        if (!color.IsIndexed) return foreground ? ((ISolidColorBrush)DefaultForeground).Color : DefaultBackground;
        int i = color.Index;
        if (i < 16) return From(Ansi[i]);
        if (i >= 232) { byte v = (byte)(8 + (i - 232) * 10); return Color.FromRgb(v, v, v); }
        i -= 16;
        static byte Level(int n) => (byte)(n == 0 ? 0 : 55 + n * 40);
        return Color.FromRgb(Level(i / 36), Level(i / 6 % 6), Level(i % 6));
    }

    private static Color From(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new SolidColorBrush(DefaultBackground), new Rect(Bounds.Size));
        if (_session is not { } session) return;
        var screen = session.Screen;
        lock (screen.Gate)
        {
            _drawnVersion = screen.Version;
            int rows = Math.Min(screen.Rows, Rows), first = screen.LineCount - screen.Rows - Math.Clamp(_scroll, 0, screen.ScrollbackCount);
            var selection = Selection();
            for (int y = 0; y < rows; y++)
            {
                int line = first + y;
                if (line < 0 || line >= screen.LineCount) continue;
                DrawLine(context, screen.Line(line), y, line, selection);
            }
            if (screen.CursorVisible && _scroll == 0 && IsFocused)
            {
                var cursor = new Rect(screen.CursorX * _cellWidth, screen.CursorY * _cellHeight, _cellWidth, _cellHeight);
                context.FillRectangle(new SolidColorBrush(Dark ? Colors.White : Colors.Black, 0.55), cursor);
            }
            else if (screen.CursorVisible && _scroll == 0)
                context.DrawRectangle(new Pen(DefaultForeground, 1), new Rect(screen.CursorX * _cellWidth + 0.5, screen.CursorY * _cellHeight + 0.5, _cellWidth - 1, _cellHeight - 1));
        }
    }

    private void DrawLine(DrawingContext context, Cell[] cells, int y, int line, ((int Line, int Column) Start, (int Line, int Column) End)? selection)
    {
        double top = y * _cellHeight;
        var run = new StringBuilder();
        int runStart = 0;
        Cell runCell = default;
        void Flush()
        {
            if (run.Length == 0) return;
            bool inverse = (runCell.Attributes & CellAttributes.Inverse) != 0;
            var foreground = ColorOf(inverse ? runCell.Background : runCell.Foreground, !inverse);
            if (inverse && runCell.Background.IsDefault) foreground = DefaultBackground;
            if (runCell.Foreground.IsDefault && inverse) foreground = DefaultBackground;
            var brush = new SolidColorBrush(foreground, (runCell.Attributes & CellAttributes.Dim) != 0 ? 0.6 : 1);
            var text = new FormattedText(run.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Mono, (runCell.Attributes & CellAttributes.Italic) != 0 ? FontStyle.Italic : FontStyle.Normal, (runCell.Attributes & CellAttributes.Bold) != 0 ? FontWeight.Bold : FontWeight.Normal), FontSize, brush);
            if ((runCell.Attributes & CellAttributes.Underline) != 0) text.SetTextDecorations(TextDecorations.Underline);
            if ((runCell.Attributes & CellAttributes.Strike) != 0) text.SetTextDecorations(TextDecorations.Strikethrough);
            context.DrawText(text, new Point(runStart * _cellWidth, top));
            run.Clear();
        }
        for (int x = 0; x < cells.Length; x++)
        {
            var cell = cells[x];
            bool inverse = (cell.Attributes & CellAttributes.Inverse) != 0;
            var background = inverse ? (cell.Foreground.IsDefault ? ((ISolidColorBrush)DefaultForeground).Color : ColorOf(cell.Foreground, true)) : (cell.Background.IsDefault ? default : ColorOf(cell.Background, false));
            bool selected = selection is { } s && Within(line, x, s.Start, s.End);
            if (selected) background = Dark ? Color.FromRgb(0x26, 0x4F, 0x78) : Color.FromRgb(0xAD, 0xD6, 0xFF);
            if (selected || inverse || !cell.Background.IsDefault) context.FillRectangle(new SolidColorBrush(background), new Rect(x * _cellWidth, top, _cellWidth * (x + 1 < cells.Length && cells[x + 1].Rune == 0 ? 2 : 1), _cellHeight));
            if (cell.Rune == 0) continue;
            bool wide = x + 1 < cells.Length && cells[x + 1].Rune == 0;
            if (cell.Rune == ' ' && (cell.Attributes & (CellAttributes.Underline | CellAttributes.Strike)) == 0) { Flush(); continue; }
            if (run.Length > 0 && (cell.Foreground != runCell.Foreground || cell.Background != runCell.Background || cell.Attributes != runCell.Attributes || x != runStart + run.Length)) Flush();
            if (run.Length == 0) { runStart = x; runCell = cell; }
            run.Append(char.ConvertFromUtf32(cell.Rune));
            if (wide) Flush();
        }
        Flush();
    }

    // ---- selection and scrolling ----

    private static bool Within(int line, int column, (int Line, int Column) start, (int Line, int Column) end) =>
        (line > start.Line || (line == start.Line && column >= start.Column)) && (line < end.Line || (line == end.Line && column <= end.Column));

    private ((int Line, int Column) Start, (int Line, int Column) End)? Selection()
    {
        if (_anchor is not { } a || _caret is not { } c || a == c) return null;
        return (a.Line, a.Column).CompareTo((c.Line, c.Column)) <= 0 ? (a, c) : (c, a);
    }

    private (int Line, int Column) CellAt(Point point)
    {
        var screen = _session!.Screen;
        int y = Math.Clamp((int)(point.Y / _cellHeight), 0, screen.Rows - 1), x = Math.Clamp((int)(point.X / _cellWidth), 0, screen.Columns - 1);
        return (screen.LineCount - screen.Rows - Math.Clamp(_scroll, 0, screen.ScrollbackCount) + y, x);
    }

    /// <summary>The selected text; empty when nothing is selected.</summary>
    public string SelectedText()
    {
        if (_session is not { } session || Selection() is not { } s) return "";
        lock (session.Screen.Gate)
        {
            var sb = new StringBuilder();
            for (int line = s.Start.Line; line <= s.End.Line && line < session.Screen.LineCount; line++)
            {
                var cells = session.Screen.Line(line);
                int from = line == s.Start.Line ? s.Start.Column : 0, to = line == s.End.Line ? Math.Min(s.End.Column, cells.Length - 1) : cells.Length - 1;
                var text = new StringBuilder();
                for (int x = from; x <= to; x++) if (cells[x].Rune != 0) text.Append(char.ConvertFromUtf32(cells[x].Rune));
                sb.Append(line == s.End.Line ? text.ToString() : text.ToString().TrimEnd()).Append(line == s.End.Line ? "" : "\n");
            }
            return sb.ToString();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_session is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _anchor = _caret = CellAt(e.GetPosition(this));
        _dragging = true;
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || _session is null) return;
        _caret = CellAt(e.GetPosition(this));
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_session is null) return;
        ScrollBy((int)Math.Round(e.Delta.Y * 3));
        e.Handled = true;
    }

    public void ScrollBy(int lines)
    {
        if (_session is null) return;
        _scroll = Math.Clamp(_scroll + lines, 0, _session.Screen.ScrollbackCount);
        InvalidateVisual();
    }

    // ---- keyboard ----

    /// <summary>The keys the window keeps even while the terminal has the focus: the one that shows and hides the terminal, and the one that moves between the parts of the window.</summary>
    public static bool IsWindowKey(Key key, KeyModifiers modifiers) =>
        (key == Key.OemTilde && (modifiers & ~KeyModifiers.Shift) == KeyModifiers.Control) || (key == Key.F6 && (modifiers & ~KeyModifiers.Shift) == KeyModifiers.None);

    private static TerminalKey? Special(Key key) => key switch
    {
        Key.Enter => TerminalKey.Enter, Key.Back => TerminalKey.Backspace, Key.Tab => TerminalKey.Tab, Key.Escape => TerminalKey.Escape,
        Key.Up => TerminalKey.Up, Key.Down => TerminalKey.Down, Key.Left => TerminalKey.Left, Key.Right => TerminalKey.Right,
        Key.Home => TerminalKey.Home, Key.End => TerminalKey.End, Key.PageUp => TerminalKey.PageUp, Key.PageDown => TerminalKey.PageDown,
        Key.Insert => TerminalKey.Insert, Key.Delete => TerminalKey.Delete,
        >= Key.F1 and <= Key.F12 => TerminalKey.F1 + (key - Key.F1),
        _ => null,
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_session is not { } session || !session.Running) return;
        bool mac = OperatingSystem.IsMacOS();
        bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0, ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0, alt = (e.KeyModifiers & KeyModifiers.Alt) != 0, meta = (e.KeyModifiers & KeyModifiers.Meta) != 0;
        if (mac ? meta && e.Key == Key.C : ctrl && shift && e.Key == Key.C)
        {
            if (SelectedText() is { Length: > 0 } text) _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);
            e.Handled = true;
            return;
        }
        if ((mac ? meta && e.Key == Key.V : ctrl && shift && e.Key == Key.V) || (shift && e.Key == Key.Insert))
        {
            _ = PasteAsync();
            e.Handled = true;
            return;
        }
        if (shift && e.Key is Key.PageUp or Key.PageDown)
        {
            ScrollBy((e.Key == Key.PageUp ? 1 : -1) * Math.Max(1, session.Screen.Rows - 1));
            e.Handled = true;
            return;
        }
        var mods = (shift ? TerminalModifiers.Shift : 0) | (alt ? TerminalModifiers.Alt : 0) | (ctrl ? TerminalModifiers.Ctrl : 0);
        string? send = null;
        if (Special(e.Key) is { } key) send = TerminalKeys.Sequence(key, mods, session.Screen.ApplicationCursor);
        else if (ctrl && !alt && !meta && e.Key is >= Key.A and <= Key.Z) send = TerminalKeys.Control((char)('A' + (e.Key - Key.A)));
        else if (ctrl && !alt && !meta && e.Key == Key.Space) send = "\0";
        else if (alt && !ctrl && !meta && e.KeySymbol is { Length: 1 } symbol && !char.IsControl(symbol[0])) send = "\u001b" + symbol;
        if (send is null) return;
        _scroll = 0;
        session.Send(send);
        e.Handled = true;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (_session is not { } session || !session.Running || string.IsNullOrEmpty(e.Text)) return;
        _scroll = 0;
        session.Send(e.Text);
        e.Handled = true;
    }

    private async Task PasteAsync()
    {
        if (_session is not { } session || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        if (await clipboard.TryGetTextAsync() is { Length: > 0 } text) session.Send(TerminalKeys.Paste(text, session.Screen.BracketedPaste));
    }

    /// <summary>Sends text to the shell as pasted text (for tests and for the commands that hand a line to the terminal).</summary>
    public void Paste(string text) => _session?.Send(TerminalKeys.Paste(text, _session.Screen.BracketedPaste));
}

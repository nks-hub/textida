using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>
/// Hides the ANSI escape sequences of a log in the picture: each complete sequence is drawn as one zero-width mark. Only the picture changes; the text, the
/// offsets and the bytes stay, so saving writes the escapes back exactly as they were.
/// </summary>
public sealed class AnsiHideGenerator : VisualLineElementGenerator
{
    private const string ZeroWidth = "​";

    /// <summary>False shows the escapes as they are in the file.</summary>
    public bool Enabled { get; set; }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!Enabled) return -1;
        int lineEnd = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
        if (startOffset >= lineEnd) return -1;
        string text = CurrentContext.Document.GetText(startOffset, lineEnd - startOffset);
        for (int i = text.IndexOf('\x1b'); i >= 0; i = text.IndexOf('\x1b', i + 1))
            if (AnsiSgr.Sequence(text, i) is not null) return startOffset + i;
        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        int lineEnd = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
        string text = CurrentContext.Document.GetText(offset, Math.Min(lineEnd - offset, 64));
        return AnsiSgr.Sequence(text, 0) is { } length ? new FormattedTextElement(ZeroWidth, length) : null;
    }
}

/// <summary>
/// Colors the text between ANSI escape sequences: 16, 256 and true colors, bold and underline. A style that a line leaves open goes on into the next lines until a
/// code changes it, as in a terminal (see <see cref="AnsiCarry"/>); with <see cref="Lookback"/> at 0 each line is read on its own.
/// </summary>
public sealed class AnsiColorizer : DocumentColorizingTransformer
{
    private const int MaxChars = 8192;

    private readonly TextView view;
    private readonly AnsiCarry _carry = new(0);
    private TextDocument? _watched;
    private bool enabled;

    public AnsiColorizer(TextView view)
    {
        this.view = view;
        view.DocumentChanged += (_, e) => Watch(e.NewDocument);
        Watch(view.Document);
    }

    /// <summary>The most lines before a line that are read to find the style it starts with; 0 reads each line on its own.</summary>
    public int Lookback
    {
        get => _carry.Lookback;
        set
        {
            if (_carry.Lookback == value) return;
            _carry.Lookback = value;
            _carry.Clear();
            view.Redraw();
        }
    }

    private void Watch(TextDocument? document)
    {
        if (_watched is not null) _watched.Changed -= OnChanged;
        _watched = document;
        if (document is not null) document.Changed += OnChanged;
        _carry.Clear();
    }

    private void OnChanged(object? sender, DocumentChangeEventArgs e) => _carry.Clear();

    /// <summary>False leaves the lines as they are.</summary>
    public bool Enabled
    {
        get => enabled;
        set
        {
            if (enabled == value) return;
            enabled = value;
            view.Redraw();
        }
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (!enabled) return;
        var document = CurrentContext.Document;
        string? TextOfLine(long index) => index < document.LineCount ? LineText(document, document.GetLineByNumber((int)index + 1)) : null;
        // an empty line has nothing to color, but the style it passes on has to be remembered for the lines after it
        var parsed = _carry.Read(line.LineNumber - 1, LineText(document, line), TextOfLine);
        foreach (var span in parsed.Spans)
        {
            var style = span.Style;
            ChangeLinePart(line.Offset + span.Start, line.Offset + span.Start + span.Length, element =>
            {
                var props = element.TextRunProperties;
                if (Brush(style.Fore) is { } fore) props.SetForegroundBrush(fore);
                if (Brush(style.Back) is { } back) props.SetBackgroundBrush(back);
                if (style.Bold) props.SetTypeface(new Typeface(props.Typeface.FontFamily, props.Typeface.Style, FontWeight.Bold));
                if (style.Underline) props.SetTextDecorations(TextDecorations.Underline);
            });
        }
    }

    private static string LineText(TextDocument document, DocumentLine line) => line.Length == 0 ? "" : document.GetText(line.Offset, Math.Min(line.Length, MaxChars));

    private static IBrush? Brush(AnsiColor color) => color.ToRgb() is { } rgb ? new ImmutableSolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)) : null;
}

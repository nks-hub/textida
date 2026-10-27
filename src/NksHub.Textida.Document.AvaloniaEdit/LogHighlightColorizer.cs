using System.Text.RegularExpressions;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>
/// Gives the text that the highlight expression of a log view matches a background, in every line. Like <see cref="LogLevelColorizer"/> it reads the text of
/// the line when the line is built; only the first <see cref="LogLevelColorizer.MaxClassifiedChars"/> characters of a line are searched.
/// </summary>
public sealed class LogHighlightColorizer : DocumentColorizingTransformer
{
    public const string Kind = "log.highlight";

    private readonly TextView view;
    private Regex? pattern;
    private SyntaxPalette palette = SyntaxPalette.Light;

    public LogHighlightColorizer(TextView view) => this.view = view;

    /// <summary>The expression to mark; null marks nothing.</summary>
    public Regex? Pattern
    {
        get => pattern;
        set
        {
            if (ReferenceEquals(pattern, value)) return;
            pattern = value;
            view.Redraw();
        }
    }

    public SyntaxPalette Palette
    {
        get => palette;
        set
        {
            palette = value;
            if (pattern is not null) view.Redraw();
        }
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (pattern is null || line.Length == 0 || palette.GetBrush(Kind) is not { } brush) return;
        string text = CurrentContext.Document.GetText(line.Offset, Math.Min(line.Length, LogLevelColorizer.MaxClassifiedChars));
        foreach (var span in Spans(pattern, text))
            ChangeLinePart(line.Offset + span.Start, line.Offset + span.Start + span.Length, element => element.TextRunProperties.SetBackgroundBrush(brush));
    }

    /// <summary>The non-empty matches of <paramref name="pattern"/> in the text; a match that runs out of time ends the search of the line.</summary>
    public static List<(int Start, int Length)> Spans(Regex pattern, string text)
    {
        var spans = new List<(int, int)>();
        try
        {
            for (var m = pattern.Match(text); m.Success; m = m.NextMatch())
                if (m.Length > 0) spans.Add((m.Index, m.Length));
        }
        catch (RegexMatchTimeoutException)
        {
        }
        return spans;
    }
}

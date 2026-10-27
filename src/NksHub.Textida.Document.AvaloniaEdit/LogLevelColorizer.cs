using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>Palette kind names of the log levels; the palette gives each its color for the light and the dark theme.</summary>
public static class LogLevelKinds
{
    public static string Of(LogLevel level) => level switch
    {
        LogLevel.Error => "log.error",
        LogLevel.Warning => "log.warning",
        LogLevel.Info => "log.info",
        _ => "log.debug",
    };
}

/// <summary>
/// Colors a whole line by its level while the document is in log mode. The level is read from the text of the line when the line is built, so there is
/// nothing to keep in step with edits. A line longer than <see cref="MaxClassifiedChars"/> is judged by its beginning, where the level of a log line stands.
/// </summary>
public sealed class LogLevelColorizer : DocumentColorizingTransformer
{
    public const int MaxClassifiedChars = 8192;

    private readonly TextView view;
    private LogLevelRules? rules;
    private SyntaxPalette palette = SyntaxPalette.Light;

    public LogLevelColorizer(TextView view) => this.view = view;

    /// <summary>The rules that decide the level of a line; null leaves every line to the other colorizers.</summary>
    public LogLevelRules? Rules
    {
        get => rules;
        set
        {
            if (ReferenceEquals(rules, value)) return;
            rules = value;
            view.Redraw();
        }
    }

    public SyntaxPalette Palette
    {
        get => palette;
        set
        {
            palette = value;
            if (rules is not null) view.Redraw();
        }
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (rules is null || line.Length == 0) return;
        string text = CurrentContext.Document.GetText(line.Offset, Math.Min(line.Length, MaxClassifiedChars));
        if (rules.Classify(text) is not { } level || palette.GetBrush(LogLevelKinds.Of(level)) is not { } brush) return;
        ChangeLinePart(line.Offset, line.EndOffset, element => element.TextRunProperties.SetForegroundBrush(brush));
    }
}

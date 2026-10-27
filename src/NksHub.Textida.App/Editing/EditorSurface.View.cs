using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using System.Collections;
using System.Globalization;
using NksHub.Textida.App.Services;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Editing;

/// <summary>View switches of the editor surface (line numbers, hidden characters, margin, links, wrapping) and the minimap host.</summary>
internal sealed partial class EditorSurface : IMinimapHost
{
    /// <summary>A document with a line longer than this is never wrapped automatically: laying out one giant line as many rows would stall the view.</summary>
    public const int WrapLineLimit = 50_000;

    private LineEndingRenderer? _lineEndings;
    private RightMarginRenderer? _margin;
    private BracketRenderer? _brackets;
    private (ITextSourceVersion? Version, int Caret, string? Grammar) _bracketKey;
    private (int Bracket, int Partner)? _bracketPair;
    private SyntaxProbe? _bracketProbe;
    private TextDocument? _guarded;
    private ITextSourceVersion? _giantVersion;
    private bool _giant;
    private Action? _mapChanged;
    private (long First, long Count) _mapVisible = (0, 1);

    /// <summary>Word wrap is switched on in the settings but not applied, because the document has a line longer than <see cref="WrapLineLimit"/>.</summary>
    public bool WrapSuppressed { get; private set; }

    private TextBlock? _watermark;

    /// <summary>The theme shows the watermark through a binding to <c>Document.Text</c>, which builds the whole text after every edit; the binding goes and the visibility follows the length of the text instead.</summary>
    private void OnAreaTemplateApplied(object? sender, Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        _watermark = e.NameScope.Find<TextBlock>("PART_Watermark");
        if (_watermark is null) return;
        Avalonia.Data.BindingOperations.GetBindingExpressionBase(_watermark, Visual.IsVisibleProperty)?.Dispose();
        UpdateWatermark();
    }

    private void UpdateWatermark()
    {
        if (_watermark is not null) _watermark.IsVisible = Document.TextLength == 0;
    }

    private void InitViewOptions()
    {
        DocumentChanged += (_, _) =>
        {
            GuardWrap();
            ApplyWrap(Settings);
            UpdateWatermark();
        };
        GuardWrap();
        TextArea.TemplateApplied += OnAreaTemplateApplied;
        var view = TextArea.TextView;
        view.VisualLinesChanged += (_, _) => _mapChanged?.Invoke();
        view.ScrollOffsetChanged += (_, _) => _mapChanged?.Invoke();
        TextArea.Caret.PositionChanged += (_, _) => InvalidateBrackets();
        TextArea.SelectionChanged += (_, _) => InvalidateBrackets();
        CaretsChanged += InvalidateBrackets;
    }

    private void ApplyViewOptions(EditorOptions o)
    {
        ShowLineNumbers = o.LineNumbers;
        Options.HighlightCurrentLine = o.CurrentLine;
        Options.EnableHyperlinks = o.Links;
        Options.EnableEmailHyperlinks = false;
        _lineEndings ??= AddRenderer(new LineEndingRenderer(this));
        _margin ??= AddRenderer(new RightMarginRenderer(this));
        _brackets ??= AddRenderer(new BracketRenderer(this));
        _lineEndings.Enabled = o.ShowLineEndings;
        _margin.Column = o.RightMargin;
        TextArea.TextView.InvalidateLayer(KnownLayer.Text);
        TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        ApplyWrap(o);
    }

    /// <summary>Applies the wrap setting again: a wrap held back by a giant line that an edit has removed comes back.</summary>
    public void RefreshWrap() => ApplyWrap(Settings);

    private void ApplyWrap(EditorOptions o)
    {
        WrapSuppressed = o.WordWrap && HasGiantLine();
        WordWrap = o.WordWrap && !WrapSuppressed;
    }

    private T AddRenderer<T>(T renderer) where T : IBackgroundRenderer
    {
        TextArea.TextView.BackgroundRenderers.Add(renderer);
        return renderer;
    }

    /// <summary>Whether a line is longer than <see cref="WrapLineLimit"/>; the scan runs once per version of the text.</summary>
    private bool HasGiantLine()
    {
        var version = Document.Version;
        if (_giantVersion is not null && version.BelongsToSameDocumentAs(_giantVersion) && version.CompareAge(_giantVersion) == 0) return _giant;
        _giantVersion = version;
        _giant = false;
        foreach (var line in Document.Lines)
            if (line.Length > WrapLineLimit)
            {
                _giant = true;
                break;
            }
        return _giant;
    }

    /// <summary>Watches the document the surface shows: an edit that makes a line giant turns wrapping off at once.</summary>
    private void GuardWrap()
    {
        if (ReferenceEquals(_guarded, Document)) return;
        if (_guarded is not null) _guarded.Changed -= OnTextChangedForWrap;
        _guarded = Document;
        if (_guarded is not null) _guarded.Changed += OnTextChangedForWrap;
    }

    private void OnTextChangedForWrap(object? sender, DocumentChangeEventArgs e)
    {
        UpdateWatermark();
        if (!WordWrap) return;
        var line = Document.GetLineByOffset(Math.Min(e.Offset, Document.TextLength));
        for (; line is not null && line.Offset <= e.Offset + e.InsertionLength; line = line.NextLine)
        {
            if (line.Length <= WrapLineLimit) continue;
            WrapSuppressed = true;
            WordWrap = false;
            return;
        }
    }

    // ---- matching brackets ----

    /// <summary>Background of the bracket at the caret and of its partner; set with the theme.</summary>
    internal IBrush? BracketBrush { get; private set; }

    /// <summary>
    /// Offsets of the bracket at the caret (or just before it) and of its partner while the highlight is on, there is one caret and nothing is
    /// selected; null otherwise. Brackets in strings and comments do not count when the language has a grammar. Kept until the text, the caret
    /// or the grammar changes.
    /// </summary>
    internal (int Bracket, int Partner)? MatchingBrackets()
    {
        if (!Settings.MatchBrackets || IsMulti || !TextArea.Selection.IsEmpty || !NextToBracket())
        {
            _bracketKey = default;
            return _bracketPair = null;
        }
        (ITextSourceVersion? Version, int Caret, string? Grammar) key = (Document.Version, CaretOffset, LanguageModes.GrammarId(Doc));
        if (_bracketKey.Version is { } known && key.Version is { } now && now.BelongsToSameDocumentAs(known) && now.CompareAge(known) == 0
            && _bracketKey.Caret == key.Caret && _bracketKey.Grammar == key.Grammar) return _bracketPair;
        _bracketKey = key;
        return _bracketPair = FindBrackets(key.Grammar);
    }

    private (int Bracket, int Partner)? FindBrackets(string? grammar)
    {
        var line = Document.GetLineByOffset(CaretOffset);
        if (line.Length > WrapLineLimit) return null; // shortcut: a giant caret line is not scanned at every caret move; Go to Matching Bracket still works there
        Func<int, IReadOnlyList<Token>>? tokensOf = null;
        if (grammar is not null)
        {
            if (_bracketProbe?.Language != grammar)
            {
                _bracketProbe?.Dispose();
                _bracketProbe = SyntaxProbe.TryCreate(Doc.Doc, grammar);
            }
            if (_bracketProbe is not { } probe) return null; // without tokens a bracket in a string would count
            tokensOf = l => probe.TokensOfLine(l + 1);
        }
        try
        {
            var match = BracketMatcher.Match(new DocumentLines(Document), tokensOf, line.LineNumber - 1, CaretOffset - line.Offset, PluginLimitsPlaceholders.BracketHighlightMaxLines);
            return match is { } m ? (OffsetOf(m.Bracket), OffsetOf(m.Partner)) : null;
        }
        catch (ArgumentOutOfRangeException) { return null; } // the line breaks of the decoded text and of the tokenizer disagree
        int OffsetOf(TextPosition p) => Document.GetLineByNumber(p.Line + 1).Offset + p.Column;
    }

    private bool NextToBracket()
    {
        int caret = CaretOffset;
        return IsBracketAt(caret) || IsBracketAt(caret - 1);
        bool IsBracketAt(int at) => at >= 0 && at < Document.TextLength && Document.GetCharAt(at) is '(' or ')' or '[' or ']' or '{' or '}';
    }

    /// <summary>The caret or the selection changed: the background is drawn again when a pair is shown or the caret may stand at a bracket.</summary>
    private void InvalidateBrackets()
    {
        if (_bracketPair is not null || (Settings.MatchBrackets && NextToBracket())) TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void ReleaseBrackets()
    {
        _bracketProbe?.Dispose();
        _bracketProbe = null;
        _bracketKey = default;
        _bracketPair = null;
    }

    /// <summary>The lines of a text document as strings, each built when it is read.</summary>
    private sealed class DocumentLines(TextDocument document) : IReadOnlyList<string>
    {
        public int Count => document.LineCount;

        public string this[int index]
        {
            get
            {
                var line = document.GetLineByNumber(index + 1);
                return document.GetText(line.Offset, line.Length);
            }
        }

        public IEnumerator<string> GetEnumerator()
        {
            for (int i = 0; i < Count; i++) yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // ---- minimap ----

    event Action? IMinimapHost.MapChanged
    {
        add => _mapChanged += value;
        remove => _mapChanged -= value;
    }

    long IMinimapHost.LineCount => Document.LineCount;

    string? IMinimapHost.LineText(long line)
    {
        if (line < 0 || line >= Document.LineCount) return null;
        var textLine = Document.GetLineByNumber((int)line + 1);
        var text = Document.GetText(textLine.Offset, Math.Min(textLine.Length, Minimap.MaxChars));
        return SecretsHidden ? SecretValues.Mask(text, Secrets(), textLine.Offset) : text;
    }

    (long First, long Count) IMinimapHost.Visible
    {
        get
        {
            var view = TextArea.TextView;
            if (view.VisualLinesValid && view.VisualLines is { Count: > 0 } lines)
            {
                long first = lines[0].FirstDocumentLine.LineNumber - 1;
                _mapVisible = (first, lines[^1].LastDocumentLine.LineNumber - 1 - first + 1);
            }
            return _mapVisible;
        }
    }

    void IMinimapHost.ScrollToLine(long line)
    {
        var view = TextArea.TextView;
        var scrollable = (Avalonia.Controls.Primitives.ILogicalScrollable)view;
        scrollable.Offset = new Vector(scrollable.Offset.X, view.GetVisualTopByDocumentLine((int)Math.Clamp(line + 1, 1, Document.LineCount)));
    }
}

/// <summary>Draws CR, LF or CRLF after the last character of each line; the document is not touched.</summary>
internal sealed class LineEndingRenderer(TextEditor owner) : IBackgroundRenderer
{
    public bool Enabled { get; set; }

    public KnownLayer Layer => KnownLayer.Text;

    /// <summary>The mark drawn for a line break: CRLF, CR or LF; null for anything else.</summary>
    public static string? Label(string lineBreak) => lineBreak switch { "\r\n" => "CRLF", "\r" => "CR", "\n" => "LF", _ => null };

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!Enabled || !textView.VisualLinesValid || textView.Document is not { } document) return;
        var color = owner.Foreground is ISolidColorBrush brush ? brush.Color : Colors.Gray;
        var dim = new ImmutableSolidColorBrush(color, 0.4);
        var typeface = new Typeface(owner.FontFamily);
        double size = Math.Max(6, owner.FontSize * 0.7);
        foreach (var line in textView.VisualLines)
        {
            var last = line.LastDocumentLine;
            if (last.DelimiterLength == 0) continue;
            if (Label(document.GetText(last.EndOffset, last.DelimiterLength)) is not { } name) continue;
            var textLine = line.TextLines[^1];
            double x = line.GetTextLineVisualXPosition(textLine, line.VisualLength) - textView.ScrollOffset.X;
            double y = line.GetTextLineVisualYPosition(textLine, VisualYPosition.TextTop) - textView.ScrollOffset.Y;
            var text = new FormattedText(name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, dim);
            drawingContext.DrawText(text, new Point(x + textView.WideSpaceWidth * 0.5, y + Math.Max(0, (textLine.Height - text.Height) / 2)));
        }
    }
}

/// <summary>Colors the bracket at the caret and its partner (<see cref="EditorSurface.MatchingBrackets"/>).</summary>
internal sealed class BracketRenderer(EditorSurface owner) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        long trace = NksHub.Textida.Core.TypingTrace.Begin();
        try
        {
            if (owner.BracketBrush is not { } brush || !textView.VisualLinesValid || owner.MatchingBrackets() is not { } pair) return;
            var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true, CornerRadius = 2 };
            builder.AddSegment(textView, new TextSegment { StartOffset = pair.Bracket, Length = 1 });
            builder.AddSegment(textView, new TextSegment { StartOffset = pair.Partner, Length = 1 });
            if (builder.CreateGeometry() is { } geometry) drawingContext.DrawGeometry(brush, null, geometry);
        }
        finally { NksHub.Textida.Core.TypingTrace.End("editor.drawBrackets", trace); }
    }
}

/// <summary>Draws a thin vertical line after a given number of columns; the column is counted in the width of a space.</summary>
internal sealed class RightMarginRenderer(TextEditor owner) : IBackgroundRenderer
{
    public int Column { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (Column <= 0) return;
        double x = Math.Round(textView.WideSpaceWidth * Column - textView.ScrollOffset.X) + 0.5;
        if (x < 0 || x > textView.Bounds.Width) return;
        var color = owner.Foreground is ISolidColorBrush brush ? brush.Color : Colors.Gray;
        drawingContext.DrawLine(new Pen(new ImmutableSolidColorBrush(color, 0.2), 1), new Point(x, 0), new Point(x, textView.Bounds.Height));
    }
}

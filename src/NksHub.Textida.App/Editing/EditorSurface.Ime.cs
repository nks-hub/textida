using Avalonia;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The composition of an input method drawn in the line, underlined at every caret. It is not part of the document: the committed text arrives as
/// typed text and goes to every caret as one undo step (<see cref="TypeAtCarets"/>). AvaloniaEdit reports no preedit, and Avalonia.Win32 then
/// shows the composition nowhere, so the client of the text area is wrapped to take it. The drawn composition is dropped when the document changes,
/// the caret moves or the editor loses focus; the input method keeps its own state and draws again on its next update.
/// </summary>
internal sealed partial class EditorSurface
{
    private string _preedit = "";
    private int[] _preeditAt = [];
    private int _preeditCaret = -1;
    private ITextSourceVersion? _preeditVersion;
    private PreeditClient? _imeClient;

    /// <summary>The text an input method is composing, empty when there is none.</summary>
    internal string Composition => _preedit;

    /// <summary>True while an input method composes in this editor; its plain keys (Esc, Enter, arrows) are not commands then.</summary>
    public bool IsComposing => _preedit.Length > 0;

    private void InitIme()
    {
        TextArea.TextView.ElementGenerators.Add(new PreeditGenerator(this));
        TextArea.AddHandler(TextInputMethodClientRequestedEvent, OnClientRequested, RoutingStrategies.Bubble, handledEventsToo: true);
        TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (IsComposing && CaretOffset != _preeditCaret) EndComposition();
        };
        TextArea.LostFocus += (_, _) => EndComposition();
    }

    // the class handler of the text area has put its own client in (none when read-only)
    private void OnClientRequested(object? sender, TextInputMethodClientRequestedEventArgs e)
    {
        if (e.Client is null or PreeditClient) return;
        if (_imeClient?.Inner != e.Client) _imeClient = new PreeditClient(e.Client, this);
        e.Client = _imeClient;
    }

    private void SetComposition(string text)
    {
        if (text == _preedit) return;
        if (text.Length > 0)
        {
            _preeditAt = [.. GetCarets().Items.Select(s => s.Caret)]; // in document order, as the carets are
            _preeditCaret = CaretOffset;
            _preeditVersion = Document.Version;
        }
        _preedit = text;
        TextArea.TextView.Redraw();
        _imeClient?.CompositionMoved();
    }

    private void EndComposition()
    {
        if (IsComposing) SetComposition("");
    }

    /// <summary>The caret rectangle after the composition at the primary caret, where the candidate window of the input method opens.</summary>
    private Rect? CompositionRect()
    {
        if (!IsComposing || Document.Version != _preeditVersion) return null;
        var view = TextArea.TextView;
        var line = view.GetOrConstructVisualLine(Document.GetLineByOffset(_preeditCaret));
        int relative = _preeditCaret - line.FirstDocumentLine.Offset;
        if (line.Elements.OfType<PreeditElement>().FirstOrDefault(p => p.RelativeTextOffset == relative) is not { } element) return null;
        if (view.TranslatePoint(default, TextArea) is not { } origin) return null;
        int column = element.VisualColumn + element.VisualLength;
        var textLine = line.GetTextLine(column);
        double top = line.GetTextLineVisualYPosition(textLine, VisualYPosition.LineTop);
        double bottom = line.GetTextLineVisualYPosition(textLine, VisualYPosition.LineBottom);
        return new Rect(origin.X + line.GetTextLineVisualXPosition(textLine, column) - view.ScrollOffset.X, origin.Y + top - view.ScrollOffset.Y, 0.5, bottom - top);
    }

    /// <summary>The composition shown at one caret: no character of the document, underlined like the system draws it.</summary>
    internal sealed class PreeditElement(string text) : FormattedTextElement(text, 0)
    {
        public override TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        {
            TextRunProperties.SetTextDecorations(TextDecorations.Underline);
            return base.CreateTextRun(startVisualColumn, context);
        }
    }

    private sealed class PreeditGenerator(EditorSurface owner) : VisualLineElementGenerator
    {
        private int[] _at = [];

        public override void StartGeneration(ITextRunConstructionContext context)
        {
            base.StartGeneration(context);
            // a line built for a newer text than the composition was placed in shows none until the composition is placed again
            _at = owner.IsComposing && context.Document.Version == owner._preeditVersion ? owner._preeditAt : [];
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            int i = Array.BinarySearch(_at, startOffset);
            if (i < 0) i = ~i;
            return i < _at.Length ? _at[i] : -1;
        }

        public override VisualLineElement ConstructElement(int offset) => new PreeditElement(owner._preedit);
    }

    /// <summary>The client of the text area with preedit switched on; everything else is the text area's own.</summary>
    private sealed class PreeditClient : TextInputMethodClient
    {
        private readonly EditorSurface _owner;

        public PreeditClient(TextInputMethodClient inner, EditorSurface owner)
        {
            Inner = inner;
            _owner = owner;
            inner.CursorRectangleChanged += (_, _) => RaiseCursorRectangleChanged();
            inner.SurroundingTextChanged += (_, _) => RaiseSurroundingTextChanged();
            inner.SelectionChanged += (_, _) => RaiseSelectionChanged();
            inner.TextViewVisualChanged += (_, _) => RaiseTextViewVisualChanged();
        }

        public TextInputMethodClient Inner { get; }
        public override Visual TextViewVisual => Inner.TextViewVisual;
        public override bool SupportsPreedit => true;
        public override bool SupportsSurroundingText => Inner.SupportsSurroundingText;
        public override string SurroundingText => Inner.SurroundingText;
        public override Rect CursorRectangle => _owner.CompositionRect() ?? Inner.CursorRectangle;
        public override TextSelection Selection { get => Inner.Selection; set => Inner.Selection = value; }
        public override void SetPreeditText(string? preeditText) => _owner.SetComposition(preeditText ?? "");
        public void CompositionMoved() => RaiseCursorRectangleChanged();
    }
}

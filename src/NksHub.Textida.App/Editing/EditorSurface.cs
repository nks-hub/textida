using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// Plug-in point of the accessibility library (P1.8.1): set <see cref="Provider"/> at startup and every editor surface
/// asks it for its automation peer. Without a provider the control keeps the peer AvaloniaEdit gives it.
/// </summary>
public interface IEditorAutomationProvider
{
    /// <summary>Peer for an editor surface showing <paramref name="document"/>; null falls back to the default peer.</summary>
    AutomationPeer? CreatePeer(Control editor, Document document);
}

public static class EditorAutomationHook
{
    public static IEditorAutomationProvider? Provider { get; set; }
}

/// <summary>The text surface of a document that lives in the AvaloniaEdit backend: line numbers, current line, block selection and typing assists.</summary>
internal sealed partial class EditorSurface : TextEditor
{
    private TextViewPosition _blockAnchor;
    private TextViewPosition _blockEnd;
    private readonly SyntaxColorizer _colorizer;
    private bool _syntaxViewQueued;

    public EditorSurface(OpenDocument doc, EditorOptions options) : base(new EditorTextArea())
    {
        Doc = doc;
        Document = doc.TextBackend!.TextDocument;
        ShowLineNumbers = true;
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        Options.HighlightCurrentLine = true;
        Options.EnableRectangularSelection = true;
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.AllowScrollBelowDocument = false;
        Options.CutCopyWholeLine = false;
        FontWeight = FontWeight.Normal;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        foreach (var margin in TextArea.LeftMargins.OfType<LineNumberMargin>()) margin.Margin = new Thickness(10, 0, 14, 0);
        Settings = options;
        ApplyOptions(options);
        _colorizer = new SyntaxColorizer(TextArea.TextView);
        _colorizer.BindDocument(Document);
        TextArea.TextView.LineTransformers.Add(_colorizer);
        Syntax = new SyntaxFeeder(doc, ApplyTokens, _colorizer.ClearTokens, LineText, () => Document.LineCount);
        TextArea.TextView.VisualLinesChanged += OnVisualLinesChanged;
        TextArea.TextView.ScrollOffsetChanged += (_, _) => QueueSyntaxView();
        TextArea.TextEntering += OnTextEntering;
        AddHandler(KeyDownEvent, OnKeyDownTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        InitCarets();
    }

    protected override Type StyleKeyOverride => typeof(TextEditor);

    public OpenDocument Doc { get; }
    /// <summary>Keeps the syntax colors of this view current.</summary>
    public SyntaxFeeder Syntax { get; }
    public EditorOptions Settings { get; private set; }
    public bool BlockMode { get; private set; }
    public bool IsBlockSelection => TextArea.Selection is RectangleSelection;

    /// <summary>Creates the invisible native control that publishes this editor to UI Automation; the caller puts it into the visual tree next to the editor.</summary>
    public EditorAutomationHost CreateAutomationHost() => ((EditorTextArea)TextArea).Automation = new EditorAutomationHost(this);

    protected override AutomationPeer OnCreateAutomationPeer() =>
        EditorAutomationHook.Provider?.CreatePeer(this, Doc.Doc) ?? base.OnCreateAutomationPeer();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ApplyTheme();
    }

    public void BindDocument()
    {
        TextArea.ClearSelection();
        Document = Doc.TextBackend!.TextDocument;
        _colorizer.BindDocument(Document);
        CaretOffset = 0;
        RebindCarets();
    }

    private string? LineText(long line)
    {
        if (line >= Document.LineCount) return null;
        var textLine = Document.GetLineByNumber((int)line + 1);
        return Document.GetText(textLine.Offset, textLine.Length);
    }

    private void ApplyTokens(SyntaxViewport viewport)
    {
        if (viewport.Revision != Doc.Doc.Revision) return;
        try { _colorizer.SetTokens(Document.Version, viewport.Lines); }
        catch (ArgumentOutOfRangeException) { _colorizer.ClearTokens(); } // the line breaks of the decoded text and of the tokenizer disagree
    }

    /// <summary>Tells the feeder which lines and which UTF-16 columns of them are on screen, read from the layout of the visible lines.</summary>
    private void OnVisualLinesChanged(object? sender, EventArgs e)
    {
        var view = TextArea.TextView;
        var lines = view.VisualLines;
        if (!view.VisualLinesValid || lines.Count == 0) return;
        int first = int.MaxValue, last = 0;
        int step = Math.Max(1, lines.Count / 8);
        for (int i = 0; i < lines.Count; i += step)
        {
            var line = lines[i];
            if (WordWrap && lines.Count > 1)
            {
                first = 0;
                last = Math.Max(last, line.LastDocumentLine.Length);
                continue;
            }
            double y = line.VisualTop + 1; // GetPosition takes document coordinates: the scroll offset is part of them
            int left = ColumnAt(view, view.ScrollOffset.X, y, 0);
            if (view.ScrollOffset.X > 0 && left >= line.LastDocumentLine.Length) continue; // scrolled past the end of this line: nothing of it is visible
            first = Math.Min(first, left);
            last = Math.Max(last, ColumnAt(view, view.ScrollOffset.X + view.Bounds.Width, y, line.LastDocumentLine.Length) + 1);
        }
        if (first == int.MaxValue) first = last = 0;
        Syntax.SetVisible(lines[0].FirstDocumentLine.LineNumber - 1, lines[^1].LastDocumentLine.LineNumber - 1, first, Math.Max(first + 1, last));
    }

    /// <summary>A scroll invalidates the visual lines; the columns are read once they are laid out again.</summary>
    private void QueueSyntaxView()
    {
        if (_syntaxViewQueued) return;
        _syntaxViewQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _syntaxViewQueued = false;
            TextArea.TextView.EnsureVisualLines();
            OnVisualLinesChanged(null, EventArgs.Empty);
        }, DispatcherPriority.Render);
    }

    private static int ColumnAt(TextView view, double x, double y, int fallback) =>
        view.GetPosition(new Point(x, y)) is { } position ? position.Column - 1 : fallback;

    // ---- look ----

    public void ApplyOptions(EditorOptions o)
    {
        Settings = o;
        FontFamily = new FontFamily(o.FontFamily);
        FontSize = o.FontSize;
        WordWrap = o.WordWrap;
        Options.IndentationSize = o.TabSize;
        Options.ConvertTabsToSpaces = o.InsertSpaces;
        Options.ShowSpaces = o.ShowWhitespace;
        Options.ShowTabs = o.ShowWhitespace;
        Options.EnableTextDragDrop = true;
        Options.AcceptsTab = false;
    }

    private void ApplyTheme()
    {
        IBrush? Find(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) ? value as IBrush : null;
        Background = Find("BgEditor");
        Foreground = Find("EditorFg") ?? Find("Fg");
        LineNumbersForeground = Find("LineNumbers");
        TextArea.TextView.CurrentLineBackground = Find("CurrentLine");
        TextArea.TextView.CurrentLineBorder = new Pen(null, 0);
        TextArea.SelectionBrush = Find("SelectionBg");
        TextArea.SelectionForeground = null;
        TextArea.SelectionBorder = null;
        _caretBrush = Find("Fg");
        UpdateNativeCaret();
        _colorizer.Palette = ActualThemeVariant == ThemeVariant.Dark ? SyntaxPalette.Dark : SyntaxPalette.Light;
    }

    // ---- selections ----

    /// <summary>The selections as a list: all carets when there are several, one for a normal selection, one per row for a block selection.</summary>
    public IReadOnlyList<Sel> GetSelections()
    {
        if (IsMulti) return GetCarets().Items;
        var selection = TextArea.Selection;
        if (selection is RectangleSelection)
        {
            var rows = selection.Segments.Select(s => new Sel(s.StartOffset, s.EndOffset)).ToList();
            if (rows.Count > 0) return rows;
        }
        int caret = CaretOffset;
        if (selection.IsEmpty) return [Sel.At(caret)];
        var seg = selection.SurroundingSegment;
        return [new Sel(caret == seg.EndOffset ? seg.Offset : seg.EndOffset, caret)];
    }

    public bool HasSelection => !TextArea.Selection.IsEmpty || (IsMulti && OtherSels().Any(s => !s.IsEmpty));

    /// <summary>Puts the selections in place. Several selections become several carets (the last one primary) unless a block selection is being edited.</summary>
    public void SetSelections(IReadOnlyList<Sel> sels)
    {
        if (sels.Count == 0) return;
        if (sels.Count > 1 && !BlockMode && !IsBlockSelection)
        {
            SetCarets(Carets.Create(sels, sels.Count - 1));
            return;
        }
        DropOthers();
        var first = sels[0];
        var last = sels[^1];
        using (TextArea.AllowCaretOutsideSelection())
        {
            if (sels.Count == 1 && first.IsEmpty)
            {
                TextArea.ClearSelection();
                CaretOffset = first.Caret;
            }
            else
            {
                int start = Math.Min(first.Start, last.Start), end = Math.Max(first.End, last.End);
                TextArea.Selection = Selection.Create(TextArea, start, end);
                CaretOffset = sels.Count == 1 ? first.Caret : last.End;
            }
        }
        TextArea.Caret.BringCaretToView();
    }

    public (int Line, int Column) CaretLineColumn() => (TextArea.Caret.Line, TextArea.Caret.Column);

    public void GoToLine(int line)
    {
        var d = Document;
        int clamped = Math.Clamp(line, 1, d.LineCount);
        TextArea.ClearSelection();
        CaretOffset = d.GetLineByNumber(clamped).Offset;
        TextArea.Caret.BringCaretToView();
        ScrollToLine(clamped);
    }

    // ---- block selection (F8) ----

    public void ToggleBlockMode()
    {
        DropOthers();
        if (BlockMode) { BlockMode = false; Options.EnableVirtualSpace = false; TextArea.ClearSelection(); return; }
        BlockMode = true;
        Options.EnableVirtualSpace = true;
        _blockAnchor = _blockEnd = TextArea.Caret.Position;
        TextArea.Selection = new RectangleSelection(TextArea, _blockAnchor, _blockEnd);
    }

    /// <summary>Leaves block mode after the selection was used (paste).</summary>
    public void ToggleBlockModeOff()
    {
        if (BlockMode) ToggleBlockMode();
    }

    private void ExtendBlock(Key key)
    {
        var end = _blockEnd;
        int line = end.Line, visual = end.VisualColumn < 0 ? end.Column - 1 : end.VisualColumn;
        switch (key)
        {
            case Key.Left: visual = Math.Max(0, visual - 1); break;
            case Key.Right: visual++; break;
            case Key.Up: line = Math.Max(1, line - 1); break;
            case Key.Down: line = Math.Min(Document.LineCount, line + 1); break;
            case Key.Home: visual = 0; break;
        }
        var l = Document.GetLineByNumber(line);
        int column = Math.Min(visual, l.Length) + 1;
        _blockEnd = new TextViewPosition(line, column, visual);
        TextArea.Selection = new RectangleSelection(TextArea, _blockAnchor, _blockEnd);
        using (TextArea.AllowCaretOutsideSelection()) TextArea.Caret.Position = _blockEnd;
    }

    // ---- typing assists ----

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (HandleCaretKey(e)) return;
        if (BlockMode && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home)
        {
            ExtendBlock(e.Key);
            e.Handled = true;
            return;
        }
        if (BlockMode && e.Key == Key.Escape)
        {
            ToggleBlockMode();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None && Settings.AutoClosePairs && !Settings.PspadPairs && !IsReadOnly && !IsBlockSelection
            && PairedCharacters.TryBackspace(Document, GetSelections()[0], out var result))
        {
            SetSelections([result]);
            e.Handled = true;
        }
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (e.Text is { Length: > 0 } entered && TypeAtCarets(entered))
        {
            e.Handled = true;
            return;
        }
        if (IsBlockSelection && !IsReadOnly && e.Text is { Length: > 0 } typed && !typed.Contains('\n') && !typed.Contains('\r'))
        {
            // one keystroke edits every row; one undo step for all of them
            using (Doc.Doc.BeginUndoGroup()) TextArea.Selection.ReplaceSelectionWithText(typed);
            e.Handled = true;
            return;
        }
    }
}

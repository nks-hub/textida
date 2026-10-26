using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;

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
internal sealed class EditorSurface : TextEditor
{
    private TextViewPosition _blockAnchor;
    private TextViewPosition _blockEnd;

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
        TextArea.TextEntering += OnTextEntering;
        AddHandler(KeyDownEvent, OnKeyDownTunnel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
    }

    protected override Type StyleKeyOverride => typeof(TextEditor);

    public OpenDocument Doc { get; }
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
        CaretOffset = 0;
    }

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
        IsReadOnly = Doc.ReadOnly;
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
        TextArea.CaretBrush = Find("Fg");
    }

    // ---- selections ----

    /// <summary>The selections as a list: one for a normal selection, one per row for a block selection.</summary>
    public IReadOnlyList<Sel> GetSelections()
    {
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

    public bool HasSelection => !TextArea.Selection.IsEmpty;

    public void SetSelections(IReadOnlyList<Sel> sels)
    {
        if (sels.Count == 0) return;
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
        if (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None && Settings.AutoClosePairs && !IsReadOnly && !IsBlockSelection
            && PairedCharacters.TryBackspace(Document, GetSelections()[0], out var result))
        {
            SetSelections([result]);
            e.Handled = true;
        }
    }

    private void OnTextEntering(object? sender, TextInputEventArgs e)
    {
        if (IsBlockSelection && !IsReadOnly && e.Text is { Length: > 0 } typed && !typed.Contains('\n') && !typed.Contains('\r'))
        {
            // one keystroke edits every row; one undo step for all of them
            using (Doc.Doc.BeginUndoGroup()) TextArea.Selection.ReplaceSelectionWithText(typed);
            e.Handled = true;
            return;
        }
        if (!Settings.AutoClosePairs || IsReadOnly || IsBlockSelection || e.Text is not { Length: 1 } text) return;
        if (PairedCharacters.TryType(Document, GetSelections()[0], text[0], out var result))
        {
            SetSelections([result]);
            e.Handled = true;
        }
    }
}

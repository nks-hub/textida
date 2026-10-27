using Avalonia.Controls;
using NksHub.Textida.App.Editing;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// A tab: one view of an <see cref="OpenDocument"/> in a <see cref="TabGroup"/>. Two tabs of one document are the two views of a split;
/// they show the same revision and share the undo history because they share the document.
/// </summary>
internal sealed class DocTab
{
    private readonly ContentControl _barSlot = new() { [DockPanel.DockProperty] = Dock.Top };

    public DocTab(OpenDocument doc, EditorOptions options, Control? custom = null)
    {
        Doc = doc;
        Custom = custom;
        if (custom is null && doc.IsHex) Hex = new HexView(doc, options);
        else if (custom is null && doc.IsLarge) Paged = new PagedTextView(doc, options);
        else if (custom is null) Surface = new EditorSurface(doc, options);
        Root = new DockPanel { Children = { _barSlot } };
        if ((Surface is not null || Paged is not null) && (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()))
        {
            _automation = Surface?.CreateAutomationHost() ?? Paged!.CreateAutomationHost();
            _automation[DockPanel.DockProperty] = Dock.Top;
            Root.Children.Add(_automation);
        }
        if (custom is null && ((IMinimapHost?)Surface ?? Paged) is { } host)
        {
            Minimap = new Minimap(doc, host) { IsVisible = options.Minimap, [DockPanel.DockProperty] = Dock.Right };
            Root.Children.Add(Minimap);
        }
        Root.Children.Add(View);
        doc.Views.Add(this);
    }

    /// <summary>The reduced picture of the text at the right edge; null for a tab without a text surface.</summary>
    public Minimap? Minimap { get; }

    private readonly EditorAutomationHost? _automation;

    /// <summary>Releases what the view holds outside the visual tree.</summary>
    public void Close()
    {
        Minimap?.Dispose();
        Syntax?.Dispose();
        _automation?.Dispose();
        Surface?.Detach();
    }

    /// <summary>The syntax colors of the view (null for tabs that show no document text); <see cref="SyntaxFeeder.Pending"/> tells that they are not complete yet.</summary>
    public SyntaxFeeder? Syntax => Surface?.Syntax ?? Paged?.Syntax;

    public OpenDocument Doc { get; }
    public EditorSurface? Surface { get; }
    public PagedTextView? Paged { get; }
    /// <summary>The hex view; null for a text or paged view.</summary>
    public HexView? Hex { get; }
    /// <summary>The AvaloniaEdit surface; null for a paged (large) view.</summary>
    public EditorSurface? Editor => Surface;
    public Control View => (Control?)Surface ?? (Control?)Paged ?? (Control?)Hex ?? Custom!;
    /// <summary>The content of a tab that is not a text view (the search results); null for a document tab.</summary>
    public Control? Custom { get; }
    public DockPanel Root { get; }
    public TabGroup? Group { get; set; }

    /// <summary>Header state last drawn, so a keystroke does not rebuild the strip.</summary>
    public string HeaderState { get; set; } = "";

    public string? Path => Doc.Path;
    public string Title => Doc.Title;
    public TaskCompletionSource Closed => Doc.Closed;

    public void ShowBar(Control? bar) => _barSlot.Content = bar;
    public Control? Bar => _barSlot.Content as Control;

    /// <summary>1-based line and column; for a paged view whose line index is not complete yet the first line on screen.</summary>
    public (long Line, long Column) CaretPosition()
    {
        if (Surface is { } s) return s.CaretLineColumn();
        if (Hex is not null) return (1, 1);
        return Paged is { } p ? (p.CaretLineColumn() is { } c ? (c.Line + 1, c.Column + 1) : (p.TopLine + 1, 1)) : (1, 1);
    }

    /// <summary>False when a paged view cannot find the line yet because its index does not reach it.</summary>
    public bool GoToLine(int line)
    {
        if (Surface is { } s) s.GoToLine(line);
        else if (Paged is { } p) return p.GoToLine(line);
        return true;
    }
}

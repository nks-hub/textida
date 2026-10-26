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

    public DocTab(OpenDocument doc, EditorOptions options)
    {
        Doc = doc;
        if (doc.IsLarge) Paged = new PagedTextView(doc, options);
        else Surface = new EditorSurface(doc, options);
        Root = new DockPanel { Children = { _barSlot, View } };
        doc.Views.Add(this);
    }

    public OpenDocument Doc { get; }
    public EditorSurface? Surface { get; }
    public PagedTextView? Paged { get; }
    /// <summary>The text surface; null for a paged (large, read-only) view.</summary>
    public EditorSurface? Editor => Surface;
    public Control View => (Control?)Surface ?? Paged!;
    public DockPanel Root { get; }
    public TabGroup? Group { get; set; }

    /// <summary>Header state last drawn, so a keystroke does not rebuild the strip.</summary>
    public string HeaderState { get; set; } = "";

    public string? Path => Doc.Path;
    public string Title => Doc.Title;
    public TaskCompletionSource Closed => Doc.Closed;

    public void ShowBar(Control? bar) => _barSlot.Content = bar;
    public Control? Bar => _barSlot.Content as Control;

    /// <summary>1-based line and column; for a paged view the first line on screen.</summary>
    public (long Line, long Column) CaretPosition()
    {
        if (Surface is { } s) return s.CaretLineColumn();
        return (Paged!.TopLine + 1, 1);
    }

    public void GoToLine(int line)
    {
        if (Surface is { } s) s.GoToLine(line);
        else Paged!.GoToLine(line);
    }
}

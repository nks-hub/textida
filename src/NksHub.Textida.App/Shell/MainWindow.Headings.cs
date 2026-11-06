using Avalonia.Controls;
using Avalonia.Threading;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The headings panel (P9.3.4): a tool window with the tree of the headings of the Markdown file in the active tab. It stays beside the document in reading, editing and source, follows the caret or
/// the scrolled page, and a heading clicked in it is shown at the top of the editor or the page. It docks and hides like the other tool windows (<c>MainWindow.PanelLayout.cs</c>).
/// </summary>
internal sealed partial class MainWindow
{
    private readonly ToolWindowPanel _headingsTool = new();
    private HeadingsView? _headingsView;

    internal ToolWindowPanel HeadingsTool => _headingsTool;

    internal HeadingsView HeadingsPanelView => _headingsView ??= CreateHeadingsView();

    private HeadingsView CreateHeadingsView()
    {
        var view = new HeadingsView(HeadingSourceOfActive);
        _headingsTool.View = view;
        Help.HelpContext.Set(_headingsTool, "view.markdownModes");
        // the page of a reading or editing tab scrolls without moving a caret: the current heading follows it
        AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) => { if (e.Source is MarkdownPreview) SyncHeadings(); }, Avalonia.Interactivity.RoutingStrategies.Bubble);
        return view;
    }

    /// <summary>The Markdown document of the active tab as the headings panel reads it; null for any other tab.</summary>
    private HeadingSource? HeadingSourceOfActive()
    {
        if (Active is not { } tab || tab.Doc.Language.Id != "markdown" || tab.Doc.IsLarge || tab.Doc.IsResults) return null;
        var od = tab.Doc;
        return new HeadingSource(od, od.Doc.Revision,
            () => tab.Surface?.Document.Text ?? od.TextBackend?.TextDocument.Text ?? "",
            () => tab.Custom is MarkdownPreview page ? page.TopLine : tab.Surface is { } surface ? surface.Document.GetLineByOffset(surface.CaretOffset).LineNumber - 1 : 0,
            line => JumpToHeading(tab, line));
    }

    /// <summary>Puts a heading at the top of the editor (with the caret on it) or of the page; the editing page beside the editor follows the editor.</summary>
    private void JumpToHeading(DocTab tab, int line)
    {
        if (tab.Custom is MarkdownPreview page) page.ScrollToLine(line);
        else if (tab.Surface is { } surface)
        {
            surface.GoToLine(line + 1);
            Dispatcher.UIThread.Post(() => MarkdownPreview.ScrollEditorTo(surface, line), DispatcherPriority.Background);
        }
        FocusView(tab);
    }

    /// <summary>Brings the open headings panel up to the active document; called with the status, which follows the caret, the text and the active tab.</summary>
    private void SyncHeadings()
    {
        if (_headingsView is { } view && IsOpen(SidePanel.Headings)) view.Sync();
    }

    /// <summary>"Show Markdown Outline": shows the headings panel, or puts the keyboard in it, or hides it when the keyboard is there.</summary>
    internal CommandOutcome ShowHeadingsPanel()
    {
        if (Active?.Doc.Language.Id != "markdown")
        {
            SetStatusMessage(Loc.Get(K.PreviewOutlineUnsupported));
            return CommandOutcome.Handled;
        }
        ToggleOrFocusPanel(SidePanel.Headings);
        return CommandOutcome.Handled;
    }
}

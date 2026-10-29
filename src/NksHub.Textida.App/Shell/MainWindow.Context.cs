using NksHub.Textida.App.Editing;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// What the menus ask about the active tab beyond the focus (the context keys of the kind of file, the language group, the log, the repository, the server, the rendered page) and the rebuilding of the
/// menu when one of them changes. A key that decides whether an item is <em>shown</em> only changes with the tab, the mode or the language, so the menu is built again then; a key that only greys an item is asked as the menu opens.
/// </summary>
internal sealed partial class MainWindow
{
    private string _menuSignature = "";
    private string? _menuPathSeen;

    /// <summary>The tab shows its document as text in an editor: not as hex, a picture, a rendered page, a table or a comparison.</summary>
    internal static bool TextViewShown(DocTab? tab) => tab is { Custom: null, Hex: null, TableShown: false } && (tab.Surface is not null || tab.Paged is not null);

    /// <summary>The document of the tab is shown with a rendered Markdown page: reading, editing with the page beside it, or the live preview under the editor.</summary>
    internal bool MarkdownPreviewShown(DocTab? tab) => tab is not null && (tab.Doc.Mode is ViewMode.Read or ViewMode.Edit || ReferenceEquals(_liveTab, tab));

    /// <summary>The values of the keys that decide which items the menu shows, as one text; two equal texts mean the menu built for the first is right for the second.</summary>
    internal string MenuSignature()
    {
        var context = new ShellContext(this, forMenu: true);
        return string.Join('|', ContextKeys.MenuKeys.Select(k => context.Get(k)));
    }

    /// <summary>Builds the menu again when the active tab changed in a way that shows other items (another mode, another language, a new path). Cheap when nothing changed.</summary>
    internal void RefreshMenuIfContextChanged()
    {
        _menuPathSeen = Active?.Doc.Path;
        if (MenuSignature() != _menuSignature) _menuModel = BuildMenuModel();
    }

    /// <summary>The path of the active document changed (saved under another name): the kind of file, the manifest and the repository may be other ones.</summary>
    private void CheckMenuPath(OpenDocument od)
    {
        if (!string.Equals(od.Path, _menuPathSeen, StringComparison.Ordinal) && Active?.Doc == od) RefreshMenuIfContextChanged();
    }
}

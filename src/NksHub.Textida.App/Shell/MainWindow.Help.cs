using NksHub.Textida.App.Help;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

internal sealed partial class MainWindow
{
    /// <summary>
    /// The help topic for F1: the command selected in the palette, else the part of the window that has the keyboard (find bar, a panel or view that names its topic with
    /// <see cref="HelpContext.Set"/>, file list, document list, search results), else the start page.
    /// </summary>
    internal string HelpContextTopic()
    {
        if (IsPaletteOpen)
        {
            int i = _paletteList.SelectedIndex;
            if (_paletteMode == PaletteMode.Commands && _paletteItems.Count == _quickRows.Count && i >= 0 && i < _paletteItems.Count
                && HelpCatalog.ForCommand(Loc.Language, _paletteItems[i].CommandId) is { } command)
                return command.Id;
            return _paletteMode switch { PaletteMode.Files => "search.gotoAnything", PaletteMode.Edits => "search.jump", _ => "view.palette" };
        }
        if (_find.IsVisible && _find.IsKeyboardFocusWithin) return _find.FilesMode ? "search.inFiles" : _find.ReplaceShown ? "search.replace" : "search.find";
        if (HelpContext.Focused(this) is { } named) return named;
        if (FilePanelOpen && _hosts[_layout.DockOf(SidePanel.Files)].IsKeyboardFocusWithin) return "view.files";
        if (DocumentsPanelOpen && _hosts[_layout.DockOf(SidePanel.Documents)].IsKeyboardFocusWithin) return "view.documents";
        if (PluginPanelOpen && _hosts[_layout.DockOf(SidePanel.Plugin)].IsKeyboardFocusWithin) return "view.pluginPanels";
        if (Active?.Custom is Editing.DiffView) return "compare.text";
        if (Active?.Custom is ImageView) return "view.image";
        if (Active?.Custom is MarkdownPreview || Active?.SidePreview is not null) return "view.markdownModes";
        if (Active?.Custom is not null) return "search.inFiles";
        if (Active?.Paged is not null) return "paged";
        return HelpCatalog.StartTopic;
    }
}

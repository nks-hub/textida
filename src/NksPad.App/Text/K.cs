namespace NksPad.App.Text;

/// <summary>
/// Every UI text key used by the code. Code never passes a key as a bare literal: a typo is a compile error,
/// and tests check that each constant exists in every language and that each key in the catalogue is used here.
/// Plural keys are listed without the category suffix (the catalogue holds <c>.one</c>, <c>.few</c>, <c>.other</c>).
/// </summary>
public static class K
{
    public const string AppName = "app.name";
    public const string LanguageNameCs = "lang.name.cs";
    public const string LanguageNameEn = "lang.name.en";

    public const string MenuFile = "menu.file";
    public const string MenuFileNew = "menu.file.new";
    public const string MenuFileNewWindow = "menu.file.newWindow";
    public const string MenuFileOpen = "menu.file.open";
    public const string MenuFileClose = "menu.file.close";
    public const string MenuFileExit = "menu.file.exit";
    public const string MenuEdit = "menu.edit";
    public const string MenuEditUndo = "menu.edit.undo";
    public const string MenuEditRedo = "menu.edit.redo";
    public const string MenuEditCut = "menu.edit.cut";
    public const string MenuEditCopy = "menu.edit.copy";
    public const string MenuEditPaste = "menu.edit.paste";
    public const string MenuEditSelectAll = "menu.edit.selectAll";
    public const string MenuView = "menu.view";
    public const string MenuViewTheme = "menu.view.theme";
    public const string MenuViewThemeSystem = "menu.view.theme.system";
    public const string MenuViewThemeLight = "menu.view.theme.light";
    public const string MenuViewThemeDark = "menu.view.theme.dark";
    public const string MenuViewLanguage = "menu.view.language";
    public const string MenuViewDocuments = "menu.view.documents";
    public const string MenuHelp = "menu.help";
    public const string MenuHelpAbout = "menu.help.about";

    public const string TabUntitled = "tab.untitled";
    public const string TabCloseA11y = "a11y.tab.close";
    public const string WorkspaceEmpty = "workspace.empty";
    public const string EditorPlaceholder = "editor.placeholder";

    public const string StatusPosition = "status.position";
    public const string StatusTabs = "status.tabs";
    public const string StatusReadOnly = "status.readOnly";
    public const string StatusReady = "status.ready";

    public const string PanelDocumentsTitle = "panel.documents.title";
    public const string PanelDocumentsEmpty = "panel.documents.empty";

    public const string DialogAboutTitle = "dialog.about.title";
    public const string DialogAboutVersion = "dialog.about.version";
    public const string DialogAboutClose = "dialog.about.close";
    public const string DialogOpenTitle = "dialog.open.title";

    public const string ErrorOpenFailed = "error.open.failed";
    public const string ErrorOpenTooLarge = "error.open.tooLarge";

    public const string A11yMainMenu = "a11y.menu.main";
    public const string A11yTabs = "a11y.tabs";
    public const string A11yEditor = "a11y.editor";
    public const string A11yDocumentList = "a11y.documents.list";
    public const string A11yStatusBar = "a11y.statusBar";
    public const string A11yWorkspace = "a11y.workspace";
}

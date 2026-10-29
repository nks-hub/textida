using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Keys;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The <c>when</c> context of the active window, read live. For keys the editor must really have the focus;
/// for menus and the palette an open editor tab counts as focused, because choosing an item moves the focus away.
/// </summary>
internal sealed class ShellContext(MainWindow window, bool forMenu) : IWhenContext
{
    public string? Get(string key)
    {
        var tab = window.Active;
        return key switch
        {
            ContextKeys.EditorFocus => Bool(forMenu || (tab is not null && window.EditorHasFocus)),
            ContextKeys.HasDocument => Bool(tab is not null),
            ContextKeys.Language => tab?.Doc.Language.Id ?? "",
            ContextKeys.HasSelection => Bool(tab?.Surface?.HasSelection == true || tab?.Paged?.HasSelection == true),
            ContextKeys.ReadOnly => Bool(tab?.Surface is null || tab.TableShown), // the paged view takes only the edits it implements
            ContextKeys.LargeFileMode => Bool(tab?.Doc.IsLarge == true || tab?.Doc.IsHex == true),
            ContextKeys.HexMode => Bool(tab?.Hex is not null),
            ContextKeys.TableMode => Bool(tab?.TableShown == true),
            ContextKeys.InMarkdownTable => Bool(window.InMarkdownTable),
            ContextKeys.InSnippet => Bool(tab?.Surface?.InSnippet == true),
            ContextKeys.ImageFile => Bool(tab?.Doc.IsImageFile == true),
            ContextKeys.CompareMode => Bool(tab?.Custom is DiffView),
            ContextKeys.Platform => OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsWindows() ? "windows" : "linux",
            ContextKeys.IsDirty => Bool(tab?.Doc.Doc.IsModified == true),
            ContextKeys.HasFolder => Bool(window.HasFolder),
            ContextKeys.FileSelected => Bool(window.HasFileSelection),
            ContextKeys.PanelOpen => Bool(window.AnySidePanelOpen),
            ContextKeys.MultipleCarets => Bool(tab?.Surface?.IsMulti == true || tab?.Paged?.IsMulti == true),
            ContextKeys.FileKind => tab is null ? "" : DocumentKinds.KindOf(tab.Doc),
            ContextKeys.LanguageGroup => tab is null ? "" : DocumentKinds.GroupOf(tab.Doc.Language.Id),
            ContextKeys.LogMode => Bool(tab?.Doc.IsLog == true),
            ContextKeys.InGitRepo => Bool(tab?.Doc.Path is { } gitPath && RepositoryProbe.IsInside(gitPath)),
            ContextKeys.RemoteFile => Bool(tab?.Doc.RemoteCopy is not null),
            ContextKeys.MarkdownPreview => Bool(window.MarkdownPreviewShown(tab)),
            ContextKeys.TextView => Bool(MainWindow.TextViewShown(tab)),
            ContextKeys.HasPath => Bool(tab?.Doc.Path is not null),
            ContextKeys.PackageManifest => Bool(tab?.Doc.Path is { } manifestPath && DocumentKinds.IsPackageManifest(manifestPath)),
            _ => null,
        };
    }

    private static string Bool(bool value) => value ? "true" : "false";
}

/// <summary>Builds the menu bar from the command registry: a menu appears only when it has at least one command that has a handler.</summary>
internal static class MenuBuilder
{
    /// <summary>The menus of D5 in order. Menus without any command stay out (today: projects, tools and HTML).</summary>
    public static readonly (string Id, string TitleKey)[] Order =
    [
        ("file", "menu.file"), ("projects", "menu.projects"), ("edit", "menu.edit"), ("search", "menu.search"), ("view", "menu.view"),
        ("format", "menu.format"), ("tools", "menu.tools"), ("html", "menu.html"), ("settings", "menu.settings"), ("window", "menu.window"), ("help", "menu.help"),
    ];

    public static List<MenuGroup> Build(AppWorkbench wb, IWhenContext context, Func<string, (bool On, bool Radio)?> checkState, Action<string> run)
    {
        var groups = new List<MenuGroup>();
        foreach (var (id, titleKey) in Order)
        {
            var nodes = Nodes(wb, context, checkState, run, id);
            if (nodes.Count > 0) groups.Add(new MenuGroup(wb.Strings.Get(titleKey), nodes));
        }
        var keys = MenuAccessKeys.Assign([.. groups.Select(g => g.Label)]);
        return [.. groups.Select((g, i) => g with { AccessAt = keys[i] })];
    }

    /// <summary>The items of one menu of the manifest (the context menu of the editor is <c>editor.context</c>), with separators between its groups.</summary>
    public static List<MenuNode> Nodes(AppWorkbench wb, IWhenContext context, Func<string, (bool On, bool Radio)?> checkState, Action<string> run, string menuId)
    {
        var items = wb.Commands.GetMenu(menuId, context).Where(i => wb.Commands.HasHandler(i.CommandId)).ToList();
        return Level(wb, context, checkState, run, items, 0);
    }

    /// <summary>
    /// One level of a menu: the entries that sit directly at this depth, and a submenu for every id that goes deeper. A submenu takes the place (group) of its first entry; its other entries
    /// are pulled in from wherever they follow. A group may be written <c>outer/inner</c>: at the top the outer part separates the items of the menu, in a submenu the whole group does.
    /// </summary>
    private static List<MenuNode> Level(AppWorkbench wb, IWhenContext context, Func<string, (bool On, bool Radio)?> checkState, Action<string> run, List<MenuItemModel> items, int depth)
    {
        var entries = new List<(string Group, string? Segment, List<MenuItemModel> Items)>();
        foreach (var item in items)
        {
            var segment = SegmentAt(item.Submenu, depth);
            var existing = segment is null ? -1 : entries.FindIndex(e => e.Segment == segment);
            if (existing >= 0) entries[existing].Items.Add(item);
            else entries.Add((depth == 0 ? TopGroup(item.Group) : item.Group, segment, [item]));
        }
        var nodes = new List<MenuNode>();
        string? group = null;
        foreach (var (entryGroup, segment, members) in entries)
        {
            if (group is not null && entryGroup != group) nodes.Add(new MenuSeparator());
            group = entryGroup;
            nodes.Add(segment is null
                ? Command(wb, context, checkState, run, members[0])
                : new MenuGroup(wb.Strings.Get("submenu." + SubmenuPath(members[0].Submenu!, depth)), Level(wb, context, checkState, run, members, depth + 1)));
        }
        var keys = MenuAccessKeys.Assign([.. nodes.Select(n => n switch { MenuCommand c => c.Label, MenuGroup g => g.Label, _ => "" })]);
        return [.. nodes.Select((n, i) => n switch { MenuCommand c => c with { AccessAt = keys[i] }, MenuGroup g => g with { AccessAt = keys[i] }, _ => n })];
    }

    /// <summary>A group may be written <c>outer/inner</c>: the outer part separates the items of the menu, the inner part the items of a submenu.</summary>
    private static string TopGroup(string group) => group.Split('/')[0];

    /// <summary>The id of the submenu an entry opens at this depth; null when the entry is a command of this level.</summary>
    private static string? SegmentAt(string? submenu, int depth)
    {
        if (submenu is null) return null;
        var segments = submenu.Split('/');
        return depth < segments.Length ? string.Join('/', segments.Take(depth + 1)) : null;
    }

    /// <summary>The key of the title of the submenu at <paramref name="depth"/> of the path: <c>outer/inner</c> at depth 1 is <c>outer.inner</c>.</summary>
    private static string SubmenuPath(string submenu, int depth) => string.Join('.', submenu.Split('/').Take(depth + 1));

    private static MenuCommand Command(AppWorkbench wb, IWhenContext context, Func<string, (bool On, bool Radio)?> checkState, Action<string> run, MenuItemModel item)
    {
        wb.Commands.TryGet(item.CommandId, out var command);
        var shortcut = wb.Keymap.Bindings.FirstOrDefault(b => b.CommandId == item.CommandId && b.Sequence.Chords.Count == 1);
        var state = checkState(item.CommandId);
        string commandId = item.CommandId;
        return new MenuCommand(
            wb.Strings.Get(item.TitleKey),
            () => run(commandId),
            shortcut is null ? null : KeyMapping.ToGesture(shortcut.Sequence.Chords[0]),
            state?.On, state?.Radio ?? false, commandId,
            () => command.IsEnabled(context),
            state is null ? null : () => checkState(commandId)?.On,
            item.Enabled);
    }
}

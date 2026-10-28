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
        return groups;
    }

    /// <summary>The items of one menu of the manifest (the context menu of the editor is <c>editor.context</c>), with separators between its groups.</summary>
    public static List<MenuNode> Nodes(AppWorkbench wb, IWhenContext context, Func<string, (bool On, bool Radio)?> checkState, Action<string> run, string menuId)
    {
        var items = wb.Commands.GetMenu(menuId, context).Where(i => wb.Commands.HasHandler(i.CommandId)).ToList();
        // a submenu takes the place (group) of its first entry; its other entries are pulled in from wherever they follow
        var entries = new List<(string Group, string? Submenu, List<MenuItemModel> Items)>();
        foreach (var item in items)
        {
            var existing = item.Submenu is null ? -1 : entries.FindIndex(e => e.Submenu == item.Submenu);
            if (existing >= 0) entries[existing].Items.Add(item);
            else entries.Add((TopGroup(item.Group), item.Submenu, [item]));
        }
        var nodes = new List<MenuNode>();
        string? group = null;
        foreach (var (entryGroup, submenu, members) in entries)
        {
            if (group is not null && entryGroup != group) nodes.Add(new MenuSeparator());
            group = entryGroup;
            nodes.Add(submenu is null
                ? Command(wb, context, checkState, run, members[0])
                : new MenuGroup(wb.Strings.Get("submenu." + submenu), WithSeparators(members, m => Command(wb, context, checkState, run, m))));
        }
        return nodes;
    }

    /// <summary>A group may be written <c>outer/inner</c>: the outer part separates the items of the menu, the inner part the items of a submenu.</summary>
    private static string TopGroup(string group) => group.Split('/')[0];

    private static List<MenuNode> WithSeparators(List<MenuItemModel> members, Func<MenuItemModel, MenuNode> toNode)
    {
        var nodes = new List<MenuNode>();
        string? group = null;
        foreach (var m in members)
        {
            if (group is not null && m.Group != group) nodes.Add(new MenuSeparator());
            group = m.Group;
            nodes.Add(toNode(m));
        }
        return nodes;
    }

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

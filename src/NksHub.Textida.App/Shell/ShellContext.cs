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
            ContextKeys.HasSelection => Bool(tab?.Surface?.HasSelection == true),
            ContextKeys.ReadOnly => Bool(tab is null || tab.Doc.ReadOnly),
            ContextKeys.LargeFileMode => Bool(tab?.Doc.IsLarge == true),
            ContextKeys.Platform => OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsWindows() ? "windows" : "linux",
            ContextKeys.IsDirty => Bool(tab?.Doc.Doc.IsModified == true),
            ContextKeys.MultipleCarets => Bool(tab?.Surface?.IsMulti == true),
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
            var items = wb.Commands.GetMenu(id, context).Where(i => wb.Commands.HasHandler(i.CommandId)).ToList();
            if (items.Count == 0) continue;
            var nodes = new List<MenuNode>();
            string? group = null;
            foreach (var item in items)
            {
                if (group is not null && item.Group != group) nodes.Add(new MenuSeparator());
                group = item.Group;
                wb.Commands.TryGet(item.CommandId, out var command);
                var shortcut = wb.Keymap.Bindings.FirstOrDefault(b => b.CommandId == item.CommandId && b.Sequence.Chords.Count == 1);
                var state = checkState(item.CommandId);
                string commandId = item.CommandId;
                nodes.Add(new MenuCommand(
                    wb.Strings.Get(item.TitleKey),
                    () => run(commandId),
                    shortcut is null ? null : KeyMapping.ToGesture(shortcut.Sequence.Chords[0]),
                    state?.On, state?.Radio ?? false, commandId,
                    () => command.IsEnabled(context),
                    state is null ? null : () => checkState(commandId)?.On,
                    item.Enabled));
            }
            groups.Add(new MenuGroup(wb.Strings.Get(titleKey), nodes));
        }
        return groups;
    }
}

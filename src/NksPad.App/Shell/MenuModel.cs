using Avalonia.Controls;
using Avalonia.Input;

namespace NksPad.App.Shell;

/// <summary>Platform-neutral menu description; rendered as an in-window <see cref="Menu"/> on Windows and a <see cref="NativeMenu"/> on macOS.</summary>
internal abstract record MenuNode;

/// <param name="ShowOnly">Gesture is displayed but not registered; the focused control already handles it (cut, copy, paste).</param>
internal sealed record MenuCommand(string Label, Action Run, KeyGesture? Gesture = null, bool? Checked = null, bool Radio = false, bool ShowOnly = false) : MenuNode;

internal sealed record MenuSeparator : MenuNode;

internal sealed record MenuGroup(string Label, IReadOnlyList<MenuNode> Items) : MenuNode;

internal static class MenuRenderer
{
    public static Menu ToMenu(IEnumerable<MenuGroup> groups)
    {
        var menu = new Menu();
        foreach (var g in groups) menu.Items.Add(ToItem(g));
        return menu;
    }

    private static Control ToItem(MenuNode node)
    {
        switch (node)
        {
            case MenuSeparator: return new Separator();
            case MenuGroup g:
                var parent = new MenuItem { Header = g.Label };
                foreach (var child in g.Items) parent.Items.Add(ToItem(child));
                return parent;
            case MenuCommand c:
                var item = new MenuItem { Header = c.Label, InputGesture = c.Gesture };
                if (c.Gesture is not null && !c.ShowOnly) item.HotKey = c.Gesture;
                if (c.Checked is { } on) { item.ToggleType = c.Radio ? MenuItemToggleType.Radio : MenuItemToggleType.CheckBox; item.IsChecked = on; item.GroupName = c.Radio ? "radio" : null; }
                item.Click += (_, _) => c.Run();
                return item;
            default: throw new ArgumentOutOfRangeException(nameof(node));
        }
    }

    /// <summary>Replaces the items of an existing menu. The exporter rejects a different <see cref="NativeMenu"/> instance on a window that already has one.</summary>
    public static void FillNative(NativeMenu menu, IEnumerable<MenuGroup> groups)
    {
        menu.Items.Clear();
        foreach (var g in groups) menu.Items.Add(ToNativeItem(g));
    }

    private static NativeMenuItemBase ToNativeItem(MenuNode node)
    {
        switch (node)
        {
            case MenuSeparator: return new NativeMenuItemSeparator();
            case MenuGroup g:
                var parent = new NativeMenuItem(g.Label) { Menu = new NativeMenu() };
                foreach (var child in g.Items) parent.Menu.Items.Add(ToNativeItem(child));
                return parent;
            case MenuCommand c:
                var item = new NativeMenuItem(c.Label) { Gesture = c.Gesture };
                if (c.Checked is { } on) { item.ToggleType = c.Radio ? MenuItemToggleType.Radio : MenuItemToggleType.CheckBox; item.IsChecked = on; }
                item.Click += (_, _) => c.Run();
                return item;
            default: throw new ArgumentOutOfRangeException(nameof(node));
        }
    }
}

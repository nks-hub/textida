using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;

namespace NksHub.Textida.App.Shell;

/// <summary>Platform-neutral menu description; rendered as an in-window <see cref="Menu"/> on Windows and a <see cref="NativeMenu"/> on macOS.</summary>
internal abstract record MenuNode;

/// <summary>
/// One command in a menu. Keys never run through the menu item: <see cref="KeyDispatcher"/>-style dispatch in the window handles them,
/// the gesture is only displayed. <paramref name="EnabledNow"/> and <paramref name="CheckedNow"/> are asked again each time the menu opens.
/// </summary>
internal sealed record MenuCommand(string Label, Action Run, KeyGesture? Gesture = null, bool? Checked = null, bool Radio = false, string? CommandId = null, Func<bool>? EnabledNow = null, Func<bool?>? CheckedNow = null, bool Enabled = true) : MenuNode
{
    /// <summary>Index in <see cref="Label"/> of the letter that opens the item with Alt; -1 for none.</summary>
    public int AccessAt { get; init; } = -1;
}

internal sealed record MenuSeparator : MenuNode;

internal sealed record MenuGroup(string Label, IReadOnlyList<MenuNode> Items) : MenuNode
{
    /// <summary>Index in <see cref="Label"/> of the letter that opens the menu with Alt; -1 for none.</summary>
    public int AccessAt { get; init; } = -1;
}

/// <summary>
/// Access keys (Alt and a letter) of the rows of one menu. A row takes the first letter of its text, else the first letter of one of its other words, else any letter; no letter is
/// used twice in one menu and only the letters a keyboard has without marks (A to Z, 0 to 9) are used, so the key can be pressed on any layout. A row that finds none has no access key.
/// </summary>
internal static class MenuAccessKeys
{
    /// <summary>The index of the access key letter in each label, or -1.</summary>
    public static int[] Assign(IReadOnlyList<string> labels)
    {
        var at = new int[labels.Count];
        Array.Fill(at, -1);
        var used = new HashSet<char>();
        for (int pass = 0; pass < 3; pass++)
            for (int i = 0; i < labels.Count; i++)
            {
                if (at[i] >= 0) continue;
                foreach (int candidate in Candidates(labels[i], pass))
                    if (used.Add(char.ToLowerInvariant(labels[i][candidate]))) { at[i] = candidate; break; }
            }
        return at;
    }

    private static IEnumerable<int> Candidates(string label, int pass)
    {
        int first = 0;
        while (first < label.Length && label[first] is ' ' or '(' or '.' or '-') first++;
        for (int i = 0; i < label.Length; i++)
        {
            if (!char.IsAsciiLetterOrDigit(label[i])) continue;
            bool wordStart = i == 0 || !char.IsLetterOrDigit(label[i - 1]);
            if (pass == 0 ? i == first : pass == 1 ? wordStart : true) yield return i;
        }
    }

    /// <summary>The text of a header without the access key mark: a single underscore goes, a doubled one is one literal underscore.</summary>
    public static string Unmark(string header)
    {
        var sb = new System.Text.StringBuilder(header.Length);
        for (int i = 0; i < header.Length; i++)
        {
            if (header[i] != '_') sb.Append(header[i]);
            else if (i + 1 < header.Length && header[i + 1] == '_') { sb.Append('_'); i++; }
        }
        return sb.ToString();
    }

    /// <summary>The text with the underscore Avalonia reads as the access key in front of the letter; other underscores are doubled so they stay literal.</summary>
    public static string Mark(string label, int at)
    {
        if (at < 0 || at >= label.Length) return label.Replace("_", "__", StringComparison.Ordinal);
        return label[..at].Replace("_", "__", StringComparison.Ordinal) + "_" + label[at..].Replace("_", "__", StringComparison.Ordinal);
    }
}

internal static class MenuRenderer
{
    public static Menu ToMenu(IEnumerable<MenuGroup> groups)
    {
        var menu = new Menu();
        foreach (var g in groups)
        {
            var item = (MenuItem)ToItem(g);
            item.SubmenuOpened += (_, _) => Refresh(item);
            menu.Items.Add(item);
        }
        return menu;
    }

    /// <summary>Items for a context menu; each is asked for its state now, as the menu opens.</summary>
    public static IEnumerable<Control> ToContextItems(IEnumerable<MenuNode> nodes)
    {
        foreach (var node in nodes)
        {
            var item = ToItem(node);
            if (item is MenuItem { Tag: MenuCommand { EnabledNow: { } enabled } } menuItem) menuItem.IsEnabled = enabled();
            yield return item;
        }
    }

    /// <summary>Asks every command of an opened submenu for its current state.</summary>
    private static void Refresh(MenuItem parent)
    {
        foreach (var child in parent.Items.OfType<MenuItem>())
        {
            if (child.Tag is MenuCommand c)
            {
                if (c.EnabledNow is { } enabled) child.IsEnabled = enabled();
                if (c.CheckedNow is { } check && check() is { } on) child.IsChecked = on;
            }
            else if (child.Items.Count > 0) Refresh(child);
        }
    }

    private static Control ToItem(MenuNode node)
    {
        switch (node)
        {
            case MenuSeparator: return new Separator();
            case MenuGroup g:
                var parent = new MenuItem { Header = MenuAccessKeys.Mark(g.Label, g.AccessAt) };
                AutomationProperties.SetName(parent, g.Label);
                foreach (var child in g.Items) parent.Items.Add(ToItem(child));
                return parent;
            case MenuCommand c:
                var item = new MenuItem { Header = MenuAccessKeys.Mark(c.Label, c.AccessAt), InputGesture = c.Gesture, IsEnabled = c.Enabled, Tag = c };
                AutomationProperties.SetName(item, c.Label);
                if (c.Checked is { } on) { item.ToggleType = c.Radio ? MenuItemToggleType.Radio : MenuItemToggleType.CheckBox; item.IsChecked = on; item.GroupName = c.Radio ? (c.CommandId is { } id ? id[..id.LastIndexOf('.')] : "radio") : null; }
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
                parent.Menu.Opening += (_, _) => RefreshNative(parent.Menu);
                return parent;
            case MenuCommand c:
                var item = new NativeMenuItem(c.Label) { Gesture = c.Gesture, IsEnabled = c.Enabled };
                if (c.Checked is { } on) { item.ToggleType = c.Radio ? MenuItemToggleType.Radio : MenuItemToggleType.CheckBox; item.IsChecked = on; }
                item.Click += (_, _) => c.Run();
                _native.AddOrUpdate(item, c);
                return item;
            default: throw new ArgumentOutOfRangeException(nameof(node));
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<NativeMenuItem, MenuCommand> _native = new();

    private static void RefreshNative(NativeMenu menu)
    {
        foreach (var child in menu.Items.OfType<NativeMenuItem>())
            if (_native.TryGetValue(child, out var c))
            {
                if (c.EnabledNow is { } enabled) child.IsEnabled = enabled();
                if (c.CheckedNow is { } check && check() is { } on) child.IsChecked = on;
            }
    }
}

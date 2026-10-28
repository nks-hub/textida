using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using AvaloniaEdit.Editing;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// What a screen reader gets from the controls of a window, read from the same automation peers it reads: the name, the role, the order the Tab key visits.
/// The gate tests walk the visual trees of the windows and dialogs with this and fail on a control the keyboard can reach that has no name, no role
/// or a name that was never translated.
/// </summary>
internal static class A11yAudit
{
    public static readonly string[] Languages = ["cs", "en", "sk", "de"];

    /// <summary>The control that holds the keyboard in the text views is drawn by the program and published through a native element; its name is the one given to the view.</summary>
    private static bool IsNativeProxied(Control c) => c is TextArea or EditorSurface or PagedTextView;

    /// <summary>A control the keyboard can reach and a screen reader reads out.</summary>
    public static bool IsGated(Control c) =>
        c is not TopLevel and not SelectableTextBlock && c.Focusable && c.IsTabStop && c.IsEffectivelyVisible && c.TemplatedParent is null;

    public sealed record Item(string Control, string Name, AutomationControlType Role);

    /// <summary>Every gated control under <paramref name="root"/>, the items of its menus and the items of the context menus of its controls.</summary>
    public static IReadOnlyList<Item> Collect(Visual root)
    {
        var seen = new HashSet<Control>();
        var items = new List<Item>();
        void Add(Control c)
        {
            if (!seen.Add(c)) return;
            var (name, role) = Read(c);
            items.Add(new Item(Describe(c), name, role));
        }
        foreach (var visual in root.GetSelfAndVisualDescendants())
        {
            if (visual is not Control c) continue;
            if (IsGated(c)) Add(c);
            if (c.ContextMenu is { } context) foreach (var item in MenuItems(context.Items)) Add(item);
        }
        if (root is ILogical logical)
            foreach (var item in logical.GetLogicalDescendants().OfType<MenuItem>()) Add(item);
        return items;
    }

    public static IEnumerable<MenuItem> MenuItems(System.Collections.IEnumerable items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in MenuItems(item.Items)) yield return child;
        }
    }

    /// <summary>The items of a menu that is not in a window: the flyouts of the status bar, the menus the tab strip shows on a click.</summary>
    public static IReadOnlyList<Item> CollectMenu(System.Collections.IEnumerable items) =>
        [.. MenuItems(items).Select(i => { var (name, role) = Read(i); return new Item(Describe(i), name, role); })];

    public static (string Name, AutomationControlType Role) Read(Control c)
    {
        if (IsNativeProxied(c))
        {
            var owner = c is TextArea ? c.FindAncestorOfType<EditorSurface>() ?? c : c;
            return (AutomationProperties.GetName(owner) ?? "", AutomationControlType.Edit);
        }
        try
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(c);
            return (peer.GetName(), peer.GetAutomationControlType());
        }
        catch (Exception e) when (e is InvalidOperationException or NullReferenceException)
        {
            return ("", AutomationControlType.None);
        }
    }

    /// <summary>The type of the control, its name and the types around it, enough to find it in the code.</summary>
    public static string Describe(Control c)
    {
        var parts = new List<string>();
        for (Visual? v = c; v is not null && parts.Count < 4; v = v.GetVisualParent())
            parts.Add(v.GetType().Name + (v is StyledElement { Name: { Length: > 0 } n } ? "#" + n : ""));
        return string.Join(" < ", parts);
    }

    private static readonly Regex TypeName = new(@"^(Avalonia|AvaloniaEdit|System|NksHub)\.[A-Za-z_.0-9]+$", RegexOptions.CultureInvariant);
    private static readonly Regex KeyLike = new(@"^[a-z][a-z0-9]*(\.[A-Za-z0-9]+)+$", RegexOptions.CultureInvariant);
    private static readonly Regex FileLike = new(@"\.[A-Za-z0-9]{1,5}$", RegexOptions.CultureInvariant);

    /// <summary>What is wrong with the name and the role of one control; nothing when they are fine.</summary>
    public static IEnumerable<string> Problems(Item item, IReadOnlySet<string> keys, IEnumerable<string> data)
    {
        if (string.IsNullOrWhiteSpace(item.Name)) yield return "has no accessible name";
        else if (!data.Any(d => item.Name.Contains(d, StringComparison.OrdinalIgnoreCase)))
        {
            if (keys.Contains(item.Name) || KeyLike.IsMatch(item.Name) && !FileLike.IsMatch(item.Name)) yield return $"name '{item.Name}' is a text key, not a text";
            else if (TypeName.IsMatch(item.Name)) yield return $"name '{item.Name}' is the name of a type, not a text";
            if (item.Name.Contains('{') && item.Name.Contains('}')) yield return $"name '{item.Name}' has a placeholder that was not filled in";
        }
        if (item.Role is AutomationControlType.None or AutomationControlType.Custom) yield return $"has no role ({item.Role})";
    }

    // ---- catalogues ----

    private static Dictionary<string, Dictionary<string, string>>? _workbench;

    /// <summary>The command titles of the Workbench catalogue per language, read from the source tree like the other catalogue tests do.</summary>
    public static IReadOnlyDictionary<string, Dictionary<string, string>> WorkbenchCatalogues => _workbench ??= Languages.ToDictionary(l => l,
        l => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Repo.Root, "src", "NksHub.Textida.Workbench", "Resources", $"strings.{l}.json")))!);

    /// <summary>Every key of the two catalogues.</summary>
    public static IReadOnlySet<string> Keys() =>
        Loc.Catalog("cs").Keys.Concat(WorkbenchCatalogues["cs"].Keys).ToHashSet(StringComparer.Ordinal);

    /// <summary>Texts that are the same in all four languages in the catalogues themselves ("OK", names of formats): a control named so is not an untranslated one.</summary>
    public static IReadOnlySet<string> NeutralTexts()
    {
        var neutral = new HashSet<string>(StringComparer.Ordinal);
        void Collect(Func<string, IReadOnlyDictionary<string, string>> catalogue)
        {
            foreach (var (key, text) in catalogue("cs"))
                if (!text.Contains('{') && Languages.All(l => catalogue(l).TryGetValue(key, out var other) && other == text)) neutral.Add(Clean(text));
        }
        Collect(l => Loc.Catalog(l));
        Collect(l => WorkbenchCatalogues[l]);
        foreach (var mode in LanguageModes.All) neutral.Add(mode.Name);
        foreach (var topic in Help.HelpCatalog.Topics("cs"))
            if (Languages.All(l => Help.HelpCatalog.Find(l, topic.Id)?.Title == topic.Title)) neutral.Add(topic.Title);
        return neutral;
    }

    private static string Clean(string text) => text.TrimEnd('.', '…', ' ');

    /// <summary>The names of data types and encodings the hex inspector and the status bar show: they are the same in every language because they are what the format is called.</summary>
    private static readonly Regex Technical = new(@"^(u?int(8|16|32|64)|float(16|32|64)|[US]LEB128|Unix time (32|64)|FILETIME|\.NET ticks|Latin-1|UTF-(8|16|32)|GUID)\b", RegexOptions.CultureInvariant);

    /// <summary>True when a name that is the same in every language is fine: no letters in it, file names and paths of the test, a name of a data format, or a text the catalogues themselves spell the same.</summary>
    public static bool IsLanguageNeutral(string name, IEnumerable<string> data, IReadOnlySet<string> neutral) =>
        !name.Any(char.IsLetter) || data.Any(d => name.Contains(d, StringComparison.OrdinalIgnoreCase)) || neutral.Contains(Clean(name)) || Technical.IsMatch(name);

    // ---- the Tab key ----

    /// <summary>The controls the Tab key visits, in the order it visits them, found with the focus manager of the window.</summary>
    public static IReadOnlyList<InputElement> TabSequence(TopLevel top, int limit = 400)
    {
        var sequence = new List<InputElement>();
        InputElement current = top;
        while (sequence.Count < limit && top.FocusManager?.FindNextElement(NavigationDirection.Next, new FindNextElementOptions { FocusedElement = current }) is InputElement next && !sequence.Contains(next))
        {
            sequence.Add(next);
            current = next;
        }
        return sequence;
    }

    private static Rect BoundsIn(Visual v, Visual top) => v.TranslatePoint(default, top) is { } p ? new Rect(p, v.Bounds.Size) : default;

    /// <summary>
    /// The places where Tab jumps back up the window: the next stop lies wholly above the one before and not in a column to its right. That is how a panel
    /// that docks at the bottom but comes first in the tree shows itself.
    /// </summary>
    public static IEnumerable<string> ReadingOrderProblems(TopLevel top, IReadOnlyList<InputElement> sequence)
    {
        for (int i = 1; i < sequence.Count; i++)
        {
            if (sequence[i - 1] is not Control a || sequence[i] is not Control b) continue;
            var from = BoundsIn(a, top);
            var to = BoundsIn(b, top);
            if (from.Width <= 0 || from.Height <= 0 || to.Width <= 0 || to.Height <= 0) continue;
            if (to.Bottom <= from.Top + 2 && to.Left < from.Right - 2) yield return $"Tab goes up: {Describe(a)} -> {Describe(b)}";
        }
    }
}

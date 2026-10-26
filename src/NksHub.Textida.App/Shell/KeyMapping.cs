using Avalonia.Input;
using NksHub.Textida.Workbench.Keys;
using AvKeyModifiers = Avalonia.Input.KeyModifiers;
using WbKeyModifiers = NksHub.Textida.Workbench.Keys.KeyModifiers;

namespace NksHub.Textida.App.Shell;

/// <summary>Converts between Avalonia key events and the chords of the Workbench keymap.</summary>
internal static class KeyMapping
{
    private static readonly Dictionary<Key, string> Names = Build();
    private static readonly Dictionary<string, Key> Reverse = Names.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.First().Key, StringComparer.Ordinal);

    private static Dictionary<Key, string> Build()
    {
        var map = new Dictionary<Key, string>
        {
            [Key.Enter] = "enter", [Key.Tab] = "tab", [Key.Escape] = "escape", [Key.Space] = "space", [Key.Back] = "backspace",
            [Key.Delete] = "delete", [Key.Insert] = "insert", [Key.Home] = "home", [Key.End] = "end", [Key.PageUp] = "pageup",
            [Key.PageDown] = "pagedown", [Key.Up] = "up", [Key.Down] = "down", [Key.Left] = "left", [Key.Right] = "right",
            [Key.OemPlus] = "equal", [Key.OemMinus] = "minus", [Key.OemComma] = "comma", [Key.OemPeriod] = "period",
            [Key.OemQuestion] = "slash", [Key.OemPipe] = "backslash", [Key.OemBackslash] = "backslash", [Key.OemOpenBrackets] = "bracketleft",
            [Key.OemCloseBrackets] = "bracketright", [Key.OemSemicolon] = "semicolon", [Key.OemQuotes] = "quote", [Key.OemTilde] = "backquote",
        };
        for (var k = Key.A; k <= Key.Z; k++) map[k] = ((char)('a' + (k - Key.A))).ToString();
        for (var k = Key.D0; k <= Key.D9; k++) map[k] = ((char)('0' + (k - Key.D0))).ToString();
        for (var k = Key.F1; k <= Key.F24; k++) map[k] = "f" + (k - Key.F1 + 1);
        return map;
    }

    /// <summary>False for modifier keys alone and for keys the keymap has no name for.</summary>
    public static bool TryChord(Key key, AvKeyModifiers modifiers, out KeyChord chord)
    {
        chord = default;
        if (!Names.TryGetValue(key, out var name)) return false;
        var mods = WbKeyModifiers.None;
        if (modifiers.HasFlag(AvKeyModifiers.Control)) mods |= WbKeyModifiers.Ctrl;
        if (modifiers.HasFlag(AvKeyModifiers.Alt)) mods |= WbKeyModifiers.Alt;
        if (modifiers.HasFlag(AvKeyModifiers.Shift)) mods |= WbKeyModifiers.Shift;
        if (modifiers.HasFlag(AvKeyModifiers.Meta)) mods |= WbKeyModifiers.Meta;
        chord = new KeyChord(mods, name);
        return true;
    }

    /// <summary>Gesture for a menu item; null for a chord the framework has no key for.</summary>
    public static KeyGesture? ToGesture(KeyChord chord)
    {
        if (!Reverse.TryGetValue(chord.Key, out var key)) return null;
        var mods = AvKeyModifiers.None;
        if ((chord.Modifiers & WbKeyModifiers.Ctrl) != 0) mods |= AvKeyModifiers.Control;
        if ((chord.Modifiers & WbKeyModifiers.Alt) != 0) mods |= AvKeyModifiers.Alt;
        if ((chord.Modifiers & WbKeyModifiers.Shift) != 0) mods |= AvKeyModifiers.Shift;
        if ((chord.Modifiers & WbKeyModifiers.Meta) != 0) mods |= AvKeyModifiers.Meta;
        return new KeyGesture(key, mods);
    }
}

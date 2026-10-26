using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace NksPad.Workbench.Keys;

public enum Platform { Windows, MacOS, Linux }

[Flags]
public enum KeyModifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, Meta = 8 }

/// <summary>
/// One key press with modifiers. Text form: <c>ctrl+shift+k</c>; <c>mod</c> means Ctrl on Windows and Linux
/// and Command on macOS. Key names are lower case: letters, digits, <c>f1</c>..<c>f24</c> and the names in <see cref="Names"/>.
/// </summary>
public readonly record struct KeyChord(KeyModifiers Modifiers, string Key)
{
    private static readonly Dictionary<string, string> Named = new(StringComparer.Ordinal)
    {
        ["enter"] = "Enter", ["tab"] = "Tab", ["escape"] = "Esc", ["space"] = "Space", ["backspace"] = "Backspace",
        ["delete"] = "Delete", ["insert"] = "Insert", ["home"] = "Home", ["end"] = "End", ["pageup"] = "Page Up",
        ["pagedown"] = "Page Down", ["up"] = "Up", ["down"] = "Down", ["left"] = "Left", ["right"] = "Right",
        ["plus"] = "+", ["minus"] = "-", ["equal"] = "=", ["slash"] = "/", ["backslash"] = "\\", ["comma"] = ",",
        ["period"] = ".", ["semicolon"] = ";", ["quote"] = "'", ["backquote"] = "`", ["bracketleft"] = "[",
        ["bracketright"] = "]",
    };

    private static readonly Dictionary<string, string> MacGlyphs = new(StringComparer.Ordinal)
    {
        ["enter"] = "↩", ["tab"] = "⇥", ["escape"] = "⎋", ["backspace"] = "⌫", ["delete"] = "⌦",
        ["up"] = "↑", ["down"] = "↓", ["left"] = "←", ["right"] = "→", ["pageup"] = "⇞", ["pagedown"] = "⇟",
        ["home"] = "↖", ["end"] = "↘",
    };

    public static IReadOnlyCollection<string> Names => Named.Keys;

    public static bool IsValidKey(string key)
    {
        if (key.Length == 1)
        {
            return char.IsAsciiLetterLower(key[0]) || char.IsAsciiDigit(key[0]);
        }

        if (key.Length is 2 or 3 && key[0] == 'f' && int.TryParse(key.AsSpan(1), out var n))
        {
            return n is >= 1 and <= 24;
        }

        return Named.ContainsKey(key);
    }

    public static bool TryParse(string text, Platform platform, out KeyChord chord, [NotNullWhen(false)] out string? error)
    {
        chord = default;
        error = null;
        var parts = text.Trim().ToLowerInvariant().Split('+');
        var mods = KeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var m = parts[i] switch
            {
                "ctrl" or "control" => KeyModifiers.Ctrl,
                "alt" or "option" or "opt" => KeyModifiers.Alt,
                "shift" => KeyModifiers.Shift,
                "meta" or "cmd" or "command" or "win" or "super" => KeyModifiers.Meta,
                "mod" => platform == Platform.MacOS ? KeyModifiers.Meta : KeyModifiers.Ctrl,
                _ => KeyModifiers.None,
            };
            if (m == KeyModifiers.None)
            {
                error = $"Unknown modifier '{parts[i]}' in '{text}'.";
                return false;
            }

            if ((mods & m) != 0)
            {
                error = $"Repeated modifier '{parts[i]}' in '{text}'.";
                return false;
            }

            mods |= m;
        }

        var key = parts[^1];
        if (!IsValidKey(key))
        {
            error = $"Unknown key '{key}' in '{text}'.";
            return false;
        }

        chord = new KeyChord(mods, key);
        return true;
    }

    /// <summary>Canonical text form with a fixed modifier order, e.g. <c>ctrl+alt+shift+meta+k</c>.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        if ((Modifiers & KeyModifiers.Ctrl) != 0) sb.Append("ctrl+");
        if ((Modifiers & KeyModifiers.Alt) != 0) sb.Append("alt+");
        if ((Modifiers & KeyModifiers.Shift) != 0) sb.Append("shift+");
        if ((Modifiers & KeyModifiers.Meta) != 0) sb.Append("meta+");
        return sb.Append(Key).ToString();
    }

    /// <summary>Text shown to the user: <c>Ctrl+Shift+K</c> on Windows and Linux, <c>⇧⌘K</c> on macOS.</summary>
    public string Display(Platform platform)
    {
        var sb = new StringBuilder();
        if (platform == Platform.MacOS)
        {
            if ((Modifiers & KeyModifiers.Ctrl) != 0) sb.Append('⌃');
            if ((Modifiers & KeyModifiers.Alt) != 0) sb.Append('⌥');
            if ((Modifiers & KeyModifiers.Shift) != 0) sb.Append('⇧');
            if ((Modifiers & KeyModifiers.Meta) != 0) sb.Append('⌘');
            return sb.Append(MacGlyphs.TryGetValue(Key, out var g) ? g : KeyLabel()).ToString();
        }

        if ((Modifiers & KeyModifiers.Ctrl) != 0) sb.Append("Ctrl+");
        if ((Modifiers & KeyModifiers.Alt) != 0) sb.Append("Alt+");
        if ((Modifiers & KeyModifiers.Shift) != 0) sb.Append("Shift+");
        if ((Modifiers & KeyModifiers.Meta) != 0) sb.Append(platform == Platform.Windows ? "Win+" : "Super+");
        return sb.Append(KeyLabel()).ToString();
    }

    private string KeyLabel() => Named.TryGetValue(Key, out var n) ? n : Key.ToUpperInvariant();
}

/// <summary>One or more chords pressed in turn, e.g. <c>ctrl+k ctrl+u</c>.</summary>
public sealed class KeySequence : IEquatable<KeySequence>
{
    public const int MaxChords = 4;

    private readonly string _text;

    private KeySequence(KeyChord[] chords)
    {
        Chords = chords;
        _text = string.Join(' ', chords.Select(c => c.ToString()));
    }

    public IReadOnlyList<KeyChord> Chords { get; }

    public static bool TryParse(string text, Platform platform, [NotNullWhen(true)] out KeySequence? sequence, [NotNullWhen(false)] out string? error)
    {
        sequence = null;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > MaxChords)
        {
            error = $"A key sequence has 1 to {MaxChords} chords: '{text}'.";
            return false;
        }

        var chords = new KeyChord[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!KeyChord.TryParse(parts[i], platform, out chords[i], out error))
            {
                return false;
            }
        }

        sequence = new KeySequence(chords);
        error = null;
        return true;
    }

    public bool StartsWith(IReadOnlyList<KeyChord> prefix)
    {
        if (prefix.Count > Chords.Count)
        {
            return false;
        }

        for (var i = 0; i < prefix.Count; i++)
        {
            if (!Chords[i].Equals(prefix[i]))
            {
                return false;
            }
        }

        return true;
    }

    public bool IsStrictPrefixOf(KeySequence other) => Chords.Count < other.Chords.Count && other.StartsWith(Chords);

    public string Display(Platform platform) => string.Join(' ', Chords.Select(c => c.Display(platform)));

    public bool Equals(KeySequence? other) => other is not null && _text == other._text;

    public override bool Equals(object? obj) => Equals(obj as KeySequence);

    public override int GetHashCode() => _text.GetHashCode(StringComparison.Ordinal);

    /// <summary>Canonical text form.</summary>
    public override string ToString() => _text;
}

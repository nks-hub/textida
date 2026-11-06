using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Avalonia.Media;
using NksHub.Textida.App.Shell;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Colors;

namespace NksHub.Textida.App.Services;

/// <summary>
/// A colour theme of the editor (P3.2.5): the surfaces of the text area (background, text, current line, selection, line numbers, minimap) and a colour for every token kind of the
/// grammars. Built-in themes, custom ones saved in the profile and themes imported from TextMate (<c>.tmTheme</c>) or VS Code (<c>.json</c>) are all of this shape; a custom theme
/// is written in the VS Code format, so an exported theme opens in VS Code and comes back unchanged.
/// </summary>
internal sealed class EditorColorTheme
{
    public const string Background = "background", Foreground = "foreground", CurrentLine = "currentLine", Selection = "selection", LineNumbers = "lineNumbers", Minimap = "minimap";

    public static IReadOnlyList<string> Surfaces { get; } = [Background, Foreground, CurrentLine, Selection, LineNumbers, Minimap];

    /// <summary>The token kinds the palette colours; the same kinds <see cref="SyntaxPalette"/> knows. <c>log.highlight</c> is a background under the text.</summary>
    public static IReadOnlyList<string> Tokens { get; } =
    [
        "keyword", "string", "comment", "number", "type", "function", "variable", "property", "constant", "tag", "attribute", "preprocessor", "meta", "regex", "heading", "link", "code",
        "log.error", "log.warning", "log.info", "log.debug", "log.highlight",
    ];

    public static IEnumerable<string> Keys => Surfaces.Concat(Tokens);

    private SyntaxPalette? _palette;

    public EditorColorTheme(string id, string name, bool dark, IReadOnlyDictionary<string, Color> colors, SyntaxPalette? palette = null)
    {
        Id = id;
        Name = name;
        IsDark = dark;
        Colors = colors;
        _palette = palette;
    }

    public string Id { get; }
    public string Name { get; }
    public bool IsDark { get; }
    public IReadOnlyDictionary<string, Color> Colors { get; }

    public Color this[string key] => Colors[key];

    public SyntaxPalette Palette => _palette ??= new SyntaxPalette(Tokens.ToDictionary(k => k, k => Colors[k], StringComparer.Ordinal));

    /// <summary>The theme with some of its colours replaced; the others and the name stay.</summary>
    public EditorColorTheme With(IReadOnlyDictionary<string, Color> overrides)
    {
        if (overrides.Count == 0) return this;
        var colors = Colors.ToDictionary(p => p.Key, p => overrides.TryGetValue(p.Key, out var c) ? c : p.Value, StringComparer.Ordinal);
        return new EditorColorTheme(Id, Name, IsDark, colors);
    }

    public EditorColorTheme Renamed(string id, string name) => new(id, name, IsDark, Colors, _palette);

    // ---- colours ----

    /// <summary>A colour written as CSS hex (#RGB, #RGBA, #RRGGBB, #RRGGBBAA, the alpha last as VS Code and TextMate write it), or null.</summary>
    public static Color? ParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !ColorLiterals.TryParse(text.Trim(), out var match) || match.Format != ColorFormat.Hex) return null;
        var c = match.Color;
        return Color.FromArgb((byte)Math.Round(c.A * 255), c.R, c.G, c.B);
    }

    public static string Format(Color c) => ColorLiterals.Format(new Rgba(c.R, c.G, c.B, c.A / 255.0), ColorFormat.Hex, upperHex: true);

    /// <summary>A colour with transparency as it shows over <paramref name="under"/>.</summary>
    public static Color Over(Color top, Color under)
    {
        double a = top.A / 255.0;
        byte Mix(byte t, byte u) => (byte)Math.Round(t * a + u * (1 - a));
        return Color.FromRgb(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B));
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        byte M(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromArgb(from.A, M(from.R, to.R), M(from.G, to.G), M(from.B, to.B));
    }

    /// <summary>The ratio a text needs on the editor and on the current line, as for the built-in UI themes.</summary>
    public const double TextRatio = 4.5;

    /// <summary>The colour moved towards white on a dark theme (black on a light one) in small steps until it reads on every one of <paramref name="fills"/>.</summary>
    private static Color Readable(Color color, bool dark, params Color[] fills)
    {
        var target = dark ? Avalonia.Media.Colors.White : Avalonia.Media.Colors.Black;
        for (int step = 0; step <= 50; step++)
        {
            var candidate = Mix(color, target, step / 50.0);
            if (fills.All(f => ContrastColors.Ratio(candidate, f) >= TextRatio)) return candidate;
        }
        return target;
    }

    /// <summary>
    /// The colours of a built-in theme raised to the contrast the editor promises: text, tokens and line numbers read on the background and the current line, the text reads on the
    /// selection and on the highlight of a log. A colour that already reads stays as published.
    /// </summary>
    private static Dictionary<string, Color> EnsureReadable(Dictionary<string, Color> colors, bool dark)
    {
        var back = colors[Background];
        var line = Over(colors[CurrentLine], back);
        colors[Foreground] = Readable(colors[Foreground], dark, back, line);
        colors[LineNumbers] = Readable(colors[LineNumbers], dark, back, line);
        foreach (var kind in Tokens.Where(k => k != "log.highlight")) colors[kind] = Readable(colors[kind], dark, back, line);
        var text = colors[Foreground];
        Color Behind(Color fill)
        {
            for (int step = 0; step <= 50 && ContrastColors.Ratio(text, fill) < TextRatio; step++) fill = Mix(fill, back, 0.02 * step);
            return fill;
        }
        var selection = Over(colors[Selection], back);
        if (ContrastColors.Ratio(text, selection) < TextRatio) colors[Selection] = Behind(selection);
        colors["log.highlight"] = Behind(Over(colors["log.highlight"], back));
        return colors;
    }

    // ---- the views beside the editor ----

    /// <summary>The hues the tints of a comparison start from; the theme's background takes them in, so they sit in every theme.</summary>
    private static readonly Color RemovedHue = Color.Parse("#E5534B"), AddedHue = Color.Parse("#3FB950"), ChangedHue = Color.Parse("#D29922");

    private Dictionary<string, Color>? _viewColors;

    /// <summary>
    /// The colours the hex view, the CSV table and the comparison draw in under this theme, by the keys of the window theme they replace. Text, muted text, line numbers, the accent
    /// and the error colour read at <see cref="TextRatio"/> on the background and the current line; the text reads on the selection and on every tint (changes, differences, warnings).
    /// </summary>
    public IReadOnlyDictionary<string, Color> ViewColors => _viewColors ??= BuildViewColors();

    private Dictionary<string, Color> BuildViewColors()
    {
        var back = Colors[Background];
        back = Color.FromRgb(back.R, back.G, back.B);
        var line = Over(Colors[CurrentLine], back);
        var fore = Readable(Over(Colors[Foreground], back), IsDark, back, line);
        var accent = Readable(Over(Colors["keyword"], back), IsDark, back, line);
        Color Tint(Color hue, double amount)
        {
            var fill = Mix(back, hue, amount);
            for (int step = 0; step <= 50 && ContrastColors.Ratio(fore, fill) < TextRatio; step++) fill = Mix(fill, back, 0.1);
            return ContrastColors.Ratio(fore, fill) >= TextRatio ? fill : back;
        }
        double strength = IsDark ? 0.3 : 0.22;
        var numbers = Readable(Over(Colors[LineNumbers], back), IsDark, back, line);
        return new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["BgEditor"] = back,
            ["Fg"] = fore,
            ["EditorFg"] = fore,
            ["FgMuted"] = Readable(Mix(fore, back, 0.3), IsDark, back, line),
            ["LineNumbers"] = numbers,
            ["Accent"] = accent,
            ["AccentSoft"] = Tint(accent, strength * 0.8),
            ["SelectionBg"] = Tint(Over(Colors[Selection], back), 1),
            ["CurrentLine"] = Tint(line, 1),
            ["Border"] = Over(Color.FromArgb(0x66, numbers.R, numbers.G, numbers.B), back),
            ["Danger"] = Readable(Over(Colors["log.error"], back), IsDark, back, line),
            ["Highlight"] = Tint(Over(Colors["log.highlight"], back), 1),
            ["WarnBg"] = Tint(ChangedHue, strength),
            ["DiffRemoved"] = Tint(RemovedHue, strength),
            ["DiffAdded"] = Tint(AddedHue, strength),
        };
    }

    // ---- built-in themes ----

    public const string AutoId = "auto", LightId = "light", DarkId = "dark";

    /// <summary>Prefix of the id of a custom theme; the name of its file follows.</summary>
    public const string UserPrefix = "user:";

    private static Dictionary<string, Color> PaletteColors(SyntaxPalette palette) =>
        Tokens.ToDictionary(k => k, k => ((ISolidColorBrush)palette.GetBrush(k)!).Color, StringComparer.Ordinal);

    /// <summary>The light and the dark theme of Textida: the palettes of the editor and the brushes of App.axaml (a test checks they agree).</summary>
    private static EditorColorTheme Own(bool dark)
    {
        var colors = PaletteColors(dark ? SyntaxPalette.Dark : SyntaxPalette.Light);
        string[] surfaces = dark ? ["#1F1F1F", "#D4D7DC", "#25272B", "#553DC2AC", "#8F95A0", "#1F1F1F"] : ["#FFFFFF", "#1B1D21", "#F1F4F8", "#3D0F7B6C", "#656B75", "#FFFFFF"];
        for (int i = 0; i < Surfaces.Count; i++) colors[Surfaces[i]] = Color.Parse(surfaces[i]); // #AARRGGBB as App.axaml writes them
        return new EditorColorTheme(dark ? DarkId : LightId, dark ? DarkId : LightId, dark, colors, dark ? SyntaxPalette.Dark : SyntaxPalette.Light);
    }

    /// <summary>
    /// A theme from a published palette: the surfaces and <paramref name="tokens"/> as <c>kind=#hex</c> pairs. A kind the pairs leave out takes the colour of the light or dark palette
    /// of Textida (the log levels), <c>meta</c> the colour of <c>preprocessor</c>; then <see cref="EnsureReadable"/>.
    /// </summary>
    private static EditorColorTheme Define(string id, string name, bool dark, string back, string fore, string line, string selection, string numbers, string tokens)
    {
        var colors = PaletteColors(dark ? SyntaxPalette.Dark : SyntaxPalette.Light);
        foreach (var (key, value) in ParseOverrides(tokens)) colors[key] = value;
        if (!tokens.Contains("meta=", StringComparison.Ordinal)) colors["meta"] = colors["preprocessor"];
        colors[Background] = colors[Minimap] = ParseColor(back)!.Value;
        colors[Foreground] = ParseColor(fore)!.Value;
        colors[CurrentLine] = ParseColor(line)!.Value;
        colors[Selection] = ParseColor(selection)!.Value;
        colors[LineNumbers] = ParseColor(numbers)!.Value;
        return new EditorColorTheme(id, name, dark, EnsureReadable(colors, dark));
    }

    /// <summary>The built-in themes in the order the list shows them. The sources and licences of the palettes are in THIRD-PARTY-NOTICES.md.</summary>
    public static IReadOnlyList<EditorColorTheme> BuiltIn { get; } =
    [
        Own(false),
        Own(true),
        Define("monokai", "Monokai", true, "#272822", "#F8F8F2", "#3E3D32", "#49483E", "#90908A",
            "keyword=#F92672;string=#E6DB74;comment=#88846F;number=#AE81FF;constant=#AE81FF;type=#66D9EF;function=#A6E22E;variable=#F8F8F2;property=#F8F8F2;tag=#F92672;attribute=#A6E22E;preprocessor=#F92672;meta=#66D9EF;regex=#FD971F;heading=#A6E22E;link=#66D9EF;code=#E6DB74"),
        Define("solarized-light", "Solarized Light", false, "#FDF6E3", "#657B83", "#EEE8D5", "#DDD6C1", "#93A1A1",
            "keyword=#859900;string=#2AA198;comment=#93A1A1;number=#D33682;constant=#CB4B16;type=#B58900;function=#268BD2;variable=#657B83;property=#268BD2;tag=#268BD2;attribute=#6C71C4;preprocessor=#CB4B16;meta=#6C71C4;regex=#DC322F;heading=#268BD2;link=#6C71C4;code=#2AA198"),
        Define("solarized-dark", "Solarized Dark", true, "#002B36", "#839496", "#073642", "#274642", "#586E75",
            "keyword=#859900;string=#2AA198;comment=#586E75;number=#D33682;constant=#CB4B16;type=#B58900;function=#268BD2;variable=#839496;property=#268BD2;tag=#268BD2;attribute=#6C71C4;preprocessor=#CB4B16;meta=#6C71C4;regex=#DC322F;heading=#268BD2;link=#6C71C4;code=#2AA198"),
        Define("dracula", "Dracula", true, "#282A36", "#F8F8F2", "#343746", "#44475A", "#6272A4",
            "keyword=#FF79C6;string=#F1FA8C;comment=#6272A4;number=#BD93F9;constant=#BD93F9;type=#8BE9FD;function=#50FA7B;variable=#F8F8F2;property=#F8F8F2;tag=#FF79C6;attribute=#50FA7B;preprocessor=#FF79C6;meta=#FFB86C;regex=#FF5555;heading=#BD93F9;link=#8BE9FD;code=#50FA7B"),
        Define("one-dark", "One Dark", true, "#282C34", "#ABB2BF", "#2C313C", "#3E4451", "#636D83",
            "keyword=#C678DD;string=#98C379;comment=#5C6370;number=#D19A66;constant=#D19A66;type=#E5C07B;function=#61AFEF;variable=#E06C75;property=#E06C75;tag=#E06C75;attribute=#D19A66;preprocessor=#C678DD;meta=#56B6C2;regex=#56B6C2;heading=#E06C75;link=#56B6C2;code=#98C379"),
        Define("github-light", "GitHub Light", false, "#FFFFFF", "#1F2328", "#EAEEF280", "#0969DA33", "#8C959F",
            "keyword=#CF222E;string=#0A3069;comment=#6E7781;number=#0550AE;constant=#0550AE;type=#953800;function=#8250DF;variable=#1F2328;property=#0550AE;tag=#116329;attribute=#0550AE;preprocessor=#CF222E;meta=#8250DF;regex=#116329;heading=#0550AE;link=#0A3069;code=#0550AE"),
        Define("github-dark", "GitHub Dark", true, "#0D1117", "#E6EDF3", "#6E76811A", "#388BFD66", "#6E7681",
            "keyword=#FF7B72;string=#A5D6FF;comment=#8B949E;number=#79C0FF;constant=#79C0FF;type=#FFA657;function=#D2A8FF;variable=#E6EDF3;property=#79C0FF;tag=#7EE787;attribute=#79C0FF;preprocessor=#FF7B72;meta=#D2A8FF;regex=#7EE787;heading=#1F6FEB;link=#A5D6FF;code=#79C0FF"),
        Define("nord", "Nord", true, "#2E3440", "#D8DEE9", "#3B4252", "#434C5E", "#4C566A",
            "keyword=#81A1C1;string=#A3BE8C;comment=#616E88;number=#B48EAD;constant=#81A1C1;type=#8FBCBB;function=#88C0D0;variable=#D8DEE9;property=#D8DEE9;tag=#81A1C1;attribute=#8FBCBB;preprocessor=#5E81AC;meta=#D08770;regex=#EBCB8B;heading=#88C0D0;link=#88C0D0;code=#A3BE8C"),
    ];

    public static EditorColorTheme Light => BuiltIn[0];

    public static EditorColorTheme Dark => BuiltIn[1];

    // ---- overrides as the setting stores them ----

    /// <summary>Reads <c>kind=#hex;kind=#hex</c>; pairs with an unknown key or a colour that does not parse are left out.</summary>
    public static Dictionary<string, Color> ParseOverrides(string? text)
    {
        var result = new Dictionary<string, Color>(StringComparer.Ordinal);
        foreach (var pair in (text ?? "").Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            string key = pair[..eq].Trim();
            if (Keys.Contains(key) && ParseColor(pair[(eq + 1)..]) is { } color) result[key] = color;
        }
        return result;
    }

    public static string FormatOverrides(IReadOnlyDictionary<string, Color> overrides) =>
        string.Join(";", Keys.Where(overrides.ContainsKey).Select(k => k + "=" + Format(overrides[k])));

    // ---- VS Code and TextMate ----

    /// <summary>The scope a kind is written under in an exported theme, then the scopes its colour is looked up by in an imported one, most telling first.</summary>
    private static readonly (string Kind, string[] Scopes)[] ScopeTable =
    [
        ("keyword", ["keyword.control", "keyword", "storage.modifier", "storage"]),
        ("string", ["string.quoted.double", "string.quoted", "string"]),
        ("comment", ["comment.line", "comment.block", "comment"]),
        ("number", ["constant.numeric"]),
        ("constant", ["constant.language", "constant.other", "support.constant", "constant"]),
        ("type", ["entity.name.type", "entity.name.class", "support.type", "support.class", "storage.type"]),
        ("function", ["entity.name.function", "support.function", "meta.function-call"]),
        ("variable", ["variable.other", "variable"]),
        ("property", ["variable.other.property", "variable.other.member", "support.type.property-name", "meta.property-name"]),
        ("tag", ["entity.name.tag"]),
        ("attribute", ["entity.other.attribute-name"]),
        ("preprocessor", ["meta.preprocessor", "keyword.control.directive", "keyword.preprocessor"]),
        ("meta", ["meta.annotation", "storage.type.annotation", "meta.decorator", "meta.attribute"]),
        ("regex", ["string.regexp"]),
        ("heading", ["markup.heading", "entity.name.section"]),
        ("link", ["markup.underline.link", "string.other.link"]),
        ("code", ["markup.inline.raw", "markup.raw", "markup.fenced_code"]),
        ("log.error", ["textida.log.error"]),
        ("log.warning", ["textida.log.warning"]),
        ("log.info", ["textida.log.info"]),
        ("log.debug", ["textida.log.debug"]),
        ("log.highlight", ["textida.log.highlight"]),
    ];

    /// <summary>The surfaces and the keys of <c>colors</c> in a VS Code theme (and of the global settings of a TextMate theme, which are mapped onto them).</summary>
    private static readonly (string Surface, string VsCode, string TextMate)[] SurfaceTable =
    [
        (Background, "editor.background", "background"),
        (Foreground, "editor.foreground", "foreground"),
        (CurrentLine, "editor.lineHighlightBackground", "lineHighlight"),
        (Selection, "editor.selectionBackground", "selection"),
        (LineNumbers, "editorLineNumber.foreground", "gutterForeground"),
        (Minimap, "minimap.background", "minimapBackground"),
    ];

    /// <summary>The theme in the VS Code format: <c>colors</c> for the surfaces, <c>tokenColors</c> with one rule per kind.</summary>
    public string ToVsCodeJson()
    {
        var colors = new JsonObject();
        foreach (var (surface, key, _) in SurfaceTable) colors[key] = Format(Colors[surface]);
        var rules = new JsonArray();
        foreach (var (kind, scopes) in ScopeTable)
            rules.Add((JsonNode)new JsonObject { ["name"] = kind, ["scope"] = scopes[0], ["settings"] = new JsonObject { ["foreground"] = Format(Colors[kind]) } });
        var root = new JsonObject { ["name"] = Name, ["type"] = IsDark ? "dark" : "light", ["colors"] = colors, ["tokenColors"] = rules };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>Largest theme file read; real themes are a few tens of kilobytes.</summary>
    public const long MaxFileBytes = 4 << 20;

    /// <summary>Reads a theme file: TextMate (<c>.tmTheme</c>, an XML property list) or VS Code (JSON with comments). Throws <see cref="FormatException"/> with the reason.</summary>
    public static EditorColorTheme Load(string path, string id)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes) throw new FormatException($"The file is larger than {MaxFileBytes >> 20} MiB.");
        string text = File.ReadAllText(path);
        string fallback = Path.GetFileNameWithoutExtension(path);
        return text.TrimStart().StartsWith('<') ? FromTmTheme(text, id, fallback) : FromVsCode(text, id, fallback);
    }

    public static EditorColorTheme FromVsCode(string json, string id, string fallbackName)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException e) { throw new FormatException(e.Message, e); }
        if (root is not JsonObject theme) throw new FormatException("The file is not a JSON object.");
        if (theme["tokenColors"] is JsonValue reference) throw new FormatException($"The token colours are in another file ({reference}); import that file.");
        var globals = new Dictionary<string, Color>(StringComparer.Ordinal);
        if (theme["colors"] is JsonObject colors)
            foreach (var (surface, key, _) in SurfaceTable)
                if (ParseColor(Text(colors[key])) is { } c) globals[surface] = c;
        var rules = Rules(theme["tokenColors"] as JsonArray ?? theme["settings"] as JsonArray, globals);
        if (globals.Count == 0 && rules.Count == 0) throw new FormatException("The file has neither editor colours nor token colours.");
        bool? dark = Text(theme["type"]) switch { "dark" or "hc-black" => true, "light" or "hc-light" => false, _ => null };
        return Build(id, Text(theme["name"]) is { Length: > 0 } name ? name : fallbackName, dark, globals, rules);
    }

    public static EditorColorTheme FromTmTheme(string xml, string id, string fallbackName)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException e) { throw new FormatException(e.Message, e); }
        if (document.Root?.Element("dict") is not { } top || Plist(top) is not JsonObject theme) throw new FormatException("The file is not a TextMate theme (no property list).");
        var globals = new Dictionary<string, Color>(StringComparer.Ordinal);
        var rules = Rules(theme["settings"] as JsonArray, globals);
        if (globals.Count == 0 && rules.Count == 0) throw new FormatException("The file has neither editor colours nor token colours.");
        return Build(id, Text(theme["name"]) is { Length: > 0 } name ? name : fallbackName, null, globals, rules);
    }

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>A property list element as JSON: dict, array, string, numbers and booleans.</summary>
    private static JsonNode? Plist(XElement element) => element.Name.LocalName switch
    {
        "dict" => DictOf(element),
        "array" => new JsonArray([.. element.Elements().Select(Plist)]),
        "string" or "date" or "data" => JsonValue.Create(element.Value),
        "integer" or "real" => double.TryParse(element.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? JsonValue.Create(n) : null,
        "true" => JsonValue.Create(true),
        "false" => JsonValue.Create(false),
        _ => null,
    };

    private static JsonObject DictOf(XElement dict)
    {
        var result = new JsonObject();
        string? key = null;
        foreach (var child in dict.Elements())
        {
            if (child.Name.LocalName == "key") key = child.Value;
            else if (key is not null)
            {
                result[key] = Plist(child);
                key = null;
            }
        }
        return result;
    }

    /// <summary>
    /// The rules of a theme as (scope selector, foreground). A rule without a scope carries the global colours of a TextMate theme, which go into <paramref name="globals"/> unless
    /// <c>colors</c> of VS Code already gave them. Selectors with a space (descendants) or an exclusion only apply in a context and are skipped.
    /// </summary>
    private static List<(string Selector, Color Foreground)> Rules(JsonArray? array, Dictionary<string, Color> globals)
    {
        var rules = new List<(string, Color)>();
        foreach (var rule in (array ?? []).OfType<JsonObject>())
        {
            if (rule["settings"] is not JsonObject settings) continue;
            var scopes = rule["scope"] switch
            {
                JsonArray list => list.Select(Text).OfType<string>(),
                JsonValue one when Text(one) is { } text => text.Split(','),
                _ => null,
            };
            if (scopes is null)
            {
                foreach (var (surface, _, key) in SurfaceTable)
                    if (!globals.ContainsKey(surface) && ParseColor(Text(settings[key])) is { } c) globals[surface] = c;
                continue;
            }
            if (ParseColor(Text(settings["foreground"])) is not { } foreground) continue;
            foreach (var scope in scopes.Select(s => s.Trim()))
                if (scope.Length > 0 && !scope.Contains(' ') && !scope.StartsWith('-')) rules.Add((scope, foreground));
        }
        return rules;
    }

    /// <summary>The colour a theme gives a scope as TextMate matches it: the rule whose selector is the longest prefix of the scope (by segments) wins, a later one on a tie.</summary>
    private static Color? Match(List<(string Selector, Color Foreground)> rules, string scope)
    {
        Color? best = null;
        int bestLength = -1;
        foreach (var (selector, color) in rules)
            if ((scope == selector || scope.StartsWith(selector + ".", StringComparison.Ordinal)) && selector.Length >= bestLength)
                (best, bestLength) = (color, selector.Length);
        return best;
    }

    private static EditorColorTheme Build(string id, string name, bool? type, Dictionary<string, Color> globals, List<(string Selector, Color Foreground)> rules)
    {
        bool dark = type ?? (globals.TryGetValue(Background, out var b) ? ContrastColors.Luminance(b) < 0.5 : false);
        var basis = dark ? Dark : Light;
        var back = globals.GetValueOrDefault(Background, basis[Background]);
        back = Color.FromRgb(back.R, back.G, back.B);
        var fore = Over(globals.GetValueOrDefault(Foreground, basis[Foreground]), back);
        var colors = new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            [Background] = back,
            [Foreground] = fore,
            [CurrentLine] = globals.GetValueOrDefault(CurrentLine, Mix(back, fore, 0.06)),
            [Selection] = globals.GetValueOrDefault(Selection, Mix(back, fore, 0.22)),
            [LineNumbers] = Over(globals.GetValueOrDefault(LineNumbers, Mix(fore, back, 0.4)), back),
            [Minimap] = Over(globals.GetValueOrDefault(Minimap, back), back),
        };
        foreach (var (kind, scopes) in ScopeTable)
        {
            var found = scopes.Select(s => Match(rules, s)).FirstOrDefault(c => c is not null);
            colors[kind] = found is { } c ? (kind == "log.highlight" ? c : Over(c, back))
                : kind.StartsWith("log.", StringComparison.Ordinal) ? basis[kind]
                : kind == "meta" && colors.TryGetValue("preprocessor", out var pre) ? pre
                : fore;
        }
        return new EditorColorTheme(id, name, dark, colors);
    }
}

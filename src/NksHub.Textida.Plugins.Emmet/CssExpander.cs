using System.Globalization;
using System.Text;

namespace NksHub.Textida.Plugins.Emmet;

/// <summary>
/// Expands CSS abbreviations: a short name for a property, then values (<c>m10-20</c> is <c>margin: 10px 20px;</c>), <c>:keyword</c> or a colour, <c>!</c> for important,
/// and <c>+</c> for several declarations. The short names and keyword letters are this editor's own table of common properties; the number rules (unit letters, <c>px</c>
/// for whole numbers, <c>em</c> for fractions) follow the public Emmet documentation.
/// </summary>
public static class CssExpander
{
    private static readonly Dictionary<string, string> Properties = new(StringComparer.Ordinal)
    {
        ["m"] = "margin", ["mt"] = "margin-top", ["mr"] = "margin-right", ["mb"] = "margin-bottom", ["ml"] = "margin-left",
        ["p"] = "padding", ["pt"] = "padding-top", ["pr"] = "padding-right", ["pb"] = "padding-bottom", ["pl"] = "padding-left",
        ["w"] = "width", ["h"] = "height", ["miw"] = "min-width", ["mih"] = "min-height", ["maw"] = "max-width", ["mah"] = "max-height",
        ["t"] = "top", ["r"] = "right", ["b"] = "bottom", ["l"] = "left", ["z"] = "z-index",
        ["d"] = "display", ["pos"] = "position", ["fl"] = "float", ["cl"] = "clear", ["ov"] = "overflow", ["ovx"] = "overflow-x", ["ovy"] = "overflow-y",
        ["v"] = "visibility", ["op"] = "opacity", ["c"] = "color",
        ["bg"] = "background", ["bgc"] = "background-color", ["bgi"] = "background-image", ["bgr"] = "background-repeat", ["bgp"] = "background-position", ["bgs"] = "background-size",
        ["bd"] = "border", ["bdt"] = "border-top", ["bdr"] = "border-right", ["bdb"] = "border-bottom", ["bdl"] = "border-left",
        ["bdc"] = "border-color", ["bdw"] = "border-width", ["bds"] = "border-style", ["bdrs"] = "border-radius", ["ol"] = "outline",
        ["ff"] = "font-family", ["fz"] = "font-size", ["fw"] = "font-weight", ["fs"] = "font-style", ["lh"] = "line-height", ["ls"] = "letter-spacing", ["ws"] = "word-spacing",
        ["ta"] = "text-align", ["td"] = "text-decoration", ["tt"] = "text-transform", ["ti"] = "text-indent", ["tsh"] = "text-shadow", ["tov"] = "text-overflow",
        ["bxsh"] = "box-shadow", ["bxz"] = "box-sizing", ["va"] = "vertical-align", ["whs"] = "white-space", ["cur"] = "cursor", ["ct"] = "content",
        ["fx"] = "flex", ["fxd"] = "flex-direction", ["fxw"] = "flex-wrap", ["fxg"] = "flex-grow", ["fxs"] = "flex-shrink",
        ["jc"] = "justify-content", ["ai"] = "align-items", ["ac"] = "align-content", ["as"] = "align-self", ["or"] = "order", ["g"] = "gap",
        ["gtc"] = "grid-template-columns", ["gtr"] = "grid-template-rows",
        ["tr"] = "transition", ["trf"] = "transform", ["an"] = "animation", ["us"] = "user-select", ["pe"] = "pointer-events", ["lis"] = "list-style",
        ["res"] = "resize", ["fil"] = "filter", ["ap"] = "appearance", ["obf"] = "object-fit",
    };

    private static readonly HashSet<string> Unitless = new(StringComparer.Ordinal)
    {
        "z-index", "opacity", "line-height", "font-weight", "flex", "flex-grow", "flex-shrink", "order", "zoom", "orphans", "widows", "column-count", "tab-size", "fill-opacity",
    };

    private static readonly Dictionary<string, string[]> Keywords = new(StringComparer.Ordinal)
    {
        ["display"] = ["n", "none", "b", "block", "i", "inline", "ib", "inline-block", "f", "flex", "if", "inline-flex", "g", "grid", "t", "table", "c", "contents"],
        ["position"] = ["a", "absolute", "r", "relative", "f", "fixed", "s", "static", "st", "sticky"],
        ["float"] = ["l", "left", "r", "right", "n", "none"],
        ["clear"] = ["l", "left", "r", "right", "b", "both", "n", "none"],
        ["overflow"] = ["h", "hidden", "v", "visible", "s", "scroll", "a", "auto"],
        ["overflow-x"] = ["h", "hidden", "v", "visible", "s", "scroll", "a", "auto"],
        ["overflow-y"] = ["h", "hidden", "v", "visible", "s", "scroll", "a", "auto"],
        ["visibility"] = ["v", "visible", "h", "hidden", "c", "collapse"],
        ["text-align"] = ["l", "left", "c", "center", "r", "right", "j", "justify"],
        ["text-decoration"] = ["n", "none", "u", "underline", "l", "line-through", "o", "overline"],
        ["text-transform"] = ["n", "none", "u", "uppercase", "l", "lowercase", "c", "capitalize"],
        ["font-weight"] = ["n", "normal", "b", "bold", "l", "lighter"],
        ["font-style"] = ["n", "normal", "i", "italic", "o", "oblique"],
        ["white-space"] = ["n", "normal", "nw", "nowrap", "p", "pre", "pw", "pre-wrap", "pl", "pre-line"],
        ["vertical-align"] = ["t", "top", "m", "middle", "b", "bottom", "bl", "baseline"],
        ["cursor"] = ["p", "pointer", "d", "default", "t", "text", "m", "move", "h", "help"],
        ["box-sizing"] = ["bb", "border-box", "cb", "content-box"],
        ["flex-direction"] = ["r", "row", "rr", "row-reverse", "c", "column", "cr", "column-reverse"],
        ["flex-wrap"] = ["n", "nowrap", "w", "wrap", "wr", "wrap-reverse"],
        ["justify-content"] = ["s", "flex-start", "e", "flex-end", "c", "center", "sb", "space-between", "sa", "space-around", "se", "space-evenly"],
        ["align-items"] = ["s", "flex-start", "e", "flex-end", "c", "center", "st", "stretch", "b", "baseline"],
        ["align-content"] = ["s", "flex-start", "e", "flex-end", "c", "center", "st", "stretch", "sb", "space-between", "sa", "space-around"],
        ["align-self"] = ["a", "auto", "s", "flex-start", "e", "flex-end", "c", "center", "st", "stretch", "b", "baseline"],
        ["background-repeat"] = ["n", "no-repeat", "r", "repeat", "x", "repeat-x", "y", "repeat-y"],
        ["background-size"] = ["c", "cover", "ct", "contain", "a", "auto"],
        ["border-style"] = ["s", "solid", "d", "dashed", "dt", "dotted", "n", "none"],
        ["user-select"] = ["n", "none", "a", "auto", "t", "text", "al", "all"],
        ["pointer-events"] = ["n", "none", "a", "auto"],
        ["resize"] = ["n", "none", "b", "both", "h", "horizontal", "v", "vertical"],
        ["border"] = ["n", "none"],
        ["outline"] = ["n", "none"],
    };

    /// <summary>Declarations written as property letters plus keyword letters in one word, such as <c>dn</c>, <c>posa</c> or <c>tac</c>.</summary>
    private static readonly Dictionary<string, (string Property, string Value)> Whole = BuildWhole();

    private static Dictionary<string, (string, string)> BuildWhole()
    {
        var whole = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var (key, property) in Properties)
        {
            if (!Keywords.TryGetValue(property, out var pairs) || property is "border" or "outline") continue;
            for (int i = 0; i < pairs.Length; i += 2)
                if (!Properties.ContainsKey(key + pairs[i])) whole.TryAdd(key + pairs[i], (property, pairs[i + 1]));
        }
        return whole;
    }

    private static string KeywordOf(string property, string letters) =>
        Keywords.TryGetValue(property, out var pairs) ? Lookup(pairs, letters) ?? letters : letters;

    private static string? Lookup(string[] pairs, string key)
    {
        for (int i = 0; i < pairs.Length; i += 2)
            if (pairs[i] == key) return pairs[i + 1];
        return null;
    }

    /// <summary>The declarations an abbreviation stands for, or null when the text is not a CSS abbreviation.</summary>
    public static Expansion? Expand(string abbreviation, ExpandOptions options)
    {
        if (abbreviation.Length == 0) return null;
        var sb = new StringBuilder();
        var carets = new List<int>();
        foreach (var part in abbreviation.Split('+'))
        {
            if (part.Length == 0) return null;
            var declaration = Declaration(part);
            if (declaration is null) return null;
            if (sb.Length > 0) sb.Append('\n').Append(options.BaseIndent);
            var (property, value) = declaration.Value;
            sb.Append(property).Append(": ");
            if (value.Length == 0) carets.Add(sb.Length);
            sb.Append(value).Append(';');
            if (sb.Length > options.MaxChars) throw new ExpansionTooLargeException();
        }
        if (carets.Count == 0) carets.Add(sb.Length);
        return new Expansion(sb.ToString(), carets.Count <= options.MaxCarets ? carets : carets.GetRange(0, options.MaxCarets));
    }

    private static (string Property, string Value)? Declaration(string part)
    {
        bool important = part.EndsWith('!');
        if (important) part = part[..^1];
        var found = Parse(part);
        if (found is null) return null;
        var (property, value) = found.Value;
        if (important) value = (value + " !important").TrimStart();
        return (property, value);
    }

    private static (string Property, string Value)? Parse(string part)
    {
        if (Properties.TryGetValue(part, out var exact)) return (exact, "");
        if (Whole.TryGetValue(part, out var whole)) return whole;
        int colon = part.IndexOf(':');
        if (colon > 0)
        {
            string left = part[..colon], right = part[(colon + 1)..];
            string? property = Properties.TryGetValue(left, out var p) ? p : Properties.ContainsValue(left) ? left : null;
            if (property is null || right.Length == 0) return null;
            return right.All(c => char.IsAsciiLetter(c) || c == '-') ? (property, KeywordOf(property, right)) : null;
        }
        // the longest property name that starts the text and is followed by a value
        for (int length = part.Length - 1; length >= 1; length--)
        {
            string head = part[..length];
            string? property = Properties.TryGetValue(head, out var p) ? p : Properties.ContainsValue(head) ? head : null;
            if (property is null || !(char.IsAsciiDigit(part[length]) || part[length] is '-' or '.' or '#')) continue;
            var value = Values(property, part[length..]);
            if (value is not null) return (property, value);
        }
        return null;
    }

    private static string? Values(string property, string s)
    {
        var values = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '#')
            {
                int start = ++i;
                while (i < s.Length && char.IsAsciiHexDigit(s[i])) i++;
                string hex = s[start..i];
                values.Add("#" + hex.Length switch { 1 => new string(hex[0], 6), 2 => hex + hex + hex, 3 or 4 or 6 or 8 => hex, _ => "" });
                if (hex.Length is 0 or 5 or 7 or > 8) return null;
            }
            else
            {
                int start = i;
                if (s[i] == '-') i++;
                int digits = i;
                while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                bool fraction = false;
                if (i < s.Length && s[i] == '.')
                {
                    fraction = true;
                    i++;
                    while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                }
                if (i == digits || (fraction && i == digits + 1 && digits == start)) return null;
                string number = s[start..i];
                int unitStart = i;
                while (i < s.Length && (char.IsAsciiLetter(s[i]) || s[i] == '%')) i++;
                values.Add(WithUnit(property, number, s[unitStart..i], fraction));
            }
            if (i < s.Length)
            {
                if (s[i] != '-') return null;
                i++;
                if (i >= s.Length) return null;
            }
        }
        return values.Count == 0 ? null : string.Join(' ', values);
    }

    private static string WithUnit(string property, string number, string unit, bool fraction)
    {
        if (unit.Length > 0)
            return number + unit switch { "p" => "%", "e" => "em", "x" => "ex", "r" => "rem", _ => unit };
        if (Unitless.Contains(property)) return number;
        if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d == 0) return "0";
        return number + (fraction ? "em" : "px");
    }

    /// <summary>
    /// Declarations only make sense inside a block, so this tells whether the end of <paramref name="before"/> (the text in front of the abbreviation) is inside braces.
    /// Comments and strings are skipped. shortcut: only the text given is read, so a block that opened before it is not seen; the editor gives a generous window.
    /// </summary>
    public static bool InsideBlock(string before)
    {
        int depth = 0;
        for (int i = 0; i < before.Length; i++)
        {
            char c = before[i];
            if (c == '/' && i + 1 < before.Length && before[i + 1] == '*')
            {
                int end = before.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) return false;
                i = end + 1;
            }
            else if (c is '"' or '\'')
            {
                int j = i + 1;
                while (j < before.Length && before[j] != c && before[j] != '\n') j += before[j] == '\\' ? 2 : 1;
                i = j;
            }
            else if (c == '{') depth++;
            else if (c == '}' && depth > 0) depth--;
        }
        return depth > 0;
    }
}

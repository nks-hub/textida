using System.Globalization;
using System.Text;
using System.Xml;

namespace NksHub.Textida.Workbench.Icons;

/// <summary>
/// Reads the icon of a plugin: a small SVG with shapes only. This is not an SVG renderer; it accepts the few elements and attributes that draw a one-colour icon and refuses everything else, so a file
/// that carries a script, a link to another file or a style sheet never gets as far as being drawn. The shapes become paths in the path mini-language, which the program draws with the colours of the theme.
/// The built-in icons are converted when the program is built (see <c>Icons</c> in the app project); only a plugin's icon is read at run time, and this reader uses neither reflection nor an SVG library, so it works under NativeAOT.
/// </summary>
public static class SvgIcon
{
    /// <summary>Most shapes an icon may have.</summary>
    public const int MaxShapes = 64;

    private static readonly HashSet<string> Elements = new(StringComparer.Ordinal) { "svg", "g", "path", "circle", "ellipse", "rect", "line", "polyline", "polygon", "title", "desc" };

    private static readonly HashSet<string> Attributes = new(StringComparer.Ordinal)
    {
        "xmlns", "version", "viewBox", "width", "height", "d", "cx", "cy", "r", "rx", "ry", "x", "y", "x1", "y1", "x2", "y2", "points", "fill", "stroke", "stroke-width", "stroke-linecap",
        "stroke-linejoin", "fill-rule", "clip-rule", "opacity", "class", "id",
    };

    /// <summary>Reads the icon. On failure <paramref name="error"/> says what is wrong and <paramref name="shape"/> is null.</summary>
    public static bool TryParse(string svg, out IconShape? shape, out string error)
    {
        shape = null;
        error = "";
        if (string.IsNullOrWhiteSpace(svg)) return Fail("the file is empty", out error);
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreProcessingInstructions = true, MaxCharactersInDocument = 1_000_000 };
            using var reader = XmlReader.Create(new StringReader(svg), settings);
            int size = 16;
            var paths = new List<IconPath>();
            bool rootSeen = false, rootFill = false, rootStroke = true;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                string name = reader.LocalName;
                if (!rootSeen && name != "svg") return Fail("the root element must be <svg>", out error);
                if (!Elements.Contains(name)) return Fail($"the element <{name}> is not allowed in an icon", out error);
                if (!rootSeen)
                {
                    rootSeen = true;
                    size = ReadSize(reader, out var sizeError);
                    if (size == 0) return Fail(sizeError, out error);
                }
                else if (name == "svg") return Fail("<svg> may not be nested", out error);

                var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
                if (reader.MoveToFirstAttribute())
                {
                    do
                    {
                        if (reader.Prefix == "xmlns") continue;
                        string attribute = reader.Prefix.Length > 0 ? reader.Prefix + ":" + reader.LocalName : reader.LocalName;
                        if (!Attributes.Contains(attribute)) return Fail($"the attribute {attribute} is not allowed in an icon", out error);
                        if (reader.Value.Length > 8192) return Fail($"the attribute {attribute} is too long", out error);
                        if (attribute is "fill" or "stroke" && reader.Value is not ("none" or "currentColor"))
                            return Fail($"{attribute}=\"{reader.Value}\" is not allowed: an icon uses \"none\" or \"currentColor\" and takes its colours from the theme", out error);
                        attributes[attribute] = reader.Value;
                    }
                    while (reader.MoveToNextAttribute());
                    reader.MoveToElement();
                }
                if (name == "svg")
                {
                    // like SVG itself, the shapes inherit the fill and the stroke of the root; a shape that says nothing is drawn as a line unless the root fills
                    rootFill = Get(attributes, "fill") == "currentColor";
                    rootStroke = attributes.TryGetValue("stroke", out var rs) ? rs == "currentColor" : !rootFill;
                    continue;
                }
                if (name is "g" or "title" or "desc") continue;
                if (paths.Count >= MaxShapes) return Fail($"more than {MaxShapes} shapes", out error);
                var data = ShapeToPath(name, attributes, out var shapeError);
                if (data is null) return Fail($"<{name}>: {shapeError}", out error);
                paths.Add(new IconPath(data, PaintOf(attributes, rootFill, rootStroke)));
            }
            if (!rootSeen) return Fail("the file is empty", out error);
            if (paths.Count == 0) return Fail("the icon draws nothing", out error);
            shape = new IconShape(size, paths);
            return true;
        }
        catch (XmlException e)
        {
            return Fail("not a valid SVG file: " + e.Message, out error);
        }
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    private static IconPaint PaintOf(Dictionary<string, string> a, bool rootFill, bool rootStroke)
    {
        bool fill = a.TryGetValue("fill", out var f) ? f == "currentColor" : rootFill;
        bool stroke = a.TryGetValue("stroke", out var s) ? s == "currentColor" : a.ContainsKey("fill") ? !fill && rootStroke : rootStroke;
        bool accent = Get(a, "class") == "accent";
        if (fill && !stroke) return accent ? IconPaint.AccentFill : IconPaint.Fill;
        return accent ? IconPaint.AccentLine : IconPaint.Line;
    }

    private static string? Get(Dictionary<string, string> a, string name) => a.TryGetValue(name, out var value) ? value : null;

    private static int ReadSize(XmlReader reader, out string error)
    {
        error = "";
        string? box = reader.GetAttribute("viewBox");
        if (box is null) { error = "the <svg> element needs a viewBox"; return 0; }
        var parts = box.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[0] != "0" || parts[1] != "0" || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int w) || parts[2] != parts[3] || w is < 8 or > 64)
        {
            error = "the viewBox must be square, start at 0 0 and be between 8 and 64 units wide";
            return 0;
        }
        return w;
    }

    private static string? ShapeToPath(string element, Dictionary<string, string> a, out string error)
    {
        error = "";
        switch (element)
        {
            case "path":
                if (!a.TryGetValue("d", out var d) || !IsPathData(d)) { error = "the d attribute is missing or holds something that is not path data"; return null; }
                return d;
            case "circle":
                if (!Number(a, "cx", out var cx) || !Number(a, "cy", out var cy) || !Number(a, "r", out var r) || r <= 0) { error = "needs cx, cy and a positive r"; return null; }
                return Ellipse(cx, cy, r, r);
            case "ellipse":
                if (!Number(a, "cx", out cx) || !Number(a, "cy", out cy) || !Number(a, "rx", out var rx) || !Number(a, "ry", out var ry) || rx <= 0 || ry <= 0) { error = "needs cx, cy, rx and ry"; return null; }
                return Ellipse(cx, cy, rx, ry);
            case "rect":
                if (!Number(a, "x", out var x) | !Number(a, "y", out var y) | !Number(a, "width", out var w) | !Number(a, "height", out var h) || w <= 0 || h <= 0) { error = "needs a positive width and height"; return null; }
                double corner = 0;
                if (a.ContainsKey("rx") && (!Number(a, "rx", out corner) || corner < 0)) { error = "rx must be a number that is not negative"; return null; }
                corner = Math.Min(corner, Math.Min(w, h) / 2);
                if (corner == 0) return $"M{F(x)},{F(y)} L{F(x + w)},{F(y)} L{F(x + w)},{F(y + h)} L{F(x)},{F(y + h)} Z";
                return $"M{F(x + corner)},{F(y)} H{F(x + w - corner)} A{F(corner)},{F(corner)} 0 0 1 {F(x + w)},{F(y + corner)} V{F(y + h - corner)} A{F(corner)},{F(corner)} 0 0 1 {F(x + w - corner)},{F(y + h)} "
                    + $"H{F(x + corner)} A{F(corner)},{F(corner)} 0 0 1 {F(x)},{F(y + h - corner)} V{F(y + corner)} A{F(corner)},{F(corner)} 0 0 1 {F(x + corner)},{F(y)} Z";
            case "line":
                if (!Number(a, "x1", out var x1) || !Number(a, "y1", out var y1) || !Number(a, "x2", out var x2) || !Number(a, "y2", out var y2)) { error = "needs x1, y1, x2 and y2"; return null; }
                return $"M{F(x1)},{F(y1)} L{F(x2)},{F(y2)}";
            case "polyline":
            case "polygon":
                if (!a.TryGetValue("points", out var points)) { error = "needs points"; return null; }
                var numbers = points.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
                if (numbers.Length < 4 || numbers.Length % 2 != 0 || numbers.Any(n => !double.TryParse(n, NumberStyles.Float, CultureInfo.InvariantCulture, out _))) { error = "points must be pairs of numbers"; return null; }
                var sb = new StringBuilder();
                for (int i = 0; i < numbers.Length; i += 2) sb.Append(i == 0 ? "M" : " L").Append(numbers[i]).Append(',').Append(numbers[i + 1]);
                return element == "polygon" ? sb.Append(" Z").ToString() : sb.ToString();
            default:
                error = "not a shape";
                return null;
        }

        static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        static string Ellipse(double cx, double cy, double rx, double ry) =>
            $"M{F(cx - rx)},{F(cy)} A{F(rx)},{F(ry)} 0 1 0 {F(cx + rx)},{F(cy)} A{F(rx)},{F(ry)} 0 1 0 {F(cx - rx)},{F(cy)} Z";
    }

    /// <summary>A number attribute; x and y may be missing (they are 0), every other one must be there.</summary>
    private static bool Number(Dictionary<string, string> a, string name, out double value)
    {
        value = 0;
        if (!a.TryGetValue(name, out var text)) return name is "x" or "y";
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) < 1000;
    }

    /// <summary>
    /// Path data is well formed: it starts with a move, every command comes with the numbers it needs (a repeated command with a whole group of them more), and nothing else is in it. That is
    /// what the drawing code can parse, so a malformed path is refused when the plugin is loaded and never reaches it.
    /// </summary>
    public static bool IsPathData(string data)
    {
        if (data.Length is 0 or > 8192) return false;
        char command = '\0';
        int needed = 0, seen = 0, i = 0;
        while (true)
        {
            while (i < data.Length && data[i] is ' ' or ',' or '\n' or '\r' or '\t') i++;
            if (i >= data.Length) break;
            char c = data[i];
            if (IsAsciiLetter(c))
            {
                int arguments = ArgumentCount(c);
                if (arguments < 0 || (command == '\0' && c is not ('M' or 'm')) || !IsComplete(command, needed, seen)) return false;
                (command, needed, seen) = (c, arguments, 0);
                i++;
                continue;
            }

            if (needed == 0) return false;
            if (command is 'A' or 'a' && seen % 7 is 3 or 4)
            {
                if (c is not ('0' or '1')) return false;
                i++;
            }
            else if (!SkipNumber(data, ref i)) return false;
            seen++;
        }

        return command != '\0' && IsComplete(command, needed, seen);
    }

    private static bool IsAsciiLetter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';

    private static bool IsComplete(char command, int needed, int seen) => command == '\0' || (needed == 0 ? seen == 0 : seen >= needed && seen % needed == 0);

    private static int ArgumentCount(char command) => char.ToUpperInvariant(command) switch
    {
        'Z' => 0,
        'H' or 'V' => 1,
        'M' or 'L' or 'T' => 2,
        'S' or 'Q' => 4,
        'C' => 6,
        'A' => 7,
        _ => -1,
    };

    private static bool SkipNumber(string data, ref int i)
    {
        int start = i;
        if (i < data.Length && data[i] is '+' or '-') i++;
        int digits = 0;
        while (i < data.Length && data[i] is >= '0' and <= '9') { i++; digits++; }
        if (i < data.Length && data[i] == '.')
        {
            i++;
            while (i < data.Length && data[i] is >= '0' and <= '9') { i++; digits++; }
        }

        if (digits == 0) return false;
        if (i < data.Length && data[i] is 'e' or 'E')
        {
            int exponent = i + 1;
            if (exponent < data.Length && data[exponent] is '+' or '-') exponent++;
            int exponentDigits = 0;
            while (exponent < data.Length && data[exponent] is >= '0' and <= '9') { exponent++; exponentDigits++; }
            if (exponentDigits == 0) return false;
            i = exponent;
        }

        return double.TryParse(data.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

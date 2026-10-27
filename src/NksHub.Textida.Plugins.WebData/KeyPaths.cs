using System.Text;

namespace NksHub.Textida.Plugins.WebData;

/// <summary>
/// One key of a YAML or JSON text. <see cref="Path"/> is the dotted path (<c>cs.app.title</c>, <c>items[0].name</c>); <see cref="KeyStart"/> and <see cref="KeyEnd"/>
/// are the characters of the key as written (quotes included), the place a jump goes to; <see cref="ExtentEnd"/> is the end of its value, nested keys included,
/// so the key under a caret is the innermost one whose extent holds it.
/// </summary>
public sealed class KeyNode(string path, KeyNode? parent, int keyStart, int keyEnd)
{
    public string Path { get; } = path;
    public KeyNode? Parent { get; } = parent;
    public int KeyStart { get; } = keyStart;
    public int KeyEnd { get; set; } = keyEnd;
    public int ExtentEnd { get; set; } = keyEnd;
    public bool HasChildren { get; set; }
}

/// <summary>The keys found, in order of position, and where the text stopped making sense (null when it did not).</summary>
public sealed record KeyParse(List<KeyNode> Nodes, int? ErrorAt)
{
    /// <summary>The innermost key whose extent holds <paramref name="offset"/>; null between top-level keys.</summary>
    public KeyNode? At(int offset)
    {
        KeyNode? found = null;
        foreach (var node in Nodes)
        {
            if (node.KeyStart > offset) break;
            if (offset <= node.ExtentEnd) found = node;
        }
        return found;
    }

    /// <summary>Paths of the keys that hold a value and no further keys: what two translation files have to agree on.</summary>
    public HashSet<string> Leaves() => [.. Nodes.Where(n => !n.HasChildren).Select(n => n.Path)];
}

/// <summary>Builds key paths. A key with a dot, a bracket, a quote or nothing in it is written <c>["…"]</c>, so a path is never ambiguous.</summary>
public static class KeyPathText
{
    public static string Join(string parent, string key)
    {
        if (key.Length > 0 && key.IndexOfAny(['.', '[', ']', '"']) < 0) return parent.Length == 0 ? key : parent + "." + key;
        var quoted = new StringBuilder(key.Length + 6).Append("[\"");
        foreach (char c in key) quoted.Append(c is '"' or '\\' ? "\\" : "").Append(c);
        return parent + quoted.Append("\"]");
    }

    public static string Item(string parent, int index) => parent + "[" + index + "]";
}

/// <summary>
/// Keys of a YAML text in block style: nested mappings, sequences (also at the indentation of the key that owns them), quoted keys, block scalars and
/// continuation lines of scalars. A flow value (<c>{…}</c>, <c>[…]</c>) is one value; its inner keys are not listed. Anchors, tags and comments are skipped.
/// A document marker (<c>---</c>) starts the paths again.
/// </summary>
public static class YamlKeys
{
    private sealed class Frame
    {
        public int Indent = -1;
        public bool? IsSeq;
        public string Path = "";
        public int Count;
        public int KeyIndent = -1;
        public KeyNode? Node;
    }

    private static int EndOfLine(string text, int from)
    {
        int i = text.AsSpan(from).IndexOfAny('\r', '\n');
        return i < 0 ? text.Length : from + i;
    }

    private static int NextLine(string text, int lineEnd) =>
        lineEnd >= text.Length ? text.Length : text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n' ? lineEnd + 2 : lineEnd + 1;

    private static int TrimEnd(string text, int start, int end)
    {
        while (end > start && text[end - 1] is ' ' or '\t') end--;
        return end;
    }

    public static KeyParse Parse(string text)
    {
        var nodes = new List<KeyNode>();
        var stack = new List<Frame> { new() };
        int skipIndent = -1;
        KeyNode? lastLeaf = null;
        for (int pos = 0; pos < text.Length;)
        {
            int lineStart = pos, lineEnd = EndOfLine(text, pos);
            pos = NextLine(text, lineEnd);
            int p = lineStart;
            while (p < lineEnd && text[p] == ' ') p++;
            if (p >= lineEnd || text[p] == '#') continue;
            int indent = p - lineStart;
            var line = text.AsSpan(p, TrimEnd(text, p, lineEnd) - p);
            if (indent == 0 && (line is "---" or "..." || line.StartsWith("--- ")))
            {
                stack = [new Frame()];
                skipIndent = -1;
                lastLeaf = null;
                continue;
            }
            if (skipIndent >= 0)
            {
                if (indent > skipIndent)
                {
                    if (lastLeaf is not null) lastLeaf.ExtentEnd = Math.Max(lastLeaf.ExtentEnd, TrimEnd(text, p, lineEnd));
                    continue;
                }
                skipIndent = -1;
            }
            Entry(text, p, indent, lineEnd, stack, nodes, ref skipIndent, ref lastLeaf);
        }
        for (int i = nodes.Count - 1; i >= 0; i--)
            if (nodes[i].Parent is { } parent) parent.ExtentEnd = Math.Max(parent.ExtentEnd, nodes[i].ExtentEnd);
        return new KeyParse(nodes, null);
    }

    /// <summary>The frame an entry at <paramref name="indent"/> belongs to; frames it ends are dropped.</summary>
    private static Frame Resolve(List<Frame> stack, int indent, bool dash)
    {
        while (stack.Count > 1)
        {
            var top = stack[^1];
            if (top.IsSeq is null)
            {
                if (indent > top.KeyIndent || (dash && indent == top.KeyIndent)) break;
                stack.RemoveAt(stack.Count - 1);
            }
            else if (indent < top.Indent || (indent == top.Indent && top.IsSeq != dash)) stack.RemoveAt(stack.Count - 1);
            else break;
        }
        var frame = stack[^1];
        if (frame.IsSeq is null)
        {
            frame.IsSeq = dash;
            frame.Indent = indent;
        }
        return frame;
    }

    private static KeyNode Add(List<KeyNode> nodes, Frame frame, string path, int keyStart, int keyEnd)
    {
        var node = new KeyNode(path, frame.Node, keyStart, keyEnd);
        if (frame.Node is not null) frame.Node.HasChildren = true;
        nodes.Add(node);
        return node;
    }

    private static void Entry(string text, int p, int indent, int lineEnd, List<Frame> stack, List<KeyNode> nodes, ref int skipIndent, ref KeyNode? lastLeaf)
    {
        int col = p, colIndent = indent, dashIndent = indent;
        KeyNode? inline = null;
        while (true)
        {
            bool dash = text[col] == '-' && (col + 1 == lineEnd || text[col + 1] == ' ');
            if (dash)
            {
                var frame = inline is null ? Resolve(stack, colIndent, true) : Push(stack, new Frame { Indent = colIndent, IsSeq = true, Path = inline.Path, Node = inline });
                var item = Add(nodes, frame, KeyPathText.Item(frame.Path, frame.Count++), col, col + 1);
                int after = col + 1;
                while (after < lineEnd && text[after] == ' ') after++;
                if (after >= lineEnd || text[after] == '#')
                {
                    stack.Add(new Frame { Path = item.Path, KeyIndent = colIndent, Node = item });
                    return;
                }
                dashIndent = colIndent;
                colIndent += after - col;
                col = after;
                inline = item;
                continue;
            }
            if (TryKey(text, col, lineEnd, out string key, out int keyEnd, out int valueStart))
            {
                var frame = inline is null ? Resolve(stack, colIndent, false) : Push(stack, new Frame { Indent = colIndent, IsSeq = false, Path = inline.Path, Node = inline });
                var node = Add(nodes, frame, KeyPathText.Join(frame.Path, key), col, keyEnd);
                int valueEnd = ValueEnd(text, valueStart, lineEnd);
                if (valueEnd <= valueStart) stack.Add(new Frame { Path = node.Path, KeyIndent = colIndent, Node = node });
                else
                {
                    node.ExtentEnd = valueEnd;
                    lastLeaf = node;
                    skipIndent = colIndent;
                }
                return;
            }
            if (inline is not null)
            {
                inline.ExtentEnd = TrimEnd(text, col, lineEnd);
                lastLeaf = inline;
                skipIndent = dashIndent;
            }
            return;
        }
    }

    private static Frame Push(List<Frame> stack, Frame frame)
    {
        stack.Add(frame);
        return frame;
    }

    /// <summary>A key and its colon at <paramref name="col"/>: plain, or in single or double quotes. The colon ends the key only before a space or the end of the line.</summary>
    private static bool TryKey(string text, int col, int lineEnd, out string key, out int keyEnd, out int valueStart)
    {
        key = "";
        keyEnd = valueStart = col;
        char first = text[col];
        int colon;
        if (first is '"' or '\'')
        {
            int close = col + 1;
            var sb = new StringBuilder();
            for (; close < lineEnd; close++)
            {
                char c = text[close];
                if (c == first)
                {
                    if (first == '\'' && close + 1 < lineEnd && text[close + 1] == '\'') { sb.Append('\''); close++; continue; }
                    break;
                }
                if (c == '\\' && first == '"' && close + 1 < lineEnd) { close++; sb.Append(text[close]); continue; }
                sb.Append(c);
            }
            if (close >= lineEnd) return false;
            keyEnd = close + 1;
            colon = keyEnd;
            while (colon < lineEnd && text[colon] == ' ') colon++;
            if (colon >= lineEnd || text[colon] != ':' || (colon + 1 < lineEnd && text[colon + 1] != ' ')) return false;
            key = sb.ToString();
        }
        else
        {
            if (first is '{' or '[' or '&' or '*' or '!' or '|' or '>' or '%' or '@' or '`' or '#' or '?' or ',' or ']' or '}') return false;
            colon = -1;
            for (int i = col; i < lineEnd; i++)
            {
                if (text[i] == '#' && i > col && text[i - 1] == ' ') return false;
                if (text[i] == ':' && (i + 1 == lineEnd || text[i + 1] == ' ')) { colon = i; break; }
            }
            if (colon <= col) return false;
            keyEnd = TrimEnd(text, col, colon);
            key = text[col..keyEnd];
            if (key.Length == 0) return false;
        }
        valueStart = colon + 1;
        while (valueStart < lineEnd && text[valueStart] == ' ') valueStart++;
        return true;
    }

    /// <summary>End of the value that starts at <paramref name="start"/> on the line, without a comment; equal to <paramref name="start"/> when there is none (also for an anchor or a tag alone).</summary>
    private static int ValueEnd(string text, int start, int lineEnd)
    {
        int p = start;
        while (p < lineEnd && text[p] is '&' or '!')
        {
            while (p < lineEnd && text[p] != ' ') p++;
            while (p < lineEnd && text[p] == ' ') p++;
        }
        if (p >= lineEnd || text[p] == '#') return start;
        int end = p;
        if (text[p] is '"' or '\'')
        {
            char q = text[p];
            int i = p + 1;
            while (i < lineEnd && text[i] != q) i += q == '"' && text[i] == '\\' ? 2 : 1;
            if (i < lineEnd) return i + 1;
        }
        for (int i = p; i < lineEnd; i++)
        {
            if (text[i] == '#' && i > p && text[i - 1] == ' ') break;
            if (text[i] != ' ') end = i + 1;
        }
        return end;
    }
}

/// <summary>Keys of a JSON text (comments and trailing commas are tolerated). On a syntax error the keys read up to it are kept and <see cref="KeyParse.ErrorAt"/> says where.</summary>
public static class JsonKeys
{
    private const int MaxDepth = 200;

    private sealed class Reader(string text)
    {
        public readonly string Text = text;
        public int Pos;
        public readonly List<KeyNode> Nodes = [];
        public int? Error;
    }

    private sealed class Stop : Exception;

    public static KeyParse Parse(string text)
    {
        var r = new Reader(text);
        try
        {
            Skip(r);
            if (r.Pos < text.Length) Value(r, "", null, 0);
            Skip(r);
            if (r.Pos < text.Length) throw new Stop();
        }
        catch (Stop) { r.Error ??= r.Pos; }
        return new KeyParse(r.Nodes, r.Error);
    }

    private static void Skip(Reader r)
    {
        var t = r.Text;
        while (r.Pos < t.Length)
        {
            char c = t[r.Pos];
            if (c is ' ' or '\t' or '\r' or '\n' or '﻿') r.Pos++;
            else if (c == '/' && r.Pos + 1 < t.Length && t[r.Pos + 1] == '/')
            {
                while (r.Pos < t.Length && t[r.Pos] is not ('\r' or '\n')) r.Pos++;
            }
            else if (c == '/' && r.Pos + 1 < t.Length && t[r.Pos + 1] == '*')
            {
                int end = t.IndexOf("*/", r.Pos + 2, StringComparison.Ordinal);
                if (end < 0) throw new Stop();
                r.Pos = end + 2;
            }
            else break;
        }
    }

    /// <summary>Reads the string that starts at <see cref="Reader.Pos"/> and returns its text.</summary>
    private static string Str(Reader r)
    {
        var t = r.Text;
        var sb = new StringBuilder();
        r.Pos++;
        while (r.Pos < t.Length)
        {
            char c = t[r.Pos++];
            if (c == '"') return sb.ToString();
            if (c is '\r' or '\n') break;
            if (c != '\\') { sb.Append(c); continue; }
            if (r.Pos >= t.Length) break;
            char e = t[r.Pos++];
            switch (e)
            {
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (r.Pos + 4 > t.Length || !ushort.TryParse(t.AsSpan(r.Pos, 4), System.Globalization.NumberStyles.AllowHexSpecifier, null, out var unit)) throw new Stop();
                    sb.Append((char)unit);
                    r.Pos += 4;
                    break;
                default: sb.Append(e); break;
            }
        }
        throw new Stop();
    }

    private static void Value(Reader r, string path, KeyNode? parent, int depth)
    {
        if (depth > MaxDepth) throw new Stop();
        var t = r.Text;
        if (r.Pos >= t.Length) throw new Stop();
        switch (t[r.Pos])
        {
            case '{': Object(r, path, parent, depth); break;
            case '[': Array(r, path, parent, depth); break;
            case '"': Str(r); break;
            default:
                int start = r.Pos;
                while (r.Pos < t.Length && t[r.Pos] is not (',' or '}' or ']' or ' ' or '\t' or '\r' or '\n' or '/')) r.Pos++;
                if (r.Pos == start) throw new Stop();
                break;
        }
    }

    private static void Object(Reader r, string path, KeyNode? parent, int depth)
    {
        var t = r.Text;
        r.Pos++;
        while (true)
        {
            Skip(r);
            if (r.Pos >= t.Length) throw new Stop();
            if (t[r.Pos] == '}') { r.Pos++; return; }
            if (t[r.Pos] != '"') throw new Stop();
            int keyStart = r.Pos;
            string key = Str(r);
            var node = new KeyNode(KeyPathText.Join(path, key), parent, keyStart, r.Pos);
            r.Nodes.Add(node);
            if (parent is not null) parent.HasChildren = true;
            Skip(r);
            if (r.Pos >= t.Length || t[r.Pos] != ':') throw new Stop();
            r.Pos++;
            Skip(r);
            try { Value(r, node.Path, node, depth + 1); }
            finally { node.ExtentEnd = Math.Max(node.ExtentEnd, r.Pos); }
            Skip(r);
            if (r.Pos < t.Length && t[r.Pos] == ',') r.Pos++;
            else if (r.Pos < t.Length && t[r.Pos] != '}') throw new Stop();
        }
    }

    private static void Array(Reader r, string path, KeyNode? parent, int depth)
    {
        var t = r.Text;
        r.Pos++;
        for (int index = 0; ;)
        {
            Skip(r);
            if (r.Pos >= t.Length) throw new Stop();
            if (t[r.Pos] == ']') { r.Pos++; return; }
            int start = r.Pos;
            var node = new KeyNode(KeyPathText.Item(path, index++), parent, start, start + 1);
            r.Nodes.Add(node);
            if (parent is not null) parent.HasChildren = true;
            try { Value(r, node.Path, node, depth + 1); }
            finally { node.ExtentEnd = Math.Max(node.ExtentEnd, r.Pos); }
            if (t[start] is not ('{' or '[')) node.KeyEnd = r.Pos; // a plain item is its own key
            Skip(r);
            if (r.Pos < t.Length && t[r.Pos] == ',') r.Pos++;
            else if (r.Pos < t.Length && t[r.Pos] != ']') throw new Stop();
        }
    }
}

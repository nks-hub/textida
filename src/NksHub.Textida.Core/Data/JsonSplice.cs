using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace NksHub.Textida.Core.Data;

/// <summary>One replacement of a range of a text: the bytes outside it stay as they were.</summary>
public readonly record struct TextEdit(int Start, int End, string Replacement)
{
    public string ApplyTo(string text) => string.Concat(text.AsSpan(0, Start), Replacement, text.AsSpan(End));
}

/// <summary>How a JSON text is laid out: the indent of one level and the line break.</summary>
public sealed record JsonStyle(string Indent, string Eol)
{
    public static readonly JsonStyle Default = new("  ", "\n");

    /// <summary>The style of an existing text: the indent of its first indented line (two spaces when there is none) and the first line break it uses.</summary>
    public static JsonStyle Of(string text)
    {
        string eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\r') && !text.Contains('\n') ? "\r" : "\n";
        int at = 0;
        while (at < text.Length)
        {
            int end = text.IndexOfAny(['\r', '\n'], at);
            if (end < 0) end = text.Length;
            int i = at;
            while (i < end && text[i] is ' ' or '\t') i++;
            if (i > at && i < end) return new JsonStyle(text[at..i], eol);
            at = end < text.Length && text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? end + 2 : end + 1;
        }
        return new JsonStyle("  ", eol);
    }
}

/// <summary>
/// Edits of a JSON text made on the tree <see cref="JsonDataReader"/> read from it. Each edit touches the one member or item and the comma next to it, so a save of the result differs from the
/// text it started from in the changed place only; comments cannot occur because the text was read as strict JSON.
/// </summary>
public static class JsonSplice
{
    private static readonly JavaScriptEncoder Relaxed = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>A JSON string literal with the quotes; only the characters JSON requires are escaped.</summary>
    public static string Quote(string value) => "\"" + JsonEncodedText.Encode(value, Relaxed).ToString() + "\"";

    /// <summary>The white space at the start of the line that holds <paramref name="offset"/>.</summary>
    public static string LineIndent(string text, int offset)
    {
        int start = offset;
        while (start > 0 && text[start - 1] is not ('\n' or '\r')) start--;
        int end = start;
        while (end < text.Length && text[end] is ' ' or '\t') end++;
        return text[start..end];
    }

    private static bool Spans(string text, int from, int to) => from < to && text.AsSpan(from, to - from).IndexOfAny('\r', '\n') >= 0;

    /// <summary>The indent members of <paramref name="obj"/> have, or would have when it is empty.</summary>
    public static string MemberIndent(string text, DataNode obj, JsonStyle style) =>
        obj.Members.Count > 0 && Spans(text, obj.Start, obj.Members[0].NameStart) ? LineIndent(text, obj.Members[0].NameStart) : LineIndent(text, obj.Start) + style.Indent;

    /// <summary>The indent items of <paramref name="array"/> have, or would have when it is empty.</summary>
    public static string ItemIndent(string text, DataNode array, JsonStyle style) =>
        array.Items.Count > 0 && Spans(text, array.Start, array.Items[0].Start) ? LineIndent(text, array.Items[0].Start) : LineIndent(text, array.Start) + style.Indent;

    /// <param name="value">Writes the value; it gets the indent the member will have, for the lines after its first.</param>
    public static TextEdit InsertMember(string text, DataNode obj, string name, Func<string, string> value, JsonStyle style)
    {
        string indent = MemberIndent(text, obj, style);
        if (obj.Members.Count == 0)
            return new TextEdit(obj.Start, obj.End, "{" + style.Eol + indent + Quote(name) + ": " + value(indent) + style.Eol + LineIndent(text, obj.Start) + "}");
        bool lines = Spans(text, obj.Start, obj.Members[0].NameStart);
        int at = obj.Members[^1].Value.End;
        return new TextEdit(at, at, (lines ? "," + style.Eol + indent : ", ") + Quote(name) + ": " + value(lines ? indent : LineIndent(text, obj.Start)));
    }

    public static TextEdit RemoveMember(DataNode obj, int index)
    {
        if (obj.Members.Count == 1) return new TextEdit(obj.Start, obj.End, "{}");
        return index > 0
            ? new TextEdit(obj.Members[index - 1].Value.End, obj.Members[index].Value.End, "")
            : new TextEdit(obj.Members[0].NameStart, obj.Members[1].NameStart, "");
    }

    /// <param name="item">Writes the item; it gets the indent the item will have, for the lines after its first.</param>
    public static TextEdit InsertItem(string text, DataNode array, Func<string, string> item, JsonStyle style)
    {
        string indent = ItemIndent(text, array, style);
        if (array.Items.Count == 0)
            return new TextEdit(array.Start, array.End, "[" + style.Eol + indent + item(indent) + style.Eol + LineIndent(text, array.Start) + "]");
        bool lines = Spans(text, array.Start, array.Items[0].Start);
        int at = array.Items[^1].End;
        return new TextEdit(at, at, (lines ? "," + style.Eol + indent : ", ") + item(lines ? indent : LineIndent(text, array.Start)));
    }

    public static TextEdit RemoveItem(DataNode array, int index)
    {
        if (array.Items.Count == 1) return new TextEdit(array.Start, array.End, "[]");
        return index > 0
            ? new TextEdit(array.Items[index - 1].End, array.Items[index].End, "")
            : new TextEdit(array.Items[0].Start, array.Items[1].Start, "");
    }

    /// <summary>
    /// A value written over several lines at <paramref name="indent"/>: objects and arrays that hold other objects or arrays one member to a line, an array of plain values on a line of its own.
    /// Plain values and the keys are copied from <paramref name="text"/> as written.
    /// </summary>
    public static string Render(string text, DataNode node, JsonStyle style, string indent)
    {
        var sb = new StringBuilder();
        Write(sb, text, node, style, indent);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, string text, DataNode node, JsonStyle style, string indent)
    {
        string inner = indent + style.Indent;
        switch (node.Kind)
        {
            case DataKind.Object when node.Members.Count == 0:
                sb.Append("{}");
                break;
            case DataKind.Object:
                sb.Append('{').Append(style.Eol);
                for (int i = 0; i < node.Members.Count; i++)
                {
                    var m = node.Members[i];
                    sb.Append(inner).Append(text, m.NameStart, m.NameEnd - m.NameStart).Append(": ");
                    Write(sb, text, m.Value, style, inner);
                    sb.Append(i + 1 < node.Members.Count ? "," : "").Append(style.Eol);
                }
                sb.Append(indent).Append('}');
                break;
            case DataKind.Array when node.Items.Count == 0:
                sb.Append("[]");
                break;
            case DataKind.Array when node.Items.All(i => i.Kind is not (DataKind.Object or DataKind.Array)):
                sb.Append('[');
                for (int i = 0; i < node.Items.Count; i++) sb.Append(i > 0 ? ", " : "").Append(text, node.Items[i].Start, node.Items[i].End - node.Items[i].Start);
                sb.Append(']');
                break;
            case DataKind.Array:
                sb.Append('[').Append(style.Eol);
                for (int i = 0; i < node.Items.Count; i++)
                {
                    sb.Append(inner);
                    Write(sb, text, node.Items[i], style, inner);
                    sb.Append(i + 1 < node.Items.Count ? "," : "").Append(style.Eol);
                }
                sb.Append(indent).Append(']');
                break;
            default:
                sb.Append(text, node.Start, node.End - node.Start);
                break;
        }
    }
}

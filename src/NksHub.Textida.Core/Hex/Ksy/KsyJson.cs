using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NksHub.Textida.Core.Hex.Ksy;

/// <summary>
/// The tree a template found, written as JSON: for each node its name, type, offset and length in the file, value as text, the error when the field could not be read, and its children. Reading the children of every node
/// is bounded: past <see cref="MaxNodes"/> nodes, <see cref="MaxDepth"/> levels or <see cref="MaxMilliseconds"/> the children of the nodes that are left are not read, and each of them says <c>"truncated": true</c>.
/// </summary>
public static class KsyJson
{
    public const int MaxNodes = 50_000;
    public const int MaxDepth = 64;
    public const int MaxMilliseconds = 10_000;

    /// <summary>Reads the whole tree below <paramref name="root"/> (within the limits) and returns it as indented JSON. <paramref name="truncated"/> is true when a limit cut it.</summary>
    public static string Write(KsyNode root, out bool truncated, CancellationToken ct = default)
    {
        using var stream = new MemoryStream();
        bool cut = false;
        int count = 0;
        var clock = Stopwatch.StartNew();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Node(root, 0);
        writer.Flush();
        truncated = cut;
        return Encoding.UTF8.GetString(stream.ToArray());

        void Node(KsyNode node, int depth)
        {
            ct.ThrowIfCancellationRequested();
            count++;
            writer.WriteStartObject();
            writer.WriteString("name", node.Name);
            if (node.TypeName is { } type) writer.WriteString("type", type);
            writer.WriteNumber("start", node.Start);
            writer.WriteNumber("length", node.Length);
            if (node.Value.Length > 0) writer.WriteString("value", node.Value);
            if (node.Error is { } error)
            {
                writer.WriteStartObject("error");
                writer.WriteString("kind", error.Kind.ToString());
                writer.WriteString("subject", error.Subject);
                if (error.Line > 0) writer.WriteNumber("line", error.Line);
                writer.WriteEndObject();
            }
            if (node.HasChildren)
            {
                if (count >= MaxNodes || depth >= MaxDepth || clock.ElapsedMilliseconds > MaxMilliseconds)
                {
                    cut = true;
                    writer.WriteBoolean("truncated", true);
                }
                else
                {
                    writer.WriteStartArray("children");
                    foreach (var child in node.Children) Node(child, depth + 1);
                    writer.WriteEndArray();
                }
            }
            writer.WriteEndObject();
        }
    }
}

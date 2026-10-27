using System.Globalization;

namespace NksHub.Textida.Plugins.Json.Schema;

public enum DataKind { Object, Array, String, Number, Boolean, Null }

/// <summary>A key of an object with the place of the key in the text.</summary>
public sealed record DataMember(string Name, int NameStart, int NameEnd, DataNode Value);

/// <summary>A value read from JSON, JSONC or TOML together with its place in the text (UTF-16 offsets, <see cref="End"/> exclusive).</summary>
public sealed class DataNode(DataKind kind, int start, int end)
{
    public DataKind Kind { get; } = kind;
    public int Start { get; set; } = start;
    public int End { get; set; } = end;

    /// <summary>The string value, or the number as written.</summary>
    public string Text { get; init; } = "";

    public bool Bool { get; init; }
    public List<DataNode> Items { get; } = [];
    public List<DataMember> Members { get; } = [];

    public DataNode? Get(string name)
    {
        foreach (var m in Members) if (m.Name == name) return m.Value;
        return null;
    }

    public double Number => double.Parse(Text, NumberStyles.Float, CultureInfo.InvariantCulture);

    public bool IsInteger => Kind == DataKind.Number && Number is var n && !double.IsInfinity(n) && Math.Floor(n) == n;

    /// <summary>Structural equality, used by <c>enum</c>, <c>const</c> and <c>uniqueItems</c>.</summary>
    public bool SameValue(DataNode o)
    {
        if (Kind != o.Kind) return false;
        switch (Kind)
        {
            case DataKind.String: return Text == o.Text;
            case DataKind.Number: return Number == o.Number;
            case DataKind.Boolean: return Bool == o.Bool;
            case DataKind.Null: return true;
            case DataKind.Array:
                return Items.Count == o.Items.Count && Items.Zip(o.Items).All(p => p.First.SameValue(p.Second));
            default:
                return Members.Count == o.Members.Count && Members.All(m => o.Get(m.Name) is { } v && m.Value.SameValue(v));
        }
    }
}

/// <summary>A syntax error with the range it covers.</summary>
public sealed class DataSyntaxException(string detail, int start, int end) : Exception(detail)
{
    public int Start { get; } = start;
    public int End { get; } = end;
}

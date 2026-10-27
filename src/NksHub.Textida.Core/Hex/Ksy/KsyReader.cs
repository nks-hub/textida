using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Hex.Ksy;

/// <summary>
/// Limits that keep a hostile template or file from exhausting the editor. The numbers are placeholders: their real values are an open input decision (P8.10.3),
/// and so is a limit on time.
/// </summary>
public sealed record KsyLimits(int MaxDepth = 32, int MaxRepeat = 10_000, int MaxStringBytes = 65_536, long MaxWork = 1_000_000)
{
    public static KsyLimits Default { get; } = new();
}

/// <summary>A field of a parsed file: its place in the bytes, its value as text and, for a structure or an array, its children, which are read only when asked for.</summary>
public sealed class KsyNode
{
    private readonly Func<IReadOnlyList<KsyNode>>? _expand;
    private IReadOnlyList<KsyNode>? _children;

    internal KsyNode(string name, long start, long length, string value = "", string? type = null, Func<IReadOnlyList<KsyNode>>? expand = null, KsyError? error = null)
    {
        Name = name;
        Start = start;
        Length = length;
        Value = value;
        TypeName = type;
        _expand = expand;
        Error = error;
    }

    public string Name { get; }
    public long Start { get; }
    public long Length { get; }
    public long End => Start + Length;
    /// <summary>The value as text; empty for a structure or an array.</summary>
    public string Value { get; }
    public string? TypeName { get; }
    /// <summary>Set when the field could not be read; <see cref="Start"/> and <see cref="Length"/> then cover the bytes that were there.</summary>
    public KsyError? Error { get; }
    public bool HasChildren => _expand is not null;
    public IReadOnlyList<KsyNode> Children => _children ??= _expand?.Invoke() ?? [];
}

/// <summary>
/// Reads a file through a <see cref="KsyTemplate"/> without loading it: <see cref="Root"/> is known at once and the fields of a node are read when its
/// <see cref="KsyNode.Children"/> are asked for. A field that cannot be read (past the end, wrong magic, a limit hit) becomes a node with an
/// <see cref="KsyNode.Error"/>, and the fields behind it are not read, because their place is unknown. The reader throws only what the byte source throws.
/// </summary>
public sealed class KsyReader
{
    private readonly IByteSnapshot _data;
    private readonly KsyTemplate _template;
    private readonly KsyLimits _limits;

    private sealed class Budget { public long Work; }

    public KsyReader(IByteSnapshot data, KsyTemplate template, KsyLimits? limits = null)
    {
        _data = data;
        _template = template;
        _limits = limits ?? KsyLimits.Default;
        long length = data.ByteLength;
        Root = new KsyNode(template.Id, 0, length, type: template.Id, expand: () => Layout(template.Root, 0, length, 0, new Budget()));
    }

    public KsyNode Root { get; }

    private List<KsyNode> Layout(KsyTypeDef def, long start, long end, int depth, Budget budget)
    {
        var nodes = new List<KsyNode>();
        var values = new Dictionary<string, long>();
        long pos = start;
        foreach (var attr in def.Seq)
        {
            var node = ReadAttr(def, attr, pos, end, values, depth, budget, out long number);
            nodes.Add(node);
            if (node.Error is not null) break;
            pos = node.End;
            if (attr.Repeat == KsyRepeat.None && attr.Type.Kind == KsyKind.Int) values[attr.Id] = number;
        }
        return nodes;
    }

    private KsyNode Fail(KsyAttr attr, string name, long pos, long length, long end, KsyErrorKind kind, string subject = "") =>
        new(name, pos, Math.Clamp(length, 0, Math.Max(0, end - pos)), error: new KsyError(kind, attr.Line, subject == "" ? attr.Id : subject));

    private KsyNode ReadAttr(KsyTypeDef def, KsyAttr attr, long pos, long end, Dictionary<string, long> values, int depth, Budget budget, out long number)
    {
        number = 0;
        if (attr.Repeat == KsyRepeat.None) return ReadOne(def, attr, attr.Id, pos, end, values, depth, budget, out number);

        long count = -1;
        if (attr.Repeat == KsyRepeat.Expr)
        {
            count = Resolve(attr.RepeatCount!.Value, values);
            if (count < 0 || count > _limits.MaxRepeat) return Fail(attr, attr.Id, pos, 0, end, KsyErrorKind.LimitRepeat);
        }
        var (items, error) = Elements(def, attr, pos, end, count, values, depth, budget);
        long length = items.Count == 0 ? 0 : items[^1].End - pos;
        if (error is not null) return new KsyNode(attr.Id, pos, length, error: error);
        string name = attr.Id;
        long at = pos;
        return new KsyNode(name, pos, length, type: TypeLabel(attr) + "[" + items.Count + "]", expand: () =>
            Elements(def, attr, at, end, count, values, depth, new Budget()).Items);
    }

    private (List<KsyNode> Items, KsyError? Error) Elements(KsyTypeDef def, KsyAttr attr, long pos, long end, long count, Dictionary<string, long> values, int depth, Budget budget)
    {
        var items = new List<KsyNode>();
        for (int i = 0; attr.Repeat == KsyRepeat.Eos ? pos < end : i < count; i++)
        {
            if (i >= _limits.MaxRepeat) return (items, new KsyError(KsyErrorKind.LimitRepeat, attr.Line, attr.Id));
            var node = ReadOne(def, attr, $"{attr.Id}[{i}]", pos, end, values, depth, budget, out _);
            items.Add(node);
            if (node.Error is not null) return (items, node.Error);
            pos = node.End;
        }
        return (items, null);
    }

    private static string TypeLabel(KsyAttr a) => a.Type.Kind switch
    {
        KsyKind.User => a.Type.UserName!,
        KsyKind.Int => (a.Type.Signed ? "s" : "u") + a.Type.Width,
        KsyKind.Float => "f" + a.Type.Width,
        KsyKind.Str => "str",
        KsyKind.Strz => "strz",
        _ => "bytes",
    };

    private static long Resolve(KsyCount c, Dictionary<string, long> values) => c.Field is null ? c.Literal : values.GetValueOrDefault(c.Field, -1);

    private KsyNode ReadOne(KsyTypeDef def, KsyAttr attr, string name, long pos, long end, Dictionary<string, long> values, int depth, Budget budget, out long number)
    {
        number = 0;
        if (++budget.Work > _limits.MaxWork) return Fail(attr, name, pos, 0, end, KsyErrorKind.LimitWork);
        long room = end - pos;
        var type = attr.Type;
        string label = TypeLabel(attr);

        if (attr.Contents is { } expected)
        {
            if (expected.Length > room) return Fail(attr, name, pos, expected.Length, end, KsyErrorKind.OutOfFile);
            var actual = Read(pos, expected.Length);
            return actual.AsSpan().SequenceEqual(expected)
                ? new KsyNode(name, pos, expected.Length, Preview(actual), "bytes")
                : Fail(attr, name, pos, expected.Length, end, KsyErrorKind.Contents);
        }

        switch (type.Kind)
        {
            case KsyKind.Int:
            case KsyKind.Float:
            {
                if (type.Width > room) return Fail(attr, name, pos, type.Width, end, KsyErrorKind.OutOfFile);
                var raw = Read(pos, type.Width);
                bool big = (type.Order == KsyOrder.Default ? def.Endian : type.Order) == KsyOrder.Big;
                string text;
                if (type.Kind == KsyKind.Float)
                    text = (type.Width == 4
                        ? (big ? BinaryPrimitives.ReadSingleBigEndian(raw) : BinaryPrimitives.ReadSingleLittleEndian(raw))
                        : (big ? BinaryPrimitives.ReadDoubleBigEndian(raw) : BinaryPrimitives.ReadDoubleLittleEndian(raw))).ToString("R", CultureInfo.InvariantCulture);
                else
                {
                    ulong u = 0;
                    for (int i = 0; i < raw.Length; i++) u = (u << 8) | raw[big ? i : raw.Length - 1 - i];
                    number = type.Signed ? (long)(u << (64 - 8 * type.Width)) >> (64 - 8 * type.Width) : (long)u;
                    text = type.Signed ? number.ToString(CultureInfo.InvariantCulture) : u.ToString(CultureInfo.InvariantCulture);
                    if (attr.Enum is { } e) text = (_template.Enums[e].TryGetValue(number, out var label2) ? label2 + " (" + text + ")" : text);
                    else if (!type.Signed && type.Width > 1) text += " (0x" + u.ToString("X" + type.Width * 2, CultureInfo.InvariantCulture) + ")";
                }
                return new KsyNode(name, pos, type.Width, text, label);
            }
            case KsyKind.Strz:
            {
                long limit = Math.Min(room, _limits.MaxStringBytes + 1L);
                var buffer = Read(pos, (int)limit);
                int zero = Array.IndexOf(buffer, (byte)0);
                if (zero < 0) return Fail(attr, name, pos, limit, end, room <= _limits.MaxStringBytes ? KsyErrorKind.OutOfFile : KsyErrorKind.LimitSize);
                return new KsyNode(name, pos, zero + 1, Quote(Decode(attr.Encoding!, buffer.AsSpan(0, zero))), label);
            }
            case KsyKind.Str:
            case KsyKind.Bytes:
            case KsyKind.User:
            {
                long size = attr.SizeEos ? room : attr.Size is { } s ? Resolve(s, values) : -1;
                if (type.Kind == KsyKind.User) return ReadStruct(attr, name, pos, end, size, depth, budget);
                if (size < 0) return Fail(attr, name, pos, 0, end, KsyErrorKind.LimitSize);
                if (size > room) return Fail(attr, name, pos, size, end, KsyErrorKind.OutOfFile);
                if (type.Kind == KsyKind.Bytes) return new KsyNode(name, pos, size, Preview(Read(pos, (int)Math.Min(size, 16))) + (size > 16 ? " …" : ""), label);
                if (size > _limits.MaxStringBytes) return Fail(attr, name, pos, size, end, KsyErrorKind.LimitSize);
                return new KsyNode(name, pos, size, Quote(Decode(attr.Encoding!, Read(pos, (int)size))), label);
            }
        }
        return Fail(attr, name, pos, 0, end, KsyErrorKind.Invalid);
    }

    private KsyNode ReadStruct(KsyAttr attr, string name, long pos, long end, long size, int depth, Budget budget)
    {
        var def = _template.Types[attr.Type.UserName!];
        if (depth + 1 > _limits.MaxDepth) return Fail(attr, name, pos, 0, end, KsyErrorKind.LimitDepth);
        long inner = end;
        if (attr.Size is not null)
        {
            if (size < 0) return Fail(attr, name, pos, 0, end, KsyErrorKind.LimitSize);
            if (size > end - pos) return Fail(attr, name, pos, size, end, KsyErrorKind.OutOfFile);
            inner = pos + size;
        }
        else if (!attr.SizeEos)
        {
            // Without a size the extent of the structure is where its last field ends; the fields are walked and dropped, and read again when the node is opened.
            var fields = Layout(def, pos, end, depth + 1, budget);
            if (fields.Count > 0 && fields[^1].Error is { } error)
                return new KsyNode(name, pos, fields[^1].End - pos, error: error);
            long length = fields.Count == 0 ? 0 : fields[^1].End - pos;
            return new KsyNode(name, pos, length, type: def.Name, expand: () => Layout(def, pos, end, depth + 1, new Budget()));
        }
        else inner = end;
        long start = pos;
        return new KsyNode(name, pos, inner - pos, type: def.Name, expand: () => Layout(def, start, inner, depth + 1, new Budget()));
    }

    private byte[] Read(long offset, int length)
    {
        var buffer = new byte[length];
        int got = _data.Read(offset, buffer);
        return got == length ? buffer : buffer.AsSpan(0, got).ToArray();
    }

    private static string Preview(byte[] bytes) => Convert.ToHexString(bytes, 0, Math.Min(bytes.Length, 16));

    private static string Quote(string s) => "\"" + s + "\"";

    private static string Decode(string encoding, ReadOnlySpan<byte> bytes) => encoding switch
    {
        "UTF-8" => Encoding.UTF8.GetString(bytes),
        "ASCII" => Encoding.ASCII.GetString(bytes),
        "ISO-8859-1" => Encoding.Latin1.GetString(bytes),
        "UTF-16LE" => Encoding.Unicode.GetString(bytes),
        _ => Encoding.BigEndianUnicode.GetString(bytes),
    };
}

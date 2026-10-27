using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Hex.Ksy;

/// <summary>
/// Limits that keep a hostile template or file from exhausting the editor. The numbers are placeholders: their real values are an open input decision (P8.10.3),
/// and so is a limit on time. <c>MaxExprChars</c> and <c>MaxExprDepth</c> bound the text and the nesting of one expression (P8.10.4); computing it counts against <c>MaxWork</c>,
/// and a chain of instances or of lazily read structures a field refers to is cut at <c>MaxDepth</c>.
/// </summary>
public sealed record KsyLimits(int MaxDepth = 32, int MaxRepeat = 10_000, int MaxStringBytes = 65_536, long MaxWork = 1_000_000, int MaxExprChars = 512, int MaxExprDepth = 24)
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

/// <summary>Where a structure sits while it is read: its place in the stream, the fields read so far and the instances computed so far, which expressions look up.</summary>
internal sealed class KsyScope
{
    public KsyScope(KsyTypeDef def, KsyScope? parent, long start, long ioBase, long end, int depth)
    {
        Def = def;
        Parent = parent;
        Root = parent?.Root ?? this;
        Start = start;
        IoBase = ioBase;
        End = end;
        Depth = depth;
    }

    public KsyTypeDef Def { get; }
    public KsyScope? Parent { get; }
    public KsyScope Root { get; }
    public long Start { get; }
    /// <summary>Where the stream of this structure starts (a structure with <c>size</c> is a stream of its own); <c>pos</c> and <c>_io.pos</c> count from here.</summary>
    public long IoBase { get; }
    public long End { get; }
    public int Depth { get; }
    public Dictionary<string, object?> Values { get; } = [];
    public HashSet<string> Evaluating { get; } = [];
    public bool Filled { get; set; }
    public bool Filling { get; set; }
}

/// <summary>The stream of a structure for <c>_io</c>; <see cref="Pos"/> is -1 when the position is not known.</summary>
internal sealed record KsyIo(KsyScope Scope, long Pos);

/// <summary>
/// Reads a file through a <see cref="KsyTemplate"/> without loading it: <see cref="Root"/> is known at once and the fields of a node are read when its
/// <see cref="KsyNode.Children"/> are asked for. A field that cannot be read (past the end, wrong magic, a limit hit, an expression that cannot be computed) becomes a node with an
/// <see cref="KsyNode.Error"/>, and the fields behind it are not read, because their place is unknown. The reader throws only what the byte source throws.
/// </summary>
public sealed class KsyReader
{
    private readonly IByteSnapshot _data;
    private readonly KsyTemplate _template;
    private readonly KsyLimits _limits;

    private sealed class Budget
    {
        public long Work;
        public int Nest;
    }

    public KsyReader(IByteSnapshot data, KsyTemplate template, KsyLimits? limits = null)
    {
        _data = data;
        _template = template;
        _limits = limits ?? KsyLimits.Default;
        long length = data.ByteLength;
        var scope = new KsyScope(template.Root, null, 0, 0, length, 0);
        Root = new KsyNode(template.Id, 0, length, type: template.Id, expand: () => Layout(scope, new Budget(), true));
    }

    public KsyNode Root { get; }

    /// <summary>Reads the fields of a structure in order; the instances follow them when <paramref name="withInstances"/> is set (they have no place in the sequence).</summary>
    private List<KsyNode> Layout(KsyScope scope, Budget budget, bool withInstances = false)
    {
        var nodes = new List<KsyNode>();
        long pos = scope.Start;
        scope.Filling = true;
        try
        {
            foreach (var attr in scope.Def.Seq)
            {
                var node = ReadAttr(scope, attr, pos, budget, out var value);
                if (node is null)
                {
                    scope.Values[attr.Id] = null;
                    continue;
                }
                nodes.Add(node);
                if (node.Error is not null) break;
                pos = node.End;
                scope.Values[attr.Id] = value;
            }
        }
        finally
        {
            scope.Filling = false;
            scope.Filled = true;
        }
        if (withInstances)
            foreach (var inst in scope.Def.Instances)
                if (ReadInstance(scope, inst, budget, out var value) is { } node)
                {
                    nodes.Add(node);
                    if (node.Error is null) scope.Values[inst.Id] = value;
                }
        return nodes;
    }

    private KsyNode Fail(KsyAttr attr, string name, long pos, long length, long end, KsyErrorKind kind, string subject = "") =>
        new(name, pos, Math.Clamp(length, 0, Math.Max(0, end - pos)), error: new KsyError(kind, attr.Line, subject == "" ? attr.Id : subject));

    /// <summary>Reads a field, or all its repeats. Null when its <c>if</c> is false: the field is not there.</summary>
    private KsyNode? ReadAttr(KsyScope scope, KsyAttr attr, long pos, Budget budget, out object? value)
    {
        value = null;
        try
        {
            if (attr.If is { } condition && !Cond(condition, scope, pos, budget)) return null;
            if (attr.Repeat == KsyRepeat.None) return ReadOne(scope, attr, attr.Id, pos, -1, budget, out value);

            long count = -1;
            if (attr.Repeat == KsyRepeat.Expr)
            {
                count = Num(attr.RepeatCount!, scope, pos, budget);
                if (count < 0 || count > _limits.MaxRepeat) return Fail(attr, attr.Id, pos, 0, scope.End, KsyErrorKind.LimitRepeat);
            }
            var (items, values, error) = Elements(scope, attr, pos, count, budget);
            value = values;
            long length = items.Count == 0 ? 0 : items[^1].End - pos;
            if (error is not null) return new KsyNode(attr.Id, pos, length, error: error);
            long at = pos;
            return new KsyNode(attr.Id, pos, length, type: TypeLabel(attr) + "[" + items.Count + "]", expand: () => Elements(scope, attr, at, count, new Budget()).Items);
        }
        catch (KsyExprException e)
        {
            return Fail(attr, attr.Id, pos, 0, scope.End, e.Kind, e.Subject);
        }
    }

    private (List<KsyNode> Items, List<object?> Values, KsyError? Error) Elements(KsyScope scope, KsyAttr attr, long pos, long count, Budget budget)
    {
        var items = new List<KsyNode>();
        var values = new List<object?>();
        for (int i = 0; attr.Repeat switch { KsyRepeat.Eos => pos < scope.End, KsyRepeat.Expr => i < count, _ => true }; i++)
        {
            if (i >= _limits.MaxRepeat) return (items, values, new KsyError(KsyErrorKind.LimitRepeat, attr.Line, attr.Id));
            try
            {
                var node = ReadOne(scope, attr, $"{attr.Id}[{i}]", pos, i, budget, out var value);
                items.Add(node);
                if (node.Error is not null) return (items, values, node.Error);
                values.Add(value);
                pos = node.End;
                if (attr.Repeat == KsyRepeat.Until && Cond(attr.RepeatUntil!, scope, pos, budget, i, true, value)) break;
            }
            catch (KsyExprException e)
            {
                var node = Fail(attr, $"{attr.Id}[{i}]", pos, 0, scope.End, e.Kind, e.Subject);
                items.Add(node);
                return (items, values, node.Error);
            }
        }
        return (items, values, null);
    }

    private static string TypeLabel(KsyAttr a) => a.Type.Kind switch
    {
        KsyKind.User => a.Type.UserName!,
        KsyKind.Int => (a.Type.Signed ? "s" : "u") + a.Type.Width,
        KsyKind.Float => "f" + a.Type.Width,
        KsyKind.Str => "str",
        KsyKind.Strz => "strz",
        KsyKind.Switch => "switch",
        _ => "bytes",
    };

    // ---- expressions ----

    private object? Eval(KsyExpr e, KsyScope scope, long pos, Budget budget, long index = -1, bool hasItem = false, object? item = null) =>
        e.Eval(new Ctx(this, scope, pos, budget, index, hasItem, item));

    private long Num(KsyExpr e, KsyScope scope, long pos, Budget budget, long index = -1) =>
        Eval(e, scope, pos, budget, index) is long n ? n : throw new KsyExprException(KsyErrorKind.Expression, e.Source);

    private bool Cond(KsyExpr e, KsyScope scope, long pos, Budget budget, long index = -1, bool hasItem = false, object? item = null) =>
        Eval(e, scope, pos, budget, index, hasItem, item) is bool b ? b : throw new KsyExprException(KsyErrorKind.Expression, e.Source);

    /// <summary>A field of a structure by name: one already read, else an instance, which is computed once.</summary>
    private object? Field(KsyScope scope, string name, Budget budget)
    {
        if (scope.Values.TryGetValue(name, out var value)) return value;
        var inst = scope.Def.Instances.FirstOrDefault(i => i.Id == name) ?? throw new KsyExprException(KsyErrorKind.Expression, name);
        return Instance(scope, inst, budget);
    }

    private object? Instance(KsyScope scope, KsyAttr inst, Budget budget)
    {
        if (budget.Nest >= _limits.MaxDepth) throw new KsyExprException(KsyErrorKind.LimitDepth, inst.Id);
        if (!scope.Evaluating.Add(inst.Id)) throw new KsyExprException(KsyErrorKind.Expression, inst.Id);
        budget.Nest++;
        try
        {
            var node = ReadInstance(scope, inst, budget, out var value);
            if (node?.Error is { } error) throw new KsyExprException(error.Kind, error.Subject);
            scope.Values[inst.Id] = value;
            return value;
        }
        finally
        {
            budget.Nest--;
            scope.Evaluating.Remove(inst.Id);
        }
    }

    /// <summary>Reads the fields of a structure that an expression refers to through <c>a.b</c> or <c>_parent.b</c>, when they have not been read yet.</summary>
    private void Ensure(KsyScope scope, Budget budget)
    {
        if (scope.Filled || scope.Filling) return;
        if (budget.Nest >= _limits.MaxDepth) throw new KsyExprException(KsyErrorKind.LimitDepth, "");
        budget.Nest++;
        try { Layout(scope, budget); }
        finally { budget.Nest--; }
    }

    private KsyNode? ReadInstance(KsyScope scope, KsyAttr inst, Budget budget, out object? value)
    {
        value = null;
        try
        {
            if (inst.Value is { } expr)
            {
                if (inst.If is { } condition && !Cond(condition, scope, scope.Start, budget)) return null;
                value = Eval(expr, scope, scope.Start, budget);
                return new KsyNode(inst.Id, scope.Start, 0, FormatValue(value), "value");
            }
            long at = Num(inst.Pos!, scope, scope.Start, budget);
            if (at < 0 || at > scope.End - scope.IoBase) return Fail(inst, inst.Id, scope.End, 0, scope.End, KsyErrorKind.OutOfFile);
            return ReadAttr(scope, inst, scope.IoBase + at, budget, out value);
        }
        catch (KsyExprException e)
        {
            return Fail(inst, inst.Id, scope.Start, 0, scope.End, e.Kind, e.Subject);
        }
    }

    private static string FormatValue(object? value) => value switch
    {
        long n => n.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        string s => Quote(s),
        List<object?> l => "[" + l.Count + "]",
        _ => "",
    };

    private sealed class Ctx(KsyReader reader, KsyScope scope, long pos, Budget budget, long index, bool hasItem, object? item) : IKsyExprHost
    {
        public void Tick()
        {
            if (++budget.Work > reader._limits.MaxWork) throw new KsyExprException(KsyErrorKind.LimitWork, "");
        }

        private static KsyExprException Missing(string name) => new(KsyErrorKind.Expression, name);

        public object? Name(string name)
        {
            switch (name)
            {
                case "_index": return index >= 0 ? index : throw Missing(name);
                case "_": return hasItem ? item : throw Missing(name);
                case "_io": return new KsyIo(scope, pos);
                case "_parent": return scope.Parent ?? throw Missing(name);
                case "_root": return scope.Root;
                default: return reader.Field(scope, name, budget);
            }
        }

        public long EnumValue(string enumName, string member)
        {
            if (reader._template.Enums.TryGetValue(enumName, out var values))
                foreach (var (key, label) in values)
                    if (label == member) return key;
            throw Missing(enumName + "::" + member);
        }

        public object? Member(object? target, string name)
        {
            if (target is KsyScope s)
            {
                if (name == "_parent") return s.Parent ?? throw Missing(name);
                if (name == "_root") return s.Root;
                if (name == "_io") return new KsyIo(s, -1);
                reader.Ensure(s, budget);
                return reader.Field(s, name, budget);
            }
            if (target is KsyIo io)
            {
                if (name == "size") return io.Scope.End - io.Scope.IoBase;
                if (io.Pos < 0) throw Missing(name);
                if (name == "pos") return io.Pos - io.Scope.IoBase;
                if (name == "eof") return io.Pos >= io.Scope.End;
            }
            throw Missing(name);
        }
    }

    // ---- reading ----

    /// <summary>The field with the type that <c>switch-on</c> picks; with no branch for the value the bytes are raw.</summary>
    private KsyAttr Choose(KsyScope scope, KsyAttr attr, KsySwitch sw, long pos, long index, Budget budget)
    {
        var on = Eval(sw.On, scope, pos, budget, index);
        KsyType? chosen = null, fallback = null;
        foreach (var branch in sw.Cases)
        {
            if (branch.Key is null) fallback = branch.Type;
            else if (Equals(on, Eval(branch.Key, scope, pos, budget, index))) { chosen = branch.Type; break; }
        }
        return attr with { Type = chosen ?? fallback ?? new KsyType(KsyKind.Bytes), Switch = null };
    }

    private KsyNode ReadOne(KsyScope scope, KsyAttr attr, string name, long pos, long index, Budget budget, out object? value)
    {
        value = null;
        long end = scope.End;
        if (++budget.Work > _limits.MaxWork) return Fail(attr, name, pos, 0, end, KsyErrorKind.LimitWork);
        long room = end - pos;
        if (attr.Switch is { } sw) attr = Choose(scope, attr, sw, pos, index, budget);
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
                bool big = (type.Order == KsyOrder.Default ? scope.Def.Endian : type.Order) == KsyOrder.Big;
                string text;
                if (type.Kind == KsyKind.Float)
                    text = (type.Width == 4
                        ? (big ? BinaryPrimitives.ReadSingleBigEndian(raw) : BinaryPrimitives.ReadSingleLittleEndian(raw))
                        : (big ? BinaryPrimitives.ReadDoubleBigEndian(raw) : BinaryPrimitives.ReadDoubleLittleEndian(raw))).ToString("R", CultureInfo.InvariantCulture);
                else
                {
                    ulong u = 0;
                    for (int i = 0; i < raw.Length; i++) u = (u << 8) | raw[big ? i : raw.Length - 1 - i];
                    long number = type.Signed ? (long)(u << (64 - 8 * type.Width)) >> (64 - 8 * type.Width) : (long)u;
                    value = number;
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
                string decoded = Decode(attr.Encoding!, buffer.AsSpan(0, zero));
                value = decoded;
                return new KsyNode(name, pos, zero + 1, Quote(decoded), label);
            }
            case KsyKind.Str:
            case KsyKind.Bytes:
            case KsyKind.User:
            {
                long size = attr.SizeEos ? room : attr.Size is { } s ? Num(s, scope, pos, budget, index) : -1;
                if (type.Kind == KsyKind.User) return ReadStruct(scope, attr, name, pos, size, budget, out value);
                if (size < 0) return Fail(attr, name, pos, 0, end, KsyErrorKind.LimitSize);
                if (size > room) return Fail(attr, name, pos, size, end, KsyErrorKind.OutOfFile);
                if (type.Kind == KsyKind.Bytes) return new KsyNode(name, pos, size, Preview(Read(pos, (int)Math.Min(size, 16))) + (size > 16 ? " …" : ""), label);
                if (size > _limits.MaxStringBytes) return Fail(attr, name, pos, size, end, KsyErrorKind.LimitSize);
                string decoded = Decode(attr.Encoding!, Read(pos, (int)size));
                value = decoded;
                return new KsyNode(name, pos, size, Quote(decoded), label);
            }
        }
        return Fail(attr, name, pos, 0, end, KsyErrorKind.Invalid);
    }

    private KsyNode ReadStruct(KsyScope parent, KsyAttr attr, string name, long pos, long size, Budget budget, out object? value)
    {
        value = null;
        var def = _template.Types[attr.Type.UserName!];
        long end = parent.End;
        if (parent.Depth + 1 > _limits.MaxDepth) return Fail(attr, name, pos, 0, end, KsyErrorKind.LimitDepth);
        if (attr.Size is not null)
        {
            if (size < 0) return Fail(attr, name, pos, 0, end, KsyErrorKind.LimitSize);
            if (size > end - pos) return Fail(attr, name, pos, size, end, KsyErrorKind.OutOfFile);
            var sub = new KsyScope(def, parent, pos, pos, pos + size, parent.Depth + 1);
            value = sub;
            return new KsyNode(name, pos, size, type: def.Name, expand: () => Layout(sub, new Budget(), true));
        }
        var child = new KsyScope(def, parent, pos, parent.IoBase, end, parent.Depth + 1);
        value = child;
        if (attr.SizeEos) return new KsyNode(name, pos, end - pos, type: def.Name, expand: () => Layout(child, new Budget(), true));
        // Without a size the extent of the structure is where its last field ends; the fields are walked and dropped, and read again when the node is opened.
        var fields = Layout(child, budget);
        if (fields.Count > 0 && fields[^1].Error is { } error)
            return new KsyNode(name, pos, fields[^1].End - pos, error: error);
        long length = fields.Count == 0 ? 0 : fields[^1].End - pos;
        return new KsyNode(name, pos, length, type: def.Name, expand: () => Layout(child, new Budget(), true));
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

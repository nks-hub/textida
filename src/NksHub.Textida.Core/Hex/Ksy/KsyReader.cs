using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Hex.Ksy;

/// <summary>
/// Limits that keep a hostile template or file from exhausting the editor. Placeholder, OPEN: the numbers are not decided (P8.10.3), the owner has not chosen them.
/// <c>MaxExprChars</c> and <c>MaxExprDepth</c> bound the text and the nesting of one expression (P8.10.4); computing it counts against <c>MaxWork</c>,
/// and a chain of instances or of lazily read structures a field refers to is cut at <c>MaxDepth</c>. <c>MaxMilliseconds</c> is the time one expansion of a node may take (0 switches it off);
/// <c>PageSize</c> is the most children one node shows at once: a longer array is shown as groups of ranges (<c>items[0..255]</c>), so a tree never gets thousands of rows in one step.
/// </summary>
public sealed record KsyLimits(int MaxDepth = 32, int MaxRepeat = 10_000, int MaxStringBytes = 65_536, long MaxWork = 1_000_000, int MaxExprChars = 512, int MaxExprDepth = 24, int MaxMilliseconds = 3_000, int PageSize = 256)
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

    /// <summary>
    /// The nodes from this one down to the deepest one that holds the byte at <paramref name="offset"/>, reading the children on the way; empty when this node does not hold it.
    /// Where a field and an instance overlap, the field wins (instances come last).
    /// </summary>
    public IReadOnlyList<KsyNode> PathTo(long offset)
    {
        var path = new List<KsyNode>();
        KsyNode? node = this;
        while (node is not null && node.Start <= offset && offset < node.End)
        {
            path.Add(node);
            KsyNode? next = null;
            if (node.HasChildren)
                foreach (var child in node.Children)
                    if (child.Start <= offset && offset < child.End) { next = child; break; }
            node = next;
        }
        return path;
    }
}

/// <summary>Where a structure sits while it is read: its place in the stream, the fields read so far and the instances computed so far, which expressions look up.</summary>
internal sealed class KsyScope
{
    private HashSet<string>? _evaluating;

    public KsyScope(KsyTypeDef def, KsyScope? parent, long start, long ioBase, long end, int depth, Dictionary<string, object?>? values = null)
    {
        Values = values ?? [];
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
    public Dictionary<string, object?> Values { get; }
    public HashSet<string> Evaluating => _evaluating ??= [];
    public bool Filled { get; set; }
    public bool Filling { get; set; }
    /// <summary>The byte order the data chose for a type with <c>endian: switch-on</c>, once it is computed.</summary>
    public KsyOrder? Order { get; set; }

    /// <summary>The same structure seen through another stream (<c>io: _root._io</c>): the fields are shared, <c>pos</c> counts from <paramref name="ioBase"/> and the data ends at <paramref name="end"/>.</summary>
    public KsyScope View(long ioBase, long end) => new(Def, Parent, Start, ioBase, end, Depth, Values) { Filled = true, Order = Order };
}

/// <summary>The stream of a structure for <c>_io</c>; <see cref="Pos"/> is -1 when the position is not known.</summary>
internal sealed record KsyIo(KsyScope Scope, long Pos);

/// <summary>
/// Reads a file through a <see cref="KsyTemplate"/> without loading it: <see cref="Root"/> is known at once and the fields of a node are read when its
/// <see cref="KsyNode.Children"/> are asked for. A field that cannot be read (past the end, wrong magic, a limit hit, an expression that cannot be computed) becomes a node with an
/// <see cref="KsyNode.Error"/>, and the fields behind it are not read, because their place is unknown. The reader throws only what the byte source throws.
/// An array is walked once, to find where it ends, without keeping its elements; its children are read again when the node is opened, a page at a time (<see cref="KsyLimits.PageSize"/>).
/// </summary>
public sealed class KsyReader
{
    private readonly IByteSnapshot _data;
    private readonly KsyTemplate _template;
    private readonly KsyLimits _limits;

    /// <summary>The work and the time one expansion may spend; every expansion of a node starts a new one.</summary>
    private sealed class Budget(long deadline)
    {
        public long Work;
        public int Nest;
        public long Deadline { get; } = deadline;
    }

    /// <summary>Where an array ends and where each element or each page of it starts: <paramref name="StartOf"/> takes an index that is a multiple of the page size, or any index when the elements have a fixed size.</summary>
    private sealed record ArrayWalk(int Count, long End, Func<int, long> StartOf, IReadOnlyList<object?> Values, KsyError? Error);

    /// <summary>The values of an array that was not walked: an element is read when an expression asks for it.</summary>
    private sealed class LazyValues(int count, Func<int, object?> at) : IReadOnlyList<object?>
    {
        public int Count => count;
        public object? this[int index] => at(index);
        public IEnumerator<object?> GetEnumerator()
        {
            for (int i = 0; i < count; i++) yield return at(i);
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public KsyReader(IByteSnapshot data, KsyTemplate template, KsyLimits? limits = null)
    {
        _data = data;
        _template = template;
        _limits = limits ?? KsyLimits.Default;
        long length = data.ByteLength;
        var scope = new KsyScope(template.Root, null, 0, 0, length, 0);
        Root = new KsyNode(template.Id, 0, length, type: template.Id, expand: () => Layout(scope, NewBudget(), true));
    }

    public KsyNode Root { get; }

    private int PageSize => Math.Max(2, _limits.PageSize);

    private Budget NewBudget() => new(_limits.MaxMilliseconds > 0 ? Stopwatch.GetTimestamp() + _limits.MaxMilliseconds * Stopwatch.Frequency / 1000 : long.MaxValue);

    private static bool Late(Budget budget) => Stopwatch.GetTimestamp() > budget.Deadline;

    /// <summary>Reads the fields of a structure in order; the instances follow them when <paramref name="withInstances"/> is set (they have no place in the sequence).</summary>
    private List<KsyNode> Layout(KsyScope scope, Budget budget, bool withInstances = false)
    {
        var nodes = new List<KsyNode>();
        long pos = scope.Start;
        int bit = 0;
        scope.Filling = true;
        try
        {
            foreach (var attr in scope.Def.Seq)
            {
                var node = ReadAttr(scope, attr, pos, budget, out var value, bit);
                if (node is null)
                {
                    scope.Values[attr.Id] = null;
                    continue;
                }
                nodes.Add(node);
                if (node.Error is not null) break;
                if (attr.Type.Kind == KsyKind.Bits)
                {
                    // Bit-sized fields share bytes: the next one starts in the byte where this one stopped, unless it filled that byte.
                    int used = bit + attr.Type.Width;
                    pos = node.Start + used / 8;
                    bit = used % 8;
                }
                else
                {
                    pos = node.End;
                    bit = 0;
                }
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
    private KsyNode? ReadAttr(KsyScope scope, KsyAttr attr, long pos, Budget budget, out object? value, int bit = 0)
    {
        value = null;
        try
        {
            if (attr.If is { } condition && !Cond(condition, scope, pos, budget)) return null;
            if (attr.Repeat == KsyRepeat.None) return ReadOne(scope, attr, attr.Id, pos, -1, budget, out value, bit);

            long count = -1;
            if (attr.Repeat == KsyRepeat.Expr)
            {
                count = Num(attr.RepeatCount!, scope, pos, budget);
                if (count < 0) return Fail(attr, attr.Id, pos, 0, scope.End, KsyErrorKind.LimitRepeat);
            }
            // Elements of a size the template fixes are not walked: the array is as long as the count says, however large, and the elements are found by arithmetic.
            if (attr.Repeat != KsyRepeat.Until && _template.ElementSize(attr) is > 0 and long stride)
            {
                long room = scope.End - pos;
                long n = attr.Repeat == KsyRepeat.Expr ? count : room % stride == 0 ? room / stride : -1;
                if (n >= 0 && n <= int.MaxValue)
                {
                    if (n > room / stride) return Fail(attr, attr.Id, pos, room, scope.End, KsyErrorKind.OutOfFile);
                    long first = pos;
                    var fixedWalk = new ArrayWalk((int)n, pos + n * stride, i => first + i * stride, new LazyValues((int)n, i => ElementValue(scope, attr, first + i * stride, i)), null);
                    value = fixedWalk.Values;
                    return ArrayNode(scope, attr, pos, fixedWalk);
                }
            }
            if (count > _limits.MaxRepeat) return Fail(attr, attr.Id, pos, 0, scope.End, KsyErrorKind.LimitRepeat);
            var walk = Walk(scope, attr, pos, count, budget);
            value = walk.Values;
            if (walk.Error is not null) return new KsyNode(attr.Id, pos, walk.End - pos, error: walk.Error);
            return ArrayNode(scope, attr, pos, walk);
        }
        catch (KsyExprException e)
        {
            return Fail(attr, attr.Id, pos, 0, scope.End, e.Kind, e.Subject);
        }
    }

    private KsyNode ArrayNode(KsyScope scope, KsyAttr attr, long pos, ArrayWalk walk) =>
        new(attr.Id, pos, walk.End - pos, type: TypeLabel(attr) + "[" + walk.Count + "]", expand: () => Page(scope, attr, walk, 0, walk.Count));

    private object? ElementValue(KsyScope scope, KsyAttr attr, long at, int index)
    {
        var node = ReadOne(scope, attr, attr.Id, at, index, NewBudget(), out var value);
        return node.Error is { } error ? throw new KsyExprException(error.Kind, error.Subject) : value;
    }

    /// <summary>Reads every element once to find the end of the array; the elements themselves are not kept, only the start of each page and the values expressions may look up.</summary>
    private ArrayWalk Walk(KsyScope scope, KsyAttr attr, long pos, long count, Budget budget)
    {
        var starts = new List<long>();
        var values = new List<object?>();
        long end = pos;
        int n = 0;
        while (attr.Repeat switch { KsyRepeat.Eos => pos < scope.End, KsyRepeat.Expr => n < count, _ => true })
        {
            if (n >= _limits.MaxRepeat) return new ArrayWalk(n, end, i => starts[i / PageSize], values, new KsyError(KsyErrorKind.LimitRepeat, attr.Line, attr.Id));
            if (n % PageSize == 0) starts.Add(pos);
            try
            {
                var node = ReadOne(scope, attr, $"{attr.Id}[{n}]", pos, n, budget, out var value);
                end = node.End;
                if (node.Error is not null) return new ArrayWalk(n + 1, end, i => starts[i / PageSize], values, node.Error);
                values.Add(value);
                pos = node.End;
                n++;
                if (attr.Repeat == KsyRepeat.Until && Cond(attr.RepeatUntil!, scope, pos, budget, n - 1, true, value)) break;
            }
            catch (KsyExprException e)
            {
                var node = Fail(attr, $"{attr.Id}[{n}]", pos, 0, scope.End, e.Kind, e.Subject);
                return new ArrayWalk(n + 1, node.End, i => starts[i / PageSize], values, node.Error);
            }
        }
        return new ArrayWalk(n, end, i => starts[i / PageSize], values, null);
    }

    /// <summary>
    /// The children of the elements <paramref name="lo"/> to <paramref name="hi"/> of a walked array: the elements when they are not more than a page, otherwise groups of ranges,
    /// each group a page or a power of the page size of elements, so no node ever has more than a page of children. <paramref name="lo"/> is a multiple of the page size.
    /// </summary>
    private List<KsyNode> Page(KsyScope scope, KsyAttr attr, ArrayWalk walk, int lo, int hi)
    {
        int page = PageSize;
        if (hi - lo <= page) return Elements(scope, attr, walk, lo, hi);
        long step = page;
        while (step * page < hi - lo) step *= page;
        var groups = new List<KsyNode>();
        for (long a = lo; a < hi; a += step)
        {
            int from = (int)a, to = (int)Math.Min(hi, a + step);
            long start = walk.StartOf(from);
            long stop = to < walk.Count ? walk.StartOf(to) : walk.End;
            groups.Add(new KsyNode($"{attr.Id}[{from}..{to - 1}]", start, stop - start, type: TypeLabel(attr) + "[" + (to - from) + "]", expand: () => Page(scope, attr, walk, from, to)));
        }
        return groups;
    }

    private List<KsyNode> Elements(KsyScope scope, KsyAttr attr, ArrayWalk walk, int lo, int hi)
    {
        var nodes = new List<KsyNode>();
        if (hi <= lo) return nodes;
        var budget = NewBudget();
        long pos = walk.StartOf(lo);
        for (int i = lo; i < hi; i++)
        {
            try
            {
                var node = ReadOne(scope, attr, $"{attr.Id}[{i}]", pos, i, budget, out _);
                nodes.Add(node);
                if (node.Error is not null) break;
                pos = node.End;
            }
            catch (KsyExprException e)
            {
                nodes.Add(Fail(attr, $"{attr.Id}[{i}]", pos, 0, scope.End, e.Kind, e.Subject));
                break;
            }
        }
        return nodes;
    }

    private static string TypeLabel(KsyAttr a) => a.Type.Kind switch
    {
        KsyKind.User => a.Type.UserName!,
        KsyKind.Int => (a.Type.Signed ? "s" : "u") + a.Type.Width,
        KsyKind.Float => "f" + a.Type.Width,
        KsyKind.Bits => "b" + a.Type.Width,
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
            var stream = scope;
            if (inst.Io is { } io)
            {
                if (Eval(io, scope, scope.Start, budget) is not KsyIo target) throw new KsyExprException(KsyErrorKind.Expression, io.Source);
                if (!ReferenceEquals(target.Scope, scope)) stream = scope.View(target.Scope.IoBase, target.Scope.End);
            }
            long at = Num(inst.Pos!, scope, scope.Start, budget);
            if (at < 0 || at > stream.End - stream.IoBase) return Fail(inst, inst.Id, scope.End, 0, scope.End, KsyErrorKind.OutOfFile);
            return ReadAttr(stream, inst, stream.IoBase + at, budget, out value);
        }
        catch (KsyExprException e)
        {
            return Fail(inst, inst.Id, scope.Start, 0, scope.End, e.Kind, e.Subject);
        }
    }

    private static string FormatValue(object? value) => value switch
    {
        long n => n.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        string s => Quote(s),
        IReadOnlyList<object?> l => "[" + l.Count + "]",
        _ => "",
    };

    private sealed class Ctx(KsyReader reader, KsyScope scope, long pos, Budget budget, long index, bool hasItem, object? item) : IKsyExprHost
    {
        public void Tick()
        {
            if (++budget.Work > reader._limits.MaxWork) throw new KsyExprException(KsyErrorKind.LimitWork, "");
            if ((budget.Work & 31) == 0 && Late(budget)) throw new KsyExprException(KsyErrorKind.LimitTime, "");
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
            if (reader._template.ResolveEnum(scope.Def, enumName) is { } key)
                foreach (var (value, label) in reader._template.Enums[key])
                    if (label == member) return value;
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
    private KsyAttr Choose(KsyScope scope, KsyAttr attr, KsySwitch sw, long pos, long index, Budget budget, out bool matched)
    {
        var on = Eval(sw.On, scope, pos, budget, index);
        KsyType? chosen = null, fallback = null;
        foreach (var branch in sw.Cases)
        {
            if (branch.Key is null) fallback = branch.Type;
            else if (Equals(on, Eval(branch.Key, scope, pos, budget, index))) { chosen = branch.Type; break; }
        }
        matched = chosen is not null || fallback is not null;
        return attr with { Type = chosen ?? fallback ?? new KsyType(KsyKind.Bytes), Switch = null };
    }

    /// <summary>The byte order of a structure: fixed by its template, or computed once from the data when the template has <c>endian: switch-on</c>.</summary>
    private KsyOrder OrderOf(KsyScope scope, Budget budget)
    {
        if (scope.Order is { } known) return known;
        if (scope.Def.EndianSwitch is not { } sw) return scope.Def.Endian;
        var on = Eval(sw.On, scope, scope.Start, budget);
        foreach (var (key, order) in sw.Cases)
            if (Equals(on, Eval(key, scope, scope.Start, budget)))
            {
                scope.Order = order;
                return order;
            }
        throw new KsyExprException(KsyErrorKind.Expression, sw.On.Source);
    }

    private KsyNode ReadOne(KsyScope scope, KsyAttr attr, string name, long pos, long index, Budget budget, out object? value, int bit = 0)
    {
        value = null;
        long end = scope.End;
        if (++budget.Work > _limits.MaxWork) return Fail(attr, name, pos, 0, end, KsyErrorKind.LimitWork);
        if (Late(budget)) return Fail(attr, name, pos, 0, end, KsyErrorKind.LimitTime);
        long room = end - pos;
        if (attr.Switch is { } sw)
        {
            attr = Choose(scope, attr, sw, pos, index, budget, out bool matched);
            if (!matched && attr.Size is null && !attr.SizeEos) return Fail(attr, name, pos, 0, end, KsyErrorKind.Expression, sw.On.Source);
        }
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
                bool big = (type.Order != KsyOrder.Default ? type.Order : type.Width > 1 ? OrderOf(scope, budget) : KsyOrder.Default) == KsyOrder.Big;
                string text;
                if (type.Kind == KsyKind.Float)
                {
                    double real = type.Width == 4
                        ? (big ? BinaryPrimitives.ReadSingleBigEndian(raw) : BinaryPrimitives.ReadSingleLittleEndian(raw))
                        : (big ? BinaryPrimitives.ReadDoubleBigEndian(raw) : BinaryPrimitives.ReadDoubleLittleEndian(raw));
                    value = real;
                    text = (type.Width == 4 ? ((float)real).ToString("R", CultureInfo.InvariantCulture) : real.ToString("R", CultureInfo.InvariantCulture));
                }
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
            case KsyKind.Bits:
            {
                int need = (bit + type.Width + 7) / 8;
                if (need > room) return Fail(attr, name, pos, need, end, KsyErrorKind.OutOfFile);
                var raw = Read(pos, need);
                bool little = (type.Order != KsyOrder.Default ? type.Order : scope.Def.BitEndian) == KsyOrder.Little;
                ulong u = 0;
                for (int k = 0; k < type.Width; k++)
                {
                    int at = bit + k;
                    ulong b = (ulong)((raw[at / 8] >> (little ? at % 8 : 7 - at % 8)) & 1);
                    u = little ? u | (b << k) : (u << 1) | b;
                }
                if (type.Width == 1)
                {
                    value = u != 0;
                    return new KsyNode(name, pos, need, u != 0 ? "true" : "false", label);
                }
                long bits = (long)u;
                value = bits;
                string shown = u.ToString(CultureInfo.InvariantCulture);
                if (attr.Enum is { } be) shown = _template.Enums[be].TryGetValue(bits, out var bitLabel) ? bitLabel + " (" + shown + ")" : shown;
                return new KsyNode(name, pos, need, shown, label);
            }
            case KsyKind.Strz:
            {
                long limit = Math.Min(room, _limits.MaxStringBytes + 1L);
                int zero = IndexOfZero(pos, limit);
                if (zero < 0) return Fail(attr, name, pos, limit, end, room <= _limits.MaxStringBytes ? KsyErrorKind.OutOfFile : KsyErrorKind.LimitSize);
                string decoded = Decode(attr.Encoding!, Read(pos, zero));
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
            return new KsyNode(name, pos, size, type: def.Name, expand: () => Layout(sub, NewBudget(), true));
        }
        var child = new KsyScope(def, parent, pos, parent.IoBase, end, parent.Depth + 1);
        value = child;
        if (attr.SizeEos) return new KsyNode(name, pos, end - pos, type: def.Name, expand: () => Layout(child, NewBudget(), true));
        // Without a size the extent of the structure is where its last field ends; the fields are walked and dropped, and read again when the node is opened.
        var fields = Layout(child, budget);
        if (fields.Count > 0 && fields[^1].Error is { } error)
            return new KsyNode(name, pos, fields[^1].End - pos, error: error);
        long length = fields.Count == 0 ? 0 : fields[^1].End - pos;
        return new KsyNode(name, pos, length, type: def.Name, expand: () => Layout(child, NewBudget(), true));
    }

    private byte[] Read(long offset, int length)
    {
        var buffer = new byte[length];
        int got = _data.Read(offset, buffer);
        return got == length ? buffer : buffer.AsSpan(0, got).ToArray();
    }

    /// <summary>The distance of the first zero byte in the <paramref name="limit"/> bytes from <paramref name="offset"/>, read a small piece at a time; -1 when there is none.</summary>
    private int IndexOfZero(long offset, long limit)
    {
        const int piece = 256;
        for (long done = 0; done < limit; done += piece)
        {
            var chunk = Read(offset + done, (int)Math.Min(piece, limit - done));
            int zero = Array.IndexOf(chunk, (byte)0);
            if (zero >= 0) return (int)(done + zero);
            if (chunk.Length == 0) break;
        }
        return -1;
    }

    private static string Preview(byte[] bytes) => Convert.ToHexString(bytes, 0, Math.Min(bytes.Length, 16));

    /// <summary>The string in quotes; a control character is written as an escape so that it cannot spoil the row that shows it.</summary>
    private static string Quote(string s)
    {
        if (!s.Any(char.IsControl)) return "\"" + s + "\"";
        var sb = new StringBuilder(s.Length + 8).Append('"');
        foreach (char c in s)
            sb.Append(c switch
            {
                '\0' => "\\0",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(c) => "\\x" + ((int)c).ToString("X2", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        return sb.Append('"').ToString();
    }

    private static string Decode(string encoding, ReadOnlySpan<byte> bytes) => encoding switch
    {
        "UTF-8" => Encoding.UTF8.GetString(bytes),
        "ASCII" => Encoding.ASCII.GetString(bytes),
        "ISO-8859-1" => Encoding.Latin1.GetString(bytes),
        "UTF-16LE" => Encoding.Unicode.GetString(bytes),
        _ => Encoding.BigEndianUnicode.GetString(bytes),
    };
}

namespace NksHub.Textida.Core.Hex.Ksy;

/// <summary>What is wrong with a template or with a file read through it. The text is built from the kind by the caller, so it can be localised.</summary>
public enum KsyErrorKind
{
    /// <summary>The YAML is malformed; the subject is the reason.</summary>
    Syntax,
    /// <summary>The construct is valid Kaitai but not part of the subset; the subject names it (<c>terminator</c>, <c>valid</c>, <c>process zlib</c>, ...).</summary>
    Unsupported,
    /// <summary>A key or value is of the wrong shape; the subject names the key.</summary>
    Invalid,
    UnknownType,
    UnknownField,
    UnknownEnum,
    Duplicate,
    /// <summary>A field extends past the end of the file or of its parent.</summary>
    OutOfFile,
    /// <summary>The bytes differ from <c>contents</c>.</summary>
    Contents,
    LimitDepth,
    LimitRepeat,
    LimitSize,
    LimitWork,
    /// <summary>An expression could not be computed (division by zero, a missing field, a wrong kind of value); the subject is the expression or the field.</summary>
    Expression,
    /// <summary>Reading one node took longer than the time limit.</summary>
    LimitTime,
}

/// <summary>An error with the 1-based line of the template it belongs to (0 when no line applies).</summary>
public sealed record KsyError(KsyErrorKind Kind, int Line, string Subject);

public enum KsyKind { Int, Float, Str, Strz, Bytes, User, Switch, Bits }

public enum KsyOrder { Default, Little, Big }

/// <summary>The type of a field: <see cref="Width"/> is in bytes for numbers and in bits for <see cref="KsyKind.Bits"/>, where <see cref="Order"/> is the bit order.</summary>
public sealed record KsyType(KsyKind Kind, int Width = 0, bool Signed = false, KsyOrder Order = KsyOrder.Default, string? UserName = null);

public enum KsyRepeat { None, Eos, Expr, Until }

/// <summary>One branch of <c>switch-on</c>; a null <see cref="Key"/> is the default branch <c>_</c>.</summary>
public sealed record KsyCase(KsyExpr? Key, KsyType Type);

public sealed record KsySwitch(KsyExpr On, IReadOnlyList<KsyCase> Cases);

/// <summary>The byte-wise transforms of <c>process</c> that the subset has: <c>xor(key)</c> with a one-byte key, <c>rol(bits)</c> and <c>ror(bits)</c> with 0 to 7 bits.</summary>
public enum KsyProcessKind { Xor, Rol, Ror }

/// <summary>A <c>process</c> of a field: the bytes of the file are turned back through it before they are read as a string or kept as bytes. <see cref="Argument"/> is an expression and is computed when the field is read.</summary>
public sealed record KsyProcess(KsyProcessKind Kind, KsyExpr Argument);

/// <summary>Byte order chosen by the data: <c>meta/endian</c> with <c>switch-on</c>; the order is computed when the first multi-byte number of the type is read.</summary>
public sealed record KsyEndianSwitch(KsyExpr On, IReadOnlyList<(KsyExpr Key, KsyOrder Order)> Cases);

/// <summary>A field of <c>seq</c> or an instance. <see cref="Pos"/> and <see cref="Value"/> belong to instances only; a type of kind <see cref="KsyKind.Switch"/> is chosen by <see cref="Switch"/> when the field is read.</summary>
public sealed record KsyAttr
{
    public required string Id { get; init; }
    public required int Line { get; init; }
    public required KsyType Type { get; init; }
    public KsyExpr? Size { get; init; }
    public bool SizeEos { get; init; }
    public KsyRepeat Repeat { get; init; }
    public KsyExpr? RepeatCount { get; init; }
    public KsyExpr? RepeatUntil { get; init; }
    public KsyExpr? If { get; init; }
    public KsyExpr? Pos { get; init; }
    /// <summary>The stream <see cref="Pos"/> counts in (<c>io: _root._io</c>); null means the stream of the type the instance belongs to.</summary>
    public KsyExpr? Io { get; init; }
    public KsyExpr? Value { get; init; }
    public KsySwitch? Switch { get; init; }
    public byte[]? Contents { get; init; }
    /// <summary>Applied to the bytes of a string, of raw bytes or of <see cref="Contents"/>; the node still covers the bytes as they are in the file.</summary>
    public KsyProcess? Process { get; init; }
    public string? Enum { get; init; }
    public string? Encoding { get; init; }
}

public sealed class KsyTypeDef
{
    public required string Name { get; init; }
    public required KsyOrder Endian { get; init; }
    /// <summary>Order of the bits of a bit-sized field; <see cref="KsyOrder.Default"/> means big-endian.</summary>
    public KsyOrder BitEndian { get; init; }
    /// <summary>True when <c>meta/endian</c> is a <c>switch-on</c>; <see cref="EndianSwitch"/> is set once the type is read.</summary>
    public bool HasEndianSwitch { get; init; }
    public KsyEndianSwitch? EndianSwitch { get; set; }
    public List<KsyAttr> Seq { get; } = [];
    public List<KsyAttr> Instances { get; } = [];
}

public sealed class KsyTemplate
{
    public required string Id { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<string> Extensions { get; init; } = [];
    public required KsyTypeDef Root { get; init; }
    public Dictionary<string, KsyTypeDef> Types { get; } = [];
    /// <summary>The enums by name; an enum declared inside a type is keyed <c>type::name</c>.</summary>
    public Dictionary<string, Dictionary<long, string>> Enums { get; } = [];

    /// <summary>The constructs the template uses, as named in <see cref="KsyParser.Constructs"/>.</summary>
    public IReadOnlyList<string> Constructs { get; set; } = [];

    /// <summary>The bytes of the first field when it is <c>contents</c> (also when the first field is a structure that starts with them): the magic number that tells the file type.</summary>
    public byte[]? Magic => MagicOf(Root, 0);

    private byte[]? MagicOf(KsyTypeDef def, int depth)
    {
        if (depth > 4 || def.Seq.Count == 0) return null;
        var first = def.Seq[0];
        if (first.Contents is { } contents) return contents;
        return first is { If: null, Type.Kind: KsyKind.User } ? MagicOf(Types[first.Type.UserName!], depth + 1) : null;
    }

    private readonly Dictionary<KsyTypeDef, long?> _sizes = [];

    /// <summary>
    /// The number of bytes each element of the field takes when the template alone tells it (numbers, fixed bytes, a constant <c>size</c>, a structure of such fields), else null.
    /// An array of such elements is not walked: where it ends and where its elements are follows from the count.
    /// </summary>
    public long? ElementSize(KsyAttr attr)
    {
        lock (_sizes) return Element(attr, 0, true);
    }

    private long? Element(KsyAttr a, int depth, bool top = false)
    {
        // the condition of the field itself is no matter to its elements; that of a field inside a structure changes how long the structure is
        if ((a.If is not null && !top) || a.Switch is not null || depth > 64) return null;
        if (a.Contents is { } contents) return contents.Length;
        switch (a.Type.Kind)
        {
            case KsyKind.Int or KsyKind.Float:
                return a.Type.Width;
            case KsyKind.Bytes or KsyKind.Str:
                return Constant(a.Size);
            case KsyKind.User:
                if (a.SizeEos) return null;
                return a.Size is not null ? Constant(a.Size) : TypeSize(Types[a.Type.UserName!], depth + 1);
            default:
                return null;
        }
    }

    private static long? Constant(KsyExpr? e) => e is not null && e.TryConstant(out long n) && n >= 0 ? n : null;

    private long? TypeSize(KsyTypeDef def, int depth)
    {
        if (_sizes.TryGetValue(def, out var known)) return known;
        _sizes[def] = null; // a type that contains itself has no fixed size
        long total = 0;
        foreach (var a in def.Seq)
        {
            if (Element(a, depth) is not { } one) return null;
            long count = a.Repeat switch { KsyRepeat.None => 1, KsyRepeat.Expr when Constant(a.RepeatCount) is { } n => n, _ => -1 };
            if (count < 0) return null;
            try { total = checked(total + checked(one * count)); }
            catch (OverflowException) { return null; }
        }
        return _sizes[def] = total;
    }

    /// <summary>The key in <see cref="Enums"/> that the name <paramref name="name"/> means inside <paramref name="def"/>: the enum of the type itself, else one of the root; a name with <c>::</c> is a key already.</summary>
    public string? ResolveEnum(KsyTypeDef def, string name)
    {
        if (name.Contains("::")) return Enums.ContainsKey(name) ? name : null;
        if (!ReferenceEquals(def, Root) && Enums.ContainsKey(def.Name + "::" + name)) return def.Name + "::" + name;
        return Enums.ContainsKey(name) ? name : null;
    }
}

namespace NksHub.Textida.Core.Hex.Ksy;

/// <summary>What is wrong with a template or with a file read through it. The text is built from the kind by the caller, so it can be localised.</summary>
public enum KsyErrorKind
{
    /// <summary>The YAML is malformed; the subject is the reason.</summary>
    Syntax,
    /// <summary>The construct is valid Kaitai but not part of the subset; the subject names it (<c>process</c>, <c>if</c>, <c>switch-on</c>, ...).</summary>
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
}

/// <summary>An error with the 1-based line of the template it belongs to (0 when no line applies).</summary>
public sealed record KsyError(KsyErrorKind Kind, int Line, string Subject);

public enum KsyKind { Int, Float, Str, Strz, Bytes, User, Switch }

public enum KsyOrder { Default, Little, Big }

/// <summary>The type of a field: <see cref="Width"/> is in bytes for numbers.</summary>
public sealed record KsyType(KsyKind Kind, int Width = 0, bool Signed = false, KsyOrder Order = KsyOrder.Default, string? UserName = null);

public enum KsyRepeat { None, Eos, Expr, Until }

/// <summary>One branch of <c>switch-on</c>; a null <see cref="Key"/> is the default branch <c>_</c>.</summary>
public sealed record KsyCase(KsyExpr? Key, KsyType Type);

public sealed record KsySwitch(KsyExpr On, IReadOnlyList<KsyCase> Cases);

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
    public KsyExpr? Value { get; init; }
    public KsySwitch? Switch { get; init; }
    public byte[]? Contents { get; init; }
    public string? Enum { get; init; }
    public string? Encoding { get; init; }
}

public sealed class KsyTypeDef
{
    public required string Name { get; init; }
    public required KsyOrder Endian { get; init; }
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
    public Dictionary<string, Dictionary<long, string>> Enums { get; } = [];

    /// <summary>The bytes of the first field when it is <c>contents</c>: the magic number that tells the file type.</summary>
    public byte[]? Magic => Root.Seq.Count > 0 ? Root.Seq[0].Contents : null;
}

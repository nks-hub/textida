using System.Globalization;
using System.Text;
using NksHub.Textida.Core.Data;

namespace NksHub.Textida.Core.Hex.Ksy;

/// <summary>
/// Reads the subset of Kaitai Struct <c>.ksy</c> that <see cref="KsyReader"/> can interpret: <c>meta</c> (<c>id</c>, <c>endian</c> fixed or with <c>switch-on</c>, <c>bit-endian</c>, <c>encoding</c>, <c>file-extension</c>, <c>title</c>),
/// <c>seq</c>, <c>types</c>, <c>enums</c> (also inside a type), <c>instances</c> (<c>pos</c> or <c>value</c>), integers, floats, bit-sized integers <c>b1</c> to <c>b64</c>, <c>str</c>, <c>strz</c>, raw bytes,
/// <c>size</c>, <c>size-eos</c>, <c>repeat</c> (<c>eos</c>, <c>expr</c>, <c>until</c>; not for bit-sized fields), <c>if</c>, <c>switch-on</c> with <c>cases</c>, <c>contents</c> and <c>enum</c>,
/// and <c>process</c> (<c>xor</c>, <c>rol</c>, <c>ror</c>) on a string or on raw bytes.
/// <c>size</c>, <c>repeat-expr</c>, <c>repeat-until</c>, <c>if</c>, <c>pos</c>, <c>value</c>, <c>switch-on</c> and the argument of <c>process</c> are expressions
/// (<see cref="KsyExpr"/>); a name in one must be an earlier field or an instance of the type. The rest (<c>process: zlib</c>, a <c>process</c> of a structure, ...) is refused by name.
/// </summary>
public static class KsyParser
{
    /// <summary>Largest template read. Placeholder, OPEN: the real limit is an input decision the owner has not made (P8.10.3).</summary>
    public const int MaxTemplateChars = 256 * 1024;

    /// <summary>
    /// The constructs the subset has, named the way <see cref="KsyTemplate.Constructs"/> and the list of pinned templates name them (P8.10.5). A template that uses a construct not listed here
    /// cannot be parsed, so the list of a pinned template can only be a part of this one.
    /// </summary>
    public static IReadOnlyList<string> Constructs { get; } =
    [
        "meta", "meta.endian", "meta.endian-switch", "meta.bit-endian", "meta.encoding", "meta.file-extension",
        "seq", "types", "enums", "enums.nested", "instances.pos", "instances.value", "instances.io",
        "int", "float", "bits", "str", "strz", "bytes", "user-type", "switch-on",
        "size", "size-eos", "repeat.eos", "repeat.expr", "repeat.until", "if", "contents", "enum",
        "expr", "expr.float", "expr.enum",
    ];

    /// <summary>
    /// Constructs the subset has that no pinned template needs, so they are not in <see cref="Constructs"/> (that list is the union of the pinned templates, P8.10.1). A template that uses one of
    /// them names it in <see cref="KsyTemplate.Constructs"/>; each has a sample of its own in the tests (P8.10.4).
    /// </summary>
    public static IReadOnlyList<string> OtherConstructs { get; } = ["process.xor", "process.rol", "process.ror"];

    private const int MaxYamlDepth = 32;

    public static bool TryParse(string text, out KsyTemplate? template, out KsyError? error)
    {
        template = null;
        if (text.Length > MaxTemplateChars)
        {
            error = new KsyError(KsyErrorKind.LimitSize, 0, "template");
            return false;
        }
        try
        {
            if (!YamlDataReader.TryParse(text, MaxYamlDepth, out var root, out var syntax))
                throw new Fail(new KsyError(KsyErrorKind.Syntax, LineOf(text, syntax!.Start), syntax.Message));
            template = new Builder(text).Build(root!);
            error = null;
            return true;
        }
        catch (Fail f)
        {
            error = f.Error;
            return false;
        }
    }

    private static int LineOf(string text, int offset)
    {
        int line = 1;
        for (int i = 0; i < offset && i < text.Length; i++) if (text[i] == '\n') line++;
        return line;
    }

    private sealed class Fail(KsyError error) : Exception { public KsyError Error { get; } = error; }

    private sealed class Builder(string text)
    {
        private static readonly string[] AttrKeys = ["id", "type", "size", "size-eos", "repeat", "repeat-expr", "repeat-until", "if", "contents", "enum", "encoding", "process", "doc", "doc-ref"];
        private static readonly string[] InstanceKeys = ["type", "size", "size-eos", "repeat", "repeat-expr", "repeat-until", "if", "pos", "io", "value", "contents", "enum", "encoding", "process", "doc", "doc-ref"];
        private static readonly string[] MetaKeys = ["id", "title", "endian", "bit-endian", "encoding", "file-extension", "license", "ks-version", "ks-debug", "tags", "application", "xref"];
        private static readonly string[] Encodings = ["UTF-8", "ASCII", "ISO-8859-1", "UTF-16LE", "UTF-16BE"];

        private KsyTemplate _t = null!;
        private string? _encoding;
        private readonly Dictionary<KsyTypeDef, List<DataMember>> _declared = [];
        private readonly SortedSet<string> _uses = new(StringComparer.Ordinal);
        private readonly List<(KsyTypeDef Def, DataNode Switch)> _endianSwitches = [];

        private void Use(string construct) => _uses.Add(construct);

        private Fail Err(int offset, KsyErrorKind kind, string subject) => new(new KsyError(kind, LineOf(text, offset), subject));

        private static bool IsId(string s) => s.Length > 0 && s[0] is >= 'a' and <= 'z' && s.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

        public KsyTemplate Build(DataNode doc)
        {
            if (doc.Kind != DataKind.Object) throw Err(doc.Start, KsyErrorKind.Invalid, "(root)");
            foreach (var m in doc.Members)
                if (m.Name is not ("meta" or "seq" or "types" or "enums" or "instances" or "doc" or "doc-ref")) throw Err(m.NameStart, KsyErrorKind.Unsupported, m.Name);

            Use("meta");
            var meta = doc.Get("meta") ?? throw Err(doc.Start, KsyErrorKind.Invalid, "meta");
            if (meta.Kind != DataKind.Object) throw Err(meta.Start, KsyErrorKind.Invalid, "meta");
            foreach (var m in meta.Members)
                if (!MetaKeys.Contains(m.Name)) throw Err(m.NameStart, KsyErrorKind.Unsupported, "meta/" + m.Name);
            var idNode = meta.Get("id");
            if (idNode is not { Kind: DataKind.String } || !IsId(idNode.Text)) throw Err(idNode?.Start ?? meta.Start, KsyErrorKind.Invalid, "meta/id");
            var endian = Endian(meta, KsyOrder.Default, out var rootSwitch);
            var bitEndian = BitEndian(meta, KsyOrder.Default);
            _encoding = Encoding(meta.Get("encoding"));
            if (_encoding is not null) Use("meta.encoding");
            if (meta.Get("file-extension") is not null) Use("meta.file-extension");

            _t = new KsyTemplate
            {
                Id = idNode.Text,
                Title = meta.Get("title") is { Kind: DataKind.String } title ? title.Text : null,
                Extensions = Extensions(meta.Get("file-extension")),
                Root = new KsyTypeDef { Name = idNode.Text, Endian = endian, BitEndian = bitEndian, HasEndianSwitch = rootSwitch is not null },
            };
            if (rootSwitch is not null) _endianSwitches.Add((_t.Root, rootSwitch));

            if (doc.Get("enums") is { } enums)
            {
                ReadEnums(enums, "");
                Use("enums");
            }
            var typeNodes = new List<(KsyTypeDef Def, DataNode Node)>();
            if (doc.Get("types") is { } types)
            {
                if (types.Kind != DataKind.Object) throw Err(types.Start, KsyErrorKind.Invalid, "types");
                foreach (var m in types.Members)
                {
                    if (!IsId(m.Name)) throw Err(m.NameStart, KsyErrorKind.Invalid, m.Name);
                    if (m.Value.Kind != DataKind.Object) throw Err(m.Value.Start, KsyErrorKind.Invalid, m.Name);
                    var tm = m.Value.Get("meta") is { Kind: DataKind.Object } typeMeta ? typeMeta : null;
                    DataNode? typeSwitch = null;
                    var def = new KsyTypeDef
                    {
                        Name = m.Name,
                        Endian = tm is null ? endian : Endian(tm, endian, out typeSwitch),
                        BitEndian = tm is null ? bitEndian : BitEndian(tm, bitEndian),
                        HasEndianSwitch = typeSwitch is not null,
                    };
                    if (typeSwitch is not null) _endianSwitches.Add((def, typeSwitch));
                    _t.Types[m.Name] = def;
                    typeNodes.Add((def, m.Value));
                    Use("types");
                }
            }
            foreach (var (def, node) in typeNodes)
            {
                foreach (var m in node.Members)
                    if (m.Name is not ("seq" or "meta" or "instances" or "enums" or "doc" or "doc-ref")) throw Err(m.NameStart, KsyErrorKind.Unsupported, m.Name);
                if (node.Get("enums") is { } typeEnums)
                {
                    ReadEnums(typeEnums, def.Name + "::");
                    Use("enums.nested");
                }
                Declare(def, node.Get("instances"));
            }
            Declare(_t.Root, doc.Get("instances"));
            foreach (var (def, node) in typeNodes) ReadSeq(def, node.Get("seq"));
            ReadSeq(_t.Root, doc.Get("seq"));
            if (_t.Root.Seq.Count == 0) throw Err(doc.Start, KsyErrorKind.Invalid, "seq");
            Use("seq");
            foreach (var (def, _) in typeNodes) ReadInstances(def);
            ReadInstances(_t.Root);
            foreach (var (def, node) in _endianSwitches) def.EndianSwitch = ReadEndianSwitch(def, node);
            _t.Constructs = [.. _uses];
            return _t;
        }

        /// <summary>Notes the names of the instances of a type, so that expressions can refer to them before they are read.</summary>
        private void Declare(KsyTypeDef def, DataNode? instances)
        {
            if (instances is null) return;
            if (instances.Kind != DataKind.Object) throw Err(instances.Start, KsyErrorKind.Invalid, "instances");
            foreach (var m in instances.Members)
            {
                if (!IsId(m.Name)) throw Err(m.NameStart, KsyErrorKind.Invalid, m.Name);
                if (m.Value.Kind != DataKind.Object) throw Err(m.Value.Start, KsyErrorKind.Invalid, m.Name);
            }
            _declared[def] = instances.Members;
        }

        private void ReadInstances(KsyTypeDef def)
        {
            if (!_declared.TryGetValue(def, out var members)) return;
            foreach (var m in members)
            {
                if (def.Seq.Any(a => a.Id == m.Name)) throw Err(m.NameStart, KsyErrorKind.Duplicate, m.Name);
                def.Instances.Add(ReadAttr(def, m.Value, m.Name));
            }
        }

        /// <summary>The byte order <c>meta</c> names, or <paramref name="inherited"/>; a <c>switch-on</c> is returned as <paramref name="endianSwitch"/> and read once the fields of the type are known.</summary>
        private KsyOrder Endian(DataNode meta, KsyOrder inherited, out DataNode? endianSwitch)
        {
            endianSwitch = null;
            if (meta.Get("endian") is not { } e) return inherited;
            if (e.Kind == DataKind.Object)
            {
                endianSwitch = e;
                Use("meta.endian-switch");
                return KsyOrder.Default;
            }
            Use("meta.endian");
            return e is { Kind: DataKind.String } ? e.Text switch
            {
                "le" => KsyOrder.Little,
                "be" => KsyOrder.Big,
                _ => throw Err(e.Start, KsyErrorKind.Invalid, "endian"),
            } : throw Err(e.Start, KsyErrorKind.Invalid, "endian");
        }

        private KsyOrder BitEndian(DataNode meta, KsyOrder inherited)
        {
            if (meta.Get("bit-endian") is not { } e) return inherited;
            Use("meta.bit-endian");
            return e is { Kind: DataKind.String } ? e.Text switch
            {
                "le" => KsyOrder.Little,
                "be" => KsyOrder.Big,
                _ => throw Err(e.Start, KsyErrorKind.Invalid, "bit-endian"),
            } : throw Err(e.Start, KsyErrorKind.Invalid, "bit-endian");
        }

        private KsyEndianSwitch ReadEndianSwitch(KsyTypeDef def, DataNode n)
        {
            foreach (var m in n.Members)
                if (m.Name is not ("switch-on" or "cases")) throw Err(m.NameStart, KsyErrorKind.Unsupported, "endian/" + m.Name);
            var on = n.Get("switch-on") ?? throw Err(n.Start, KsyErrorKind.Invalid, "switch-on");
            if (n.Get("cases") is not { Kind: DataKind.Object, Members.Count: > 0 } cases) throw Err(n.Start, KsyErrorKind.Invalid, "cases");
            var branches = new List<(KsyExpr, KsyOrder)>();
            foreach (var c in cases.Members)
            {
                var key = Expr(def, c.Name, c.NameStart, "cases");
                if (key.UsesContext) throw Err(c.NameStart, KsyErrorKind.Invalid, "cases");
                var order = c.Value is { Kind: DataKind.String } v && v.Text is "le" or "be" ? (c.Value.Text == "le" ? KsyOrder.Little : KsyOrder.Big) : throw Err(c.Value.Start, KsyErrorKind.Invalid, "endian");
                branches.Add((key, order));
            }
            return new KsyEndianSwitch(Expr(def, on, "switch-on"), branches);
        }

        private string? Encoding(DataNode? n)
        {
            if (n is null) return _encoding;
            if (n.Kind != DataKind.String) throw Err(n.Start, KsyErrorKind.Invalid, "encoding");
            string name = Encodings.FirstOrDefault(e => string.Equals(e, n.Text, StringComparison.OrdinalIgnoreCase)) ?? throw Err(n.Start, KsyErrorKind.Unsupported, "encoding " + n.Text);
            return name;
        }

        private IReadOnlyList<string> Extensions(DataNode? n)
        {
            if (n is null) return [];
            if (n.Kind == DataKind.String) return [n.Text];
            if (n.Kind == DataKind.Array && n.Items.All(i => i.Kind == DataKind.String)) return [.. n.Items.Select(i => i.Text)];
            throw Err(n.Start, KsyErrorKind.Invalid, "file-extension");
        }

        private void ReadEnums(DataNode enums, string prefix)
        {
            if (enums.Kind != DataKind.Object) throw Err(enums.Start, KsyErrorKind.Invalid, "enums");
            foreach (var e in enums.Members)
            {
                if (e.Value.Kind != DataKind.Object) throw Err(e.Value.Start, KsyErrorKind.Invalid, e.Name);
                var values = new Dictionary<long, string>();
                foreach (var v in e.Value.Members)
                {
                    if (!TryNumber(v.Name, out long key)) throw Err(v.NameStart, KsyErrorKind.Invalid, v.Name);
                    string? name = v.Value.Kind == DataKind.String ? v.Value.Text : v.Value.Kind == DataKind.Object && v.Value.Get("id") is { Kind: DataKind.String } id ? id.Text : null;
                    if (name is null) throw Err(v.Value.Start, KsyErrorKind.Invalid, v.Name);
                    if (!values.TryAdd(key, name)) throw Err(v.NameStart, KsyErrorKind.Duplicate, v.Name);
                }
                _t.Enums[prefix + e.Name] = values;
            }
        }

        private static bool TryNumber(string s, out long value) =>
            s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? long.TryParse(s.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)
                : long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

        private void ReadSeq(KsyTypeDef def, DataNode? seq)
        {
            if (seq is null) return;
            if (seq.Kind != DataKind.Array) throw Err(seq.Start, KsyErrorKind.Invalid, "seq");
            foreach (var item in seq.Items)
            {
                if (item.Kind != DataKind.Object) throw Err(item.Start, KsyErrorKind.Invalid, "seq");
                var attr = ReadAttr(def, item);
                if (def.Seq.Any(a => a.Id == attr.Id)) throw Err(item.Get("id")?.Start ?? item.Start, KsyErrorKind.Duplicate, attr.Id);
                def.Seq.Add(attr);
            }
        }

        private KsyAttr ReadAttr(KsyTypeDef def, DataNode n, string? instanceId = null)
        {
            foreach (var m in n.Members)
                if (!(instanceId is null ? AttrKeys : InstanceKeys).Contains(m.Name)) throw Err(m.NameStart, KsyErrorKind.Unsupported, m.Name);
            string id = instanceId ?? $"field_{def.Seq.Count + 1}";
            var ifExpr = n.Get("if") is { } ifNode ? Expr(def, ifNode, "if") : null;
            if (ifExpr is not null) Use("if");
            var posNode = n.Get("pos");
            var ioNode = n.Get("io");
            var valueNode = n.Get("value");
            if (ioNode is not null && posNode is null) throw Err(ioNode.Start, KsyErrorKind.Invalid, "io");
            if (ioNode is not null) Use("instances.io");
            if (instanceId is not null)
            {
                if ((posNode is null) == (valueNode is null)) throw Err(n.Start, KsyErrorKind.Invalid, "pos");
                Use(valueNode is not null ? "instances.value" : "instances.pos");
                if (valueNode is not null)
                {
                    if (n.Members.Any(m => m.Name is not ("value" or "if" or "doc" or "doc-ref"))) throw Err(valueNode.Start, KsyErrorKind.Invalid, "value");
                    return new KsyAttr { Id = id, Line = LineOf(text, n.Start), Type = new KsyType(KsyKind.Bytes), If = ifExpr, Value = Expr(def, valueNode, "value") };
                }
            }
            else if (n.Get("id") is { } idNode)
            {
                if (idNode.Kind != DataKind.String || !IsId(idNode.Text)) throw Err(idNode.Start, KsyErrorKind.Invalid, "id");
                id = idNode.Text;
            }
            var size = n.Get("size");
            var sizeEos = n.Get("size-eos");
            var contents = n.Get("contents");
            var typeNode = n.Get("type");
            bool eos = sizeEos is { Kind: DataKind.Boolean, Bool: true };
            if (sizeEos is not null && sizeEos.Kind != DataKind.Boolean) throw Err(sizeEos.Start, KsyErrorKind.Invalid, "size-eos");
            if (size is not null && eos) throw Err(size.Start, KsyErrorKind.Invalid, "size");
            if (size is not null) Use("size");
            if (eos) Use("size-eos");

            KsyType type;
            KsySwitch? sw = null;
            byte[]? bytes = null;
            if (contents is not null)
            {
                if (typeNode is not null || size is not null || eos) throw Err(contents.Start, KsyErrorKind.Invalid, "contents");
                bytes = Contents(contents);
                type = new KsyType(KsyKind.Bytes);
                Use("contents");
            }
            else if (typeNode is null)
            {
                if (size is null && !eos) throw Err(n.Start, KsyErrorKind.Invalid, "type");
                type = new KsyType(KsyKind.Bytes);
                Use("bytes");
            }
            else type = ReadType(def, typeNode, out sw);

            string? encoding = Encoding(n.Get("encoding"));
            if (sw is null)
            {
                if (type.Kind is KsyKind.Int or KsyKind.Float or KsyKind.Bits or KsyKind.Strz && (size is not null || eos)) throw Err((size ?? sizeEos)!.Start, KsyErrorKind.Invalid, "size");
                CheckType(def, type, typeNode, size is not null || eos, encoding);
            }
            else
            {
                // A value that no branch names is read as raw bytes when the field has a size, and is an error on the field otherwise.
                foreach (var c in sw.Cases) CheckType(def, c.Type, typeNode, size is not null || eos, encoding);
            }

            KsyProcess? process = null;
            if (n.Get("process") is { } processNode)
            {
                if (sw is not null) throw Err(processNode.Start, KsyErrorKind.Unsupported, "process of a switch-on type");
                if (type.Kind == KsyKind.User) throw Err(processNode.Start, KsyErrorKind.Unsupported, "process of a structure");
                if (type.Kind is not (KsyKind.Bytes or KsyKind.Str)) throw Err(processNode.Start, KsyErrorKind.Invalid, "process");
                process = ReadProcess(def, processNode);
            }

            string? enumName = null;
            if (n.Get("enum") is { } en)
            {
                if (en.Kind != DataKind.String || !(type.Kind == KsyKind.Int || type is { Kind: KsyKind.Bits, Width: > 1 })) throw Err(en.Start, KsyErrorKind.Invalid, "enum");
                enumName = _t.ResolveEnum(def, en.Text) ?? throw Err(en.Start, KsyErrorKind.UnknownEnum, en.Text);
                Use("enum");
            }

            var repeatNode = n.Get("repeat");
            var repeat = KsyRepeat.None;
            if (repeatNode is not null)
            {
                if (repeatNode.Kind != DataKind.String || repeatNode.Text is not ("eos" or "expr" or "until")) throw Err(repeatNode.Start, KsyErrorKind.Unsupported, "repeat/" + repeatNode.Text);
                repeat = repeatNode.Text switch { "eos" => KsyRepeat.Eos, "expr" => KsyRepeat.Expr, _ => KsyRepeat.Until };
                if (type.Kind == KsyKind.Bits) throw Err(repeatNode.Start, KsyErrorKind.Unsupported, "repeat of a bit-sized type");
                Use("repeat." + repeatNode.Text);
            }
            var countNode = n.Get("repeat-expr");
            if ((repeat == KsyRepeat.Expr) != (countNode is not null)) throw Err((countNode ?? repeatNode)?.Start ?? n.Start, KsyErrorKind.Invalid, "repeat-expr");
            var untilNode = n.Get("repeat-until");
            if ((repeat == KsyRepeat.Until) != (untilNode is not null)) throw Err((untilNode ?? repeatNode)?.Start ?? n.Start, KsyErrorKind.Invalid, "repeat-until");

            return new KsyAttr
            {
                Id = id,
                Line = LineOf(text, n.Start),
                Type = type,
                Switch = sw,
                If = ifExpr,
                Pos = posNode is null ? null : Expr(def, posNode, "pos"),
                Io = ioNode is null ? null : Expr(def, ioNode, "io"),
                Size = size is null ? null : Expr(def, size, "size"),
                SizeEos = eos,
                Repeat = repeat,
                RepeatCount = countNode is null ? null : Expr(def, countNode, "repeat-expr"),
                RepeatUntil = untilNode is null ? null : Expr(def, untilNode, "repeat-until"),
                Contents = bytes,
                Process = process,
                Enum = enumName,
                Encoding = encoding,
            };
        }

        /// <summary><c>xor(key)</c>, <c>rol(bits)</c> or <c>ror(bits)</c>; any other process (<c>zlib</c>, a custom one) is refused by its name.</summary>
        private KsyProcess ReadProcess(KsyTypeDef def, DataNode n)
        {
            if (n.Kind != DataKind.String) throw Err(n.Start, KsyErrorKind.Invalid, "process");
            string call = n.Text.Trim();
            int open = call.IndexOf('(', StringComparison.Ordinal);
            string name = open < 0 ? call : call[..open].TrimEnd();
            if (open < 0 || call[^1] != ')' || name is not ("xor" or "rol" or "ror")) throw Err(n.Start, KsyErrorKind.Unsupported, "process " + name);
            Use("process." + name);
            return new KsyProcess(name switch { "xor" => KsyProcessKind.Xor, "rol" => KsyProcessKind.Rol, _ => KsyProcessKind.Ror }, Expr(def, call[(open + 1)..^1], n.Start, "process"));
        }

        private void CheckType(KsyTypeDef def, KsyType type, DataNode? typeNode, bool sized, string? encoding)
        {
            if (type.Kind is KsyKind.Str && !sized) throw Err(typeNode!.Start, KsyErrorKind.Invalid, "size");
            if (type.Kind == KsyKind.Int && type.Width > 1 && type.Order == KsyOrder.Default && def.Endian == KsyOrder.Default && !def.HasEndianSwitch) throw Err(typeNode!.Start, KsyErrorKind.Invalid, "endian");
            if (type.Kind == KsyKind.Float && type.Order == KsyOrder.Default && def.Endian == KsyOrder.Default && !def.HasEndianSwitch) throw Err(typeNode!.Start, KsyErrorKind.Invalid, "endian");
            if (type.Kind is KsyKind.Str or KsyKind.Strz && encoding is null) throw Err(typeNode!.Start, KsyErrorKind.Invalid, "encoding");
        }

        private KsyType ReadType(KsyTypeDef def, DataNode n, out KsySwitch? sw)
        {
            sw = null;
            if (n.Kind == DataKind.Object)
            {
                sw = ReadSwitch(def, n);
                Use("switch-on");
                return new KsyType(KsyKind.Switch);
            }
            if (n.Kind != DataKind.String) throw Err(n.Start, KsyErrorKind.Invalid, "type");
            string s = n.Text;
            if (s == "str")
            {
                Use("str");
                return new KsyType(KsyKind.Str);
            }
            if (s == "strz")
            {
                Use("strz");
                return new KsyType(KsyKind.Strz);
            }
            if (TryNumeric(s, out var numeric))
            {
                Use(numeric.Kind == KsyKind.Float ? "float" : "int");
                return numeric;
            }
            if (TryBits(s, out var bits))
            {
                Use("bits");
                return bits;
            }
            if (_t.Types.ContainsKey(s))
            {
                Use("user-type");
                return new KsyType(KsyKind.User, UserName: s);
            }
            throw Err(n.Start, KsyErrorKind.UnknownType, s);
        }

        private KsySwitch ReadSwitch(KsyTypeDef def, DataNode n)
        {
            foreach (var m in n.Members)
                if (m.Name is not ("switch-on" or "cases")) throw Err(m.NameStart, KsyErrorKind.Unsupported, "type/" + m.Name);
            var on = n.Get("switch-on") ?? throw Err(n.Start, KsyErrorKind.Invalid, "switch-on");
            if (n.Get("cases") is not { Kind: DataKind.Object, Members.Count: > 0 } cases) throw Err(n.Start, KsyErrorKind.Invalid, "cases");
            var branches = new List<KsyCase>();
            foreach (var c in cases.Members)
            {
                KsyExpr? key = null;
                if (c.Name != "_")
                {
                    key = Expr(def, c.Name, c.NameStart, "cases");
                    if (key.UsesContext) throw Err(c.NameStart, KsyErrorKind.Invalid, "cases");
                }
                if (c.Value.Kind == DataKind.Object) throw Err(c.Value.Start, KsyErrorKind.Unsupported, "switch-on/type");
                var branch = ReadType(def, c.Value, out _);
                if (branch.Kind == KsyKind.Bits) throw Err(c.Value.Start, KsyErrorKind.Unsupported, "switch-on/bit-sized type");
                branches.Add(new KsyCase(key, branch));
            }
            return new KsySwitch(Expr(def, on, "switch-on"), branches);
        }

        private static bool TryNumeric(string s, out KsyType type)
        {
            type = null!;
            if (s.Length < 2 || s[0] is not ('u' or 's' or 'f') || !char.IsAsciiDigit(s[1])) return false;
            int width = s[1] - '0';
            string suffix = s[2..];
            var order = suffix switch { "" => KsyOrder.Default, "le" => KsyOrder.Little, "be" => KsyOrder.Big, _ => (KsyOrder)(-1) };
            if (order < 0 || (s[0] == 'f' ? width is not (4 or 8) : width is not (1 or 2 or 4 or 8)) || (width == 1 && order != KsyOrder.Default)) return false;
            type = new KsyType(s[0] == 'f' ? KsyKind.Float : KsyKind.Int, width, s[0] == 's', order);
            return true;
        }

        private static bool TryBits(string s, out KsyType type)
        {
            type = null!;
            if (s.Length < 2 || s[0] != 'b' || !char.IsAsciiDigit(s[1])) return false;
            int end = 1;
            while (end < s.Length && char.IsAsciiDigit(s[end])) end++;
            if (end - 1 > 2 || !int.TryParse(s.AsSpan(1, end - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int width) || width is < 1 or > 64) return false;
            var order = s[end..] switch { "" => KsyOrder.Default, "le" => KsyOrder.Little, "be" => KsyOrder.Big, _ => (KsyOrder)(-1) };
            if (order < 0) return false;
            type = new KsyType(KsyKind.Bits, width, Order: order);
            return true;
        }

        private KsyExpr Expr(KsyTypeDef def, DataNode n, string key)
        {
            string source = n.Kind switch
            {
                DataKind.String or DataKind.Number => n.Text,
                DataKind.Boolean => n.Bool ? "true" : "false",
                _ => throw Err(n.Start, KsyErrorKind.Invalid, key),
            };
            return Expr(def, source, n.Start, key);
        }

        /// <summary>Parses an expression and checks what it names: fields read before it, instances, enums and types must exist. What it computes is known only when a file is read.</summary>
        private KsyExpr Expr(KsyTypeDef def, string source, int at, string key)
        {
            if (!KsyExpr.TryParse(source, out var expr, out var bad))
                throw bad!.Unsupported ? Err(at, KsyErrorKind.Unsupported, "expression " + bad.Reason) : Err(at, KsyErrorKind.Syntax, $"{key}: {bad.Reason}");
            foreach (string name in expr!.Names)
                if (def.Seq.All(a => a.Id != name) && !(_declared.TryGetValue(def, out var members) && members.Any(m => m.Name == name))) throw Err(at, KsyErrorKind.UnknownField, name);
            foreach (var (enumName, member) in expr.EnumRefs)
            {
                if (_t.ResolveEnum(def, enumName) is not { } resolved) throw Err(at, KsyErrorKind.UnknownEnum, enumName);
                if (!_t.Enums[resolved].ContainsValue(member)) throw Err(at, KsyErrorKind.UnknownField, enumName + "::" + member);
            }
            foreach (string cast in expr.Casts)
                if (cast is not ("str" or "strz") && !TryNumeric(cast, out _) && !TryBits(cast, out _) && !_t.Types.ContainsKey(cast)) throw Err(at, KsyErrorKind.UnknownType, cast);
            Use("expr");
            if (expr.UsesFloat) Use("expr.float");
            if (expr.EnumRefs.Count > 0) Use("expr.enum");
            return expr;
        }

        private byte[] Contents(DataNode n)
        {
            var bytes = new List<byte>();
            foreach (var item in n.Kind == DataKind.Array ? n.Items : [n])
            {
                if (item.Kind == DataKind.String) bytes.AddRange(new UTF8Encoding(false).GetBytes(item.Text));
                else if (item.Kind == DataKind.Number && long.TryParse(item.Text, NumberStyles.None, CultureInfo.InvariantCulture, out long b) && b <= 255) bytes.Add((byte)b);
                else throw Err(item.Start, KsyErrorKind.Invalid, "contents");
            }
            if (bytes.Count == 0) throw Err(n.Start, KsyErrorKind.Invalid, "contents");
            return [.. bytes];
        }
    }
}

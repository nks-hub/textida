using System.Globalization;

namespace NksHub.Textida.Core.Hex.Ksy;

/// <summary>Why an expression was refused: <see cref="Unsupported"/> when it is valid Kaitai that the subset does not have, otherwise it is malformed.</summary>
public sealed record KsyExprError(bool Unsupported, string Reason);

/// <summary>Thrown while an expression is computed; the reader turns it into an error node. An empty subject is replaced by the source of the expression.</summary>
internal sealed class KsyExprException(KsyErrorKind kind, string subject) : Exception(subject)
{
    public KsyErrorKind Kind { get; } = kind;
    public string Subject { get; } = subject;
}

/// <summary>What an expression needs from the structure it is computed in.</summary>
internal interface IKsyExprHost
{
    /// <summary>Counts one step of work; throws when the budget is spent.</summary>
    void Tick();
    object? Name(string name);
    long EnumValue(string enumName, string member);
    /// <summary>A member of a value that is not a number, a string or a list: a structure or <c>_io</c>.</summary>
    object? Member(object? target, string name);
}

/// <summary>
/// The Kaitai Struct expression subset (P8.10.4), parsed once and computed on demand without running any code. Values are integers (64-bit, checked), booleans and strings.
/// Operators by falling precedence: unary <c>-</c> <c>not</c> <c>~</c>; <c>* / %</c>; <c>+ -</c> (also string concatenation); <c>&lt;&lt; &gt;&gt;</c>; <c>&lt; &lt;= &gt; &gt;=</c>; <c>== !=</c>;
/// <c>&amp;</c>; <c>^</c>; <c>|</c>; <c>and</c>; <c>or</c>; <c>?:</c>. Operands: decimal, <c>0x</c> and <c>0b</c> integers, quoted strings, <c>true</c>, <c>false</c>, field and instance names,
/// <c>_parent</c>, <c>_root</c>, <c>_index</c>, <c>_</c>, <c>_io.pos</c>, <c>_io.size</c>, <c>_io.eof</c>, <c>enum::member</c>, <c>a.b</c>, <c>list[i]</c>, <c>.as&lt;type&gt;</c> (no conversion),
/// <c>.to_i</c> and <c>.length</c> of a string, <c>.size</c>, <c>.first</c> and <c>.last</c> of a list. Floats, array literals, other methods and calls are refused by name.
/// </summary>
public sealed class KsyExpr
{
    private static readonly string[][] Levels = [["or"], ["and"], ["|"], ["^"], ["&"], ["==", "!="], ["<", "<=", ">", ">="], ["<<", ">>"], ["+", "-"], ["*", "/", "%"]];
    private static readonly string[] Specials = ["_", "_index", "_parent", "_root", "_io"];

    private readonly Node _root;

    private KsyExpr(string source, Node root, List<string> names, List<(string, string)> enums, List<string> casts, bool usesContext)
    {
        Source = source;
        _root = root;
        Names = names;
        EnumRefs = enums;
        Casts = casts;
        UsesContext = usesContext;
    }

    public string Source { get; }
    /// <summary>The first name of each reference to a field or an instance; the parser checks them against the type.</summary>
    public IReadOnlyList<string> Names { get; }
    public IReadOnlyList<(string Enum, string Member)> EnumRefs { get; }
    public IReadOnlyList<string> Casts { get; }
    /// <summary>True when the value depends on the data (any name, <c>_parent</c>, <c>_io</c>, ...); false for a constant.</summary>
    public bool UsesContext { get; }

    public static bool TryParse(string text, out KsyExpr? expr, out KsyExprError? error)
    {
        expr = null;
        var limits = KsyLimits.Default;
        if (text.Length > limits.MaxExprChars) { error = new KsyExprError(false, "too long"); return false; }
        try
        {
            var parser = new Parser(Lex(text), limits.MaxExprDepth);
            var root = parser.Parse();
            expr = new KsyExpr(text, root, parser.Names, parser.Enums, parser.Casts, parser.UsesContext);
            error = null;
            return true;
        }
        catch (ParseFail f)
        {
            error = new KsyExprError(f.Unsupported, f.Reason);
            return false;
        }
    }

    internal object? Eval(IKsyExprHost host)
    {
        try { return _root.Eval(host); }
        catch (KsyExprException e) when (e.Subject.Length == 0) { throw new KsyExprException(e.Kind, Source); }
        catch (ArithmeticException) { throw new KsyExprException(KsyErrorKind.Expression, Source); }
    }

    // ---- lexer ----

    private sealed class ParseFail(bool unsupported, string reason) : Exception(reason)
    {
        public bool Unsupported { get; } = unsupported;
        public string Reason { get; } = reason;
    }

    /// <summary>Kind: n number, s string, i identifier or word, o operator, e end.</summary>
    private readonly record struct Tok(char Kind, string Text, long Number = 0);

    private static List<Tok> Lex(string s)
    {
        var toks = new List<Tok>();
        for (int i = 0; i < s.Length;)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (char.IsAsciiDigit(c))
            {
                int j = i;
                long value;
                if (c == '0' && i + 1 < s.Length && s[i + 1] is 'x' or 'X' or 'b' or 'B')
                {
                    bool hex = s[i + 1] is 'x' or 'X';
                    j = i + 2;
                    while (j < s.Length && (hex ? char.IsAsciiHexDigit(s[j]) : s[j] is '0' or '1')) j++;
                    var digits = s.AsSpan(i + 2, j - i - 2);
                    value = 0;
                    if (digits.IsEmpty || !(hex ? long.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value) : TryBinary(digits, out value)) || value < 0)
                        throw new ParseFail(false, "bad number");
                }
                else
                {
                    while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
                    if (j < s.Length && s[j] == '.' && j + 1 < s.Length && char.IsAsciiDigit(s[j + 1])) throw new ParseFail(true, "float literal");
                    if (!long.TryParse(s.AsSpan(i, j - i), NumberStyles.None, CultureInfo.InvariantCulture, out value)) throw new ParseFail(false, "number too large");
                }
                if (j < s.Length && (char.IsAsciiLetter(s[j]) || s[j] == '_')) throw new ParseFail(false, "bad number");
                toks.Add(new Tok('n', s[i..j], value));
                i = j;
            }
            else if (char.IsAsciiLetter(c) || c == '_')
            {
                int j = i;
                while (j < s.Length && (char.IsAsciiLetterOrDigit(s[j]) || s[j] == '_')) j++;
                toks.Add(new Tok('i', s[i..j]));
                i = j;
            }
            else if (c is '\'' or '"')
            {
                var sb = new System.Text.StringBuilder();
                int j = i + 1;
                while (true)
                {
                    if (j >= s.Length) throw new ParseFail(false, "string is not closed");
                    if (s[j] == c) break;
                    if (s[j] == '\\' && j + 1 < s.Length)
                    {
                        j++;
                        sb.Append(s[j] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '0' => '\0', var other => other });
                    }
                    else sb.Append(s[j]);
                    j++;
                }
                toks.Add(new Tok('s', sb.ToString()));
                i = j + 1;
            }
            else
            {
                string two = i + 1 < s.Length ? s.Substring(i, 2) : "";
                if (two is "<<" or ">>" or "<=" or ">=" or "==" or "!=" or "::") { toks.Add(new Tok('o', two)); i += 2; }
                else if ("+-*/%<>&|^~()[].?:,".Contains(c)) { toks.Add(new Tok('o', c.ToString())); i++; }
                else throw new ParseFail(false, $"unexpected '{c}'");
            }
        }
        toks.Add(new Tok('e', ""));
        return toks;
    }

    private static bool TryBinary(ReadOnlySpan<char> digits, out long value)
    {
        value = 0;
        if (digits.Length > 63) return false;
        foreach (char d in digits) value = (value << 1) | (long)(d - '0');
        return true;
    }

    // ---- parser ----

    private sealed class Parser(List<Tok> toks, int maxDepth)
    {
        private int _p;
        private int _depth;

        public List<string> Names { get; } = [];
        public List<(string, string)> Enums { get; } = [];
        public List<string> Casts { get; } = [];
        public bool UsesContext { get; private set; }

        private Tok Cur => toks[_p];

        private bool Is(string text) => Cur.Kind is 'o' or 'i' && Cur.Text == text;

        private void Expect(string text)
        {
            if (!Is(text)) throw new ParseFail(false, $"'{text}' is expected");
            _p++;
        }

        private string Ident()
        {
            if (Cur.Kind != 'i') throw new ParseFail(false, "a name is expected");
            return toks[_p++].Text;
        }

        private void Enter()
        {
            if (++_depth > maxDepth) throw new ParseFail(false, "nested too deeply");
        }

        public Node Parse()
        {
            var node = Ternary();
            if (Cur.Kind != 'e') throw new ParseFail(false, $"unexpected '{Cur.Text}'");
            return node;
        }

        private Node Ternary()
        {
            Enter();
            var cond = Binary(0);
            if (Is("?"))
            {
                _p++;
                var a = Ternary();
                Expect(":");
                var b = Ternary();
                _depth--;
                return new Cond(cond, a, b);
            }
            _depth--;
            return cond;
        }

        private Node Binary(int level)
        {
            if (level == Levels.Length) return Unary();
            var left = Binary(level + 1);
            while (true)
            {
                string? op = Levels[level].FirstOrDefault(Is);
                if (op is null) return left;
                _p++;
                left = new Bin(op, left, Binary(level + 1));
            }
        }

        private Node Unary()
        {
            if (Is("-") || Is("not") || Is("~"))
            {
                Enter();
                string op = toks[_p++].Text;
                var x = Unary();
                _depth--;
                return new Un(op, x);
            }
            return Postfix();
        }

        private Node Postfix()
        {
            var n = Primary();
            while (true)
            {
                if (Is("."))
                {
                    _p++;
                    string name = Ident();
                    if (Is("(")) throw new ParseFail(true, $"call .{name}(...)");
                    if (name == "as" && Is("<"))
                    {
                        _p++;
                        Casts.Add(Ident());
                        Expect(">");
                        n = new Cast(n);
                        continue;
                    }
                    n = new Member(n, name);
                }
                else if (Is("["))
                {
                    _p++;
                    var index = Ternary();
                    Expect("]");
                    n = new Idx(n, index);
                }
                else return n;
            }
        }

        private Node Primary()
        {
            var t = Cur;
            switch (t.Kind)
            {
                case 'n': _p++; return new Lit(t.Number);
                case 's': _p++; return new Lit(t.Text);
                case 'i':
                    if (t.Text is "and" or "or" or "not") throw new ParseFail(false, $"unexpected '{t.Text}'");
                    _p++;
                    if (t.Text is "true" or "false") return new Lit(t.Text == "true");
                    if (Is("::"))
                    {
                        _p++;
                        string member = Ident();
                        Enums.Add((t.Text, member));
                        return new EnumRef(t.Text, member);
                    }
                    UsesContext = true;
                    if (!Specials.Contains(t.Text)) Names.Add(t.Text);
                    return new Name(t.Text);
                case 'o' when t.Text == "(":
                {
                    _p++;
                    var inner = Ternary();
                    Expect(")");
                    return inner;
                }
                case 'o' when t.Text == "[": throw new ParseFail(true, "array literal");
                case 'e': throw new ParseFail(false, "the expression ends too early");
                default: throw new ParseFail(false, $"unexpected '{t.Text}'");
            }
        }
    }

    // ---- values ----

    private static KsyExprException Bad() => new(KsyErrorKind.Expression, "");

    private static long Int(object? v) => v is long n ? n : throw Bad();

    private static bool Bool(object? v) => v is bool b ? b : throw Bad();

    private static bool Same(object? a, object? b) => (a, b) switch
    {
        (long x, long y) => x == y,
        (bool x, bool y) => x == y,
        (string x, string y) => x == y,
        _ => throw Bad(),
    };

    private static int Compare(object? a, object? b) => (a, b) switch
    {
        (long x, long y) => x.CompareTo(y),
        (string x, string y) => string.CompareOrdinal(x, y),
        _ => throw Bad(),
    };

    private abstract class Node
    {
        public object? Eval(IKsyExprHost host)
        {
            host.Tick();
            return Do(host);
        }

        protected abstract object? Do(IKsyExprHost host);
    }

    private sealed class Lit(object value) : Node { protected override object? Do(IKsyExprHost host) => value; }

    private sealed class EnumRef(string name, string member) : Node { protected override object? Do(IKsyExprHost host) => host.EnumValue(name, member); }

    private sealed class Name(string name) : Node { protected override object? Do(IKsyExprHost host) => host.Name(name); }

    private sealed class Cast(Node target) : Node { protected override object? Do(IKsyExprHost host) => target.Eval(host); }

    private sealed class Member(Node target, string name) : Node
    {
        protected override object? Do(IKsyExprHost host)
        {
            var t = target.Eval(host);
            switch (t, name)
            {
                case (string s, "length"): return (long)s.Length;
                case (string s, "to_i"): return long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v) ? v : throw Bad();
                case (long n, "to_i"): return n;
                case (bool b, "to_i"): return b ? 1L : 0L;
                case (List<object?> l, "size" or "length"): return (long)l.Count;
                case (List<object?> l, "first"): return l.Count > 0 ? l[0] : throw Bad();
                case (List<object?> l, "last"): return l.Count > 0 ? l[^1] : throw Bad();
                default: return host.Member(t, name);
            }
        }
    }

    private sealed class Idx(Node target, Node index) : Node
    {
        protected override object? Do(IKsyExprHost host)
        {
            if (target.Eval(host) is not List<object?> list) throw Bad();
            long i = Int(index.Eval(host));
            return i >= 0 && i < list.Count ? list[(int)i] : throw Bad();
        }
    }

    private sealed class Un(string op, Node x) : Node
    {
        protected override object? Do(IKsyExprHost host)
        {
            var v = x.Eval(host);
            return op switch { "-" => checked(-Int(v)), "~" => (object)~Int(v), _ => !Bool(v) };
        }
    }

    private sealed class Cond(Node cond, Node a, Node b) : Node
    {
        protected override object? Do(IKsyExprHost host) => Bool(cond.Eval(host)) ? a.Eval(host) : b.Eval(host);
    }

    private sealed class Bin(string op, Node l, Node r) : Node
    {
        protected override object? Do(IKsyExprHost host)
        {
            if (op == "and") return Bool(l.Eval(host)) && Bool(r.Eval(host));
            if (op == "or") return Bool(l.Eval(host)) || Bool(r.Eval(host));
            var a = l.Eval(host);
            var b = r.Eval(host);
            switch (op)
            {
                case "==": return Same(a, b);
                case "!=": return !Same(a, b);
                case "<": return Compare(a, b) < 0;
                case "<=": return Compare(a, b) <= 0;
                case ">": return Compare(a, b) > 0;
                case ">=": return Compare(a, b) >= 0;
                case "+": return a is string sa && b is string sb ? sa + sb : checked(Int(a) + Int(b));
                case "-": return checked(Int(a) - Int(b));
                case "*": return checked(Int(a) * Int(b));
                case "/": return checked(Int(a) / Int(b));
                case "%":
                {
                    long x = Int(a), y = Int(b);
                    long m = x % y;
                    return m != 0 && (m ^ y) < 0 ? m + y : m;
                }
                case "&": return Int(a) & Int(b);
                case "|": return Int(a) | Int(b);
                case "^": return Int(a) ^ Int(b);
                default:
                {
                    long shift = Int(b);
                    if (shift is < 0 or > 63) throw Bad();
                    return op == "<<" ? Int(a) << (int)shift : Int(a) >> (int)shift;
                }
            }
        }
    }
}

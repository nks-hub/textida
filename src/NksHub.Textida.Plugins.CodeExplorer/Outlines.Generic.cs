using System.Text.RegularExpressions;

namespace NksHub.Textida.Plugins.CodeExplorer;

/// <summary>
/// Outlines of the other languages: the text is masked like the others, then every declaration rule of the language is matched against it. A rule that opens a block
/// ("container") makes the symbols inside its braces its children. Python has no braces and nests by indentation.
/// </summary>
public static partial class Outlines
{
    private enum Place { Anywhere, InType, OutsideCallable }

    /// <summary>One declaration form. The pattern names the symbol in the group <c>n</c>, optionally the parameters in <c>p</c> and, when <paramref name="Kind"/> is null, the kind in <c>k</c>.</summary>
    private sealed record Rule(string? Kind, Regex Pattern, bool Container = false, Place Where = Place.Anywhere);

    private static readonly HashSet<string> MethodHosts = ["class", "interface", "struct", "trait", "impl", "enum", "union", "mixin", "extension"];
    private static readonly HashSet<string> Callables = ["function", "method", "fn"];
    private static readonly HashSet<string> Keywords = ["if", "for", "while", "switch", "catch", "return", "else", "do", "sizeof", "foreach", "using", "lock", "super", "this", "assert", "function", "new", "typeof"];

    private static Regex Re(string pattern, bool ignoreCase = false) =>
        new(pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None));

    private static readonly Rule[] JsRules =
    [
        new(null, Re(@"^[ \t]*(?:export[ \t]+)?(?:default[ \t]+)?(?:declare[ \t]+)?(?:abstract[ \t]+)?(?<k>class|interface|enum|namespace|type)[ \t]+(?<n>[A-Za-z_$][\w$]*)"), true),
        new("function", Re(@"^[ \t]*(?:export[ \t]+)?(?:default[ \t]+)?(?:declare[ \t]+)?(?:async[ \t]+)?function[ \t]*\*?[ \t]*(?<n>[A-Za-z_$][\w$]*)[ \t]*(?:<[^>(){};]*>)?[ \t]*\((?<p>[^()]*)\)"), true),
        new("function", Re(@"^[ \t]*(?:export[ \t]+)?(?:const|let|var)[ \t]+(?<n>[A-Za-z_$][\w$]*)[ \t]*(?::[^=\n]+)?=[ \t]*(?:async[ \t]+)?(?:\((?<p>[^()]*)\)|[A-Za-z_$][\w$]*)[ \t]*(?::[^=\n]+?)?[ \t]*=>"), true),
        new("method", Re(@"^[ \t]*(?:(?:public|private|protected|static|async|get|set|readonly|override|abstract)[ \t]+)*\*?[ \t]*(?<n>[A-Za-z_$#][\w$]*)[ \t]*(?:<[^>(){};]*>)?[ \t]*\((?<p>[^()]*)\)[ \t]*(?::[^{;=]+?)?[ \t]*(?=\{)"), true, Place.InType),
    ];

    private static readonly Rule[] RustRules =
    [
        new("function", Re(@"^[ \t]*(?:pub(?:\([^)\n]*\))?[ \t]+)?(?:(?:default|async|const|unsafe|extern)[ \t]+)*fn[ \t]+(?<n>[A-Za-z_]\w*)[ \t]*(?:<[^>{(]*>)?[ \t]*\((?<p>[^)]*)\)"), true),
        new(null, Re(@"^[ \t]*(?:pub(?:\([^)\n]*\))?[ \t]+)?(?<k>struct|enum|trait|union|mod|type)[ \t]+(?<n>[A-Za-z_]\w*)"), true),
        new("impl", Re(@"^[ \t]*(?:unsafe[ \t]+)?impl\b(?:<[^>]*>)?[ \t]*(?<n>[^{;]+?)[ \t\r\n]*(?=\{|\bwhere\b)"), true),
        new("macro", Re(@"^[ \t]*macro_rules![ \t]*(?<n>[A-Za-z_]\w*)")),
    ];

    private static readonly Rule[] DartRules =
    [
        new(null, Re(@"^[ \t]*(?:(?:abstract|base|final|sealed|interface|mixin)[ \t]+)*(?<k>class|mixin|enum|extension)[ \t]+(?<n>[A-Za-z_$]\w*)"), true),
        new("function", Re(@"^[ \t]*(?:(?:static|external|abstract|final|const|factory|late)[ \t]+)*(?:[A-Za-z_$][\w$<>,?.\[\] ]*?[ \t]+)?(?<n>[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)?)[ \t]*\((?<p>[^()]*)\)[ \t]*(?:async\*?|sync\*)?[ \t]*(?=\{|=>)"), true, Place.OutsideCallable),
    ];

    private static readonly Rule[] CRules =
    [
        new(null, Re(@"^[ \t]*(?:typedef[ \t]+)?(?<k>struct|union|enum|class|namespace)[ \t]+(?:class[ \t]+)?(?<n>[A-Za-z_]\w*)[ \t]*(?:final[ \t]*)?(?::[^{;]*)?(?=\s*\{)"), true),
        new("function", Re(@"^[ \t]*(?:[A-Za-z_][\w:<>,*&]*[ \t*&]+)+(?<n>[A-Za-z_~][\w:~]*)[ \t]*\((?<p>[^;(){}]*)\)(?:[ \t\r\n]*(?:const|noexcept|override|final))*[ \t\r\n]*(?=\{)"), true, Place.OutsideCallable),
        new("macro", Re(@"^[ \t]*#[ \t]*define[ \t]+(?<n>[A-Za-z_]\w*)")),
    ];

    private static readonly Rule[] ShellRules =
    [
        new("function", Re(@"^[ \t]*function[ \t]+(?<n>[\w:.-]+)")),
        new("function", Re(@"^[ \t]*(?<n>[A-Za-z_][\w:.-]*)[ \t]*\([ \t]*\)")),
    ];

    private static readonly Rule[] CMakeRules =
    [
        new(null, Re(@"^[ \t]*(?<k>function|macro)[ \t]*\([ \t]*(?<n>[^\s)]+)", ignoreCase: true)),
        new(null, Re(@"^[ \t]*(?<k>add_executable|add_library|add_custom_target|project|option)[ \t]*\([ \t]*(?<n>[^\s)]+)", ignoreCase: true)),
    ];

    private static readonly Rule[] MakeRules =
    [
        new("target", Re(@"^(?!\.)(?<n>[^\s:#=][^:#=\n]*?)[ \t]*:(?![:=])")),
        new("define", Re(@"^define[ \t]+(?<n>[A-Za-z_][\w.-]*)")),
    ];

    private static readonly Rule[] MesonRules =
    [
        new(null, Re(@"^[ \t]*(?:\w+[ \t]*=[ \t]*)?(?<k>executable|shared_library|static_library|both_libraries|library|custom_target|test|project|dependency|subdir)[ \t]*\([ \t]*'(?<n>[^'\n]*)'")),
    ];

    [GeneratedRegex(@"^(?<i>[ \t]*)(?:async[ \t]+)?(?<k>def|class)[ \t]+(?<n>[A-Za-z_]\w*)[ \t]*(?:\((?<p>[^)]*)\))?", RegexOptions.Multiline)]
    private static partial Regex PythonDeclaration();

    private static readonly MaskRules CStyle = new(true, false, true, false, false);
    private static readonly MaskRules Hash = new(false, true, false, false, false);

    /// <summary>The languages outlined by rules, by language mode id.</summary>
    private static readonly Dictionary<string, Func<string, List<Sym>>> Generic = new()
    {
        ["javascript"] = t => Braced(t, Mask(t, CStyle with { Quotes = "\"'`" }), JsRules),
        ["typescript"] = t => Braced(t, Mask(t, CStyle with { Quotes = "\"'`" }), JsRules),
        ["rust"] = t => Braced(t, Mask(t, CStyle with { Quotes = "\"" }), RustRules),
        ["dart"] = t => Braced(t, Mask(t, CStyle), DartRules),
        ["c"] = t => Braced(t, Mask(t, CStyle), CRules),
        ["cpp"] = t => Braced(t, Mask(t, CStyle), CRules),
        ["shell"] = t => Braced(t, Mask(t, Hash), ShellRules),
        ["cmake"] = t => Braced(t, Mask(t, Hash with { Quotes = "" }), CMakeRules),
        ["makefile"] = t => Braced(t, Mask(t, Hash with { Quotes = "" }), MakeRules),
        ["meson"] = t => Braced(t, Mask(t, Hash with { Quotes = "" }), MesonRules),
        ["python"] = Python,
    };

    private static List<Sym> RunPlan(Func<string, List<Sym>> plan, string text)
    {
        try { return plan(text); }
        catch (RegexMatchTimeoutException) { return []; } // a pathological line: no outline rather than a guessed one
    }

    private static List<Sym> Braced(string source, string masked, Rule[] rules)
    {
        var pairs = BracePairs(masked);
        var hits = new List<(int At, int Order, Rule Rule, Match Match)>();
        foreach (var rule in rules)
            foreach (Match m in rule.Pattern.Matches(masked)) hits.Add((m.Groups["n"].Index, hits.Count, rule, m));
        hits.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Order.CompareTo(b.Order));

        var result = new List<Sym>();
        var stack = new List<(int Symbol, int BodyEnd, string Kind)>();
        int last = -1;
        foreach (var (at, _, rule, m) in hits)
        {
            if (at == last) continue;
            while (stack.Count > 0 && stack[^1].BodyEnd < at) stack.RemoveAt(stack.Count - 1);
            string top = stack.Count > 0 ? stack[^1].Kind : "";
            if (rule.Where == Place.InType && !MethodHosts.Contains(top)) continue;
            if (rule.Where == Place.OutsideCallable && Callables.Contains(top)) continue;
            var n = m.Groups["n"];
            string name = Collapse(source.Substring(n.Index, n.Length), 400);
            if (Keywords.Contains(name)) continue;
            string kind = rule.Kind ?? m.Groups["k"].Value.ToLowerInvariant();
            if (kind is "function" && MethodHosts.Contains(top)) kind = "method";
            if (m.Groups["p"] is { Success: true } p) name += "(" + Collapse(source.Substring(p.Index, p.Length), 60) + ")";
            last = at;
            result.Add(new Sym(kind, name, n.Index, n.Index + n.Length, stack.Count > 0 ? stack[^1].Symbol : -1));
            if (!rule.Container) continue;
            int k = m.Index + m.Length;
            while (k < masked.Length && masked[k] is not ('{' or ';')) k++;
            if (k < masked.Length && masked[k] == '{' && pairs.TryGetValue(k, out int close)) stack.Add((result.Count - 1, close, kind));
        }
        return result;
    }

    // shortcut: a def under an `if` or `try` at a deeper indent than a preceding class is taken as its member; fine for outlines, upgrade with a real block parser
    private static List<Sym> Python(string source)
    {
        var masked = Mask(source, Hash with { Triple = true });
        var result = new List<Sym>();
        var stack = new List<(int Indent, int Symbol, string Kind)>();
        foreach (Match m in PythonDeclaration().Matches(masked))
        {
            int indent = m.Groups["i"].Length;
            while (stack.Count > 0 && stack[^1].Indent >= indent) stack.RemoveAt(stack.Count - 1);
            var n = m.Groups["n"];
            bool isClass = m.Groups["k"].Value == "class";
            string kind = isClass ? "class" : stack.Count > 0 && stack[^1].Kind == "class" ? "method" : "function";
            string name = isClass ? n.Value : n.Value + "(" + (m.Groups["p"] is { Success: true } p ? Collapse(p.Value, 60) : "") + ")";
            result.Add(new Sym(kind, name, n.Index, n.Index + n.Length, stack.Count > 0 ? stack[^1].Symbol : -1));
            stack.Add((indent, result.Count - 1, kind));
        }
        return result;
    }
}

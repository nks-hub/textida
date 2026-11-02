using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace NksHub.Textida.Core.Syntax;

/// <summary>A TextMate grammar converted to the grammar format of the editor (<see cref="Json"/>), with every construct that could not be carried over named in <see cref="Warnings"/>.</summary>
public sealed record TextMateImportResult(string Id, string Name, string Json, IReadOnlyList<string> Warnings);

public sealed class TextMateImportException(string message) : Exception(message);

/// <summary>
/// Reads a TextMate grammar (<c>.tmLanguage</c> as a property list, <c>.tmLanguage.json</c>, or the <c>Syntaxes</c> folder of a <c>.tmbundle</c> folder or zip) and writes it in the grammar format of the editor (PZ.5.3); the editor's own engine tokenizes,
/// nothing of TextMate or Oniguruma runs. What the conversion does: scopes become token kinds by their prefix (<c>keyword.control</c> is <c>keyword</c>, <c>constant.numeric</c> is <c>number</c>, ...); a <c>match</c> becomes a match rule; <c>begin</c>/<c>end</c>
/// becomes a region that goes on across lines; <c>include</c> of <c>#name</c>, <c>$self</c> and of another grammar of the same bundle becomes an include. Oniguruma regular expressions are rewritten for .NET (<c>\h</c>, POSIX classes, <c>\R</c>, possessive quantifiers,
/// property names) and every rule whose expression .NET still refuses is dropped. A <c>match</c> without a name whose <c>captures</c> name groups becomes one rule per top-level group, the group between a lookbehind for what precedes it and a lookahead for what follows.
/// <c>beginCaptures</c> and <c>endCaptures</c> (<c>captures</c> for both) colour their groups; a back-reference in an <c>end</c> pattern is replaced at run time by the escaped text the begin pattern matched; possessive quantifiers become atomic groups
/// (<c>{n,m}+</c> repeats the interval, as in Oniguruma); POSIX classes, <c>\p{Alpha}</c> and <c>\p{Word}</c> follow the Oniguruma definitions; <c>\G</c> holds at the scan position of the engine.
/// Not carried over, and reported each time: <c>while</c> rules, <c>\K</c>, back-references in a match or begin pattern, nested or intersected character classes, the absence operator,
/// and includes of grammars that are not in the bundle. Extensions that a built-in grammar already serves are not taken over.
/// </summary>
public static class TextMateImport
{
    private static void Push(this JsonArray array, JsonNode? node) => array.Add(node);

    public const int MaxFileBytes = 4 << 20;
    public const int MaxBundleEntries = 500;

    // ---- reading files ----

    /// <summary>The grammars in a file (<c>.tmLanguage</c>, <c>.plist</c>, <c>.json</c>, or a zipped bundle) or in a folder (a <c>.tmbundle</c>).</summary>
    public static IReadOnlyList<TextMateImportResult> FromPath(string path, GrammarRegistry known)
    {
        var sources = new List<(string Name, JsonObject Root)>();
        if (Directory.Exists(path))
        {
            string folder = Directory.Exists(Path.Combine(path, "Syntaxes")) ? Path.Combine(path, "Syntaxes") : path;
            foreach (var file in Directory.EnumerateFiles(folder).Where(IsGrammarFile).Order(StringComparer.OrdinalIgnoreCase).Take(MaxBundleEntries))
                sources.Add((Path.GetFileName(file), ParseFile(File.ReadAllBytes(Limited(file)), file)));
        }
        else if (path.EndsWith(".tmbundle", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(path);
            foreach (var entry in zip.Entries.Where(e => IsGrammarFile(e.Name) && e.FullName.Contains("Syntaxes/", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.FullName, StringComparer.Ordinal).Take(MaxBundleEntries))
            {
                if (entry.Length > MaxFileBytes) throw new TextMateImportException($"{entry.FullName} is larger than {MaxFileBytes >> 20} MB.");
                using var stream = entry.Open();
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                sources.Add((entry.Name, ParseFile(ms.ToArray(), entry.Name)));
            }
        }
        else sources.Add((Path.GetFileName(path), ParseFile(File.ReadAllBytes(Limited(path)), path)));
        if (sources.Count == 0) throw new TextMateImportException("No TextMate grammar was found (a .tmLanguage, .tmLanguage.json or .plist file, or a .tmbundle with a Syntaxes folder).");
        return Convert(sources, known);
    }

    private static bool IsGrammarFile(string name) =>
        name.EndsWith(".tmLanguage", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tmLanguage.json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".plist", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static string Limited(string file) => new FileInfo(file).Length > MaxFileBytes ? throw new TextMateImportException($"{Path.GetFileName(file)} is larger than {MaxFileBytes >> 20} MB.") : file;

    private static JsonObject ParseFile(byte[] bytes, string name)
    {
        try
        {
            if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return JsonNode.Parse(bytes, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? throw new TextMateImportException($"{name} is not a JSON object.");
            using var reader = XmlReader.Create(new MemoryStream(bytes), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            var plist = XDocument.Load(reader).Root?.Elements().FirstOrDefault() ?? throw new TextMateImportException($"{name} is empty.");
            return PlistValue(plist) as JsonObject ?? throw new TextMateImportException($"{name} is not a property list with a dictionary at its top.");
        }
        catch (Exception e) when (e is JsonException or XmlException) { throw new TextMateImportException($"{name} cannot be read: {e.Message}"); }
    }

    private static JsonNode? PlistValue(XElement e) => e.Name.LocalName switch
    {
        "dict" => PlistDict(e),
        "array" => new JsonArray([.. e.Elements().Select(PlistValue)]),
        "string" => JsonValue.Create(e.Value),
        "integer" => long.TryParse(e.Value, out var n) ? JsonValue.Create(n) : JsonValue.Create(0),
        "real" => JsonValue.Create(0),
        "true" => JsonValue.Create(true),
        "false" => JsonValue.Create(false),
        _ => null,
    };

    private static JsonObject PlistDict(XElement dict)
    {
        var result = new JsonObject();
        string? key = null;
        foreach (var child in dict.Elements())
        {
            if (child.Name.LocalName == "key") key = child.Value;
            else if (key is not null)
            {
                result[key] = PlistValue(child);
                key = null;
            }
        }
        return result;
    }

    // ---- converting ----

    /// <summary>Converts parsed grammars of one source; the grammars may include each other by scope name.</summary>
    public static IReadOnlyList<TextMateImportResult> Convert(IReadOnlyList<(string Name, JsonObject Root)> sources, GrammarRegistry known)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, root) in sources)
        {
            string scope = root["scopeName"]?.GetValue<string>() ?? throw new TextMateImportException($"{name} has no scopeName, so it is not a TextMate grammar.");
            string id = IdOf(scope, known);
            for (int n = 2; !taken.Add(id); n++) id = IdOf(scope, known) + "-" + n;
            ids[scope] = id;
        }
        var results = sources.Select(s => new Converter(s.Root, ids[(string)s.Root["scopeName"]!], ids, known).Run()).ToList();
        try
        {
            var scratch = new GrammarRegistry(known) { CompileRegexes = false };
            foreach (var result in results) scratch.Add(result.Json);
            scratch.Validate();
        }
        catch (GrammarException e) { throw new TextMateImportException($"The converted grammar is not valid: {e.Message}"); }
        return results;
    }

    private static string IdOf(string scope, GrammarRegistry known)
    {
        string tail = scope.Contains('.') ? scope[(scope.IndexOf('.') + 1)..] : scope;
        var sb = new StringBuilder();
        foreach (char c in tail.ToLowerInvariant()) sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        string id = sb.ToString().Trim('-');
        if (id.Length == 0) id = "grammar";
        return known.Find(id) is not null ? "tm-" + id : id;
    }

    private static readonly (string Prefix, string? Kind)[] Kinds =
    [
        ("comment.block.documentation", TokenKinds.DocComment), ("comment.documentation", TokenKinds.DocComment), ("comment", TokenKinds.Comment),
        ("string.regexp", TokenKinds.Regex), ("string", TokenKinds.String), ("constant.character.escape", TokenKinds.Escape), ("constant.numeric", TokenKinds.Number),
        ("constant", TokenKinds.Constant), ("keyword.operator", TokenKinds.Operator), ("keyword.other.preprocessor", TokenKinds.Preprocessor), ("meta.preprocessor", TokenKinds.Preprocessor),
        ("keyword", TokenKinds.Keyword), ("storage.type", TokenKinds.Type), ("storage", TokenKinds.Keyword),
        ("entity.name.function", TokenKinds.Function), ("entity.name.tag", TokenKinds.Tag), ("entity.name.section", TokenKinds.Heading), ("entity.name.type", TokenKinds.Type),
        ("entity.name.class", TokenKinds.Type), ("entity.name.struct", TokenKinds.Type), ("entity.name.enum", TokenKinds.Type), ("entity.name.namespace", TokenKinds.Type),
        ("entity.other.inherited-class", TokenKinds.Type), ("entity.other.attribute-name", TokenKinds.Attribute), ("entity.name", TokenKinds.Variable),
        ("support.function", TokenKinds.Function), ("support.type", TokenKinds.Type), ("support.class", TokenKinds.Type), ("support.constant", TokenKinds.Constant),
        ("support.variable.property", TokenKinds.Property), ("support.variable", TokenKinds.Variable), ("support", TokenKinds.Keyword),
        ("variable.other.property", TokenKinds.Property), ("variable.other.member", TokenKinds.Property), ("variable", TokenKinds.Variable),
        ("punctuation", TokenKinds.Punctuation), ("markup.heading", TokenKinds.Heading), ("markup.bold", TokenKinds.Strong), ("markup.italic", TokenKinds.Emphasis),
        ("markup.underline.link", TokenKinds.Link), ("markup.raw", TokenKinds.Code), ("markup.inline.raw", TokenKinds.Code),
    ];

    /// <summary>The token kind a scope name stands for (the first of several scopes that has one); null for scopes the editor does not colour (<c>meta</c>, <c>source</c>, ...).</summary>
    public static string? KindOf(string? scopes)
    {
        foreach (var scope in (scopes ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            foreach (var (prefix, kind) in Kinds)
                if (scope == prefix || scope.StartsWith(prefix + ".", StringComparison.Ordinal)) return kind;
        return null;
    }

    private sealed class Converter(JsonObject root, string id, Dictionary<string, string> scopeIds, GrammarRegistry known)
    {
        private readonly List<string> _warnings = [];
        private readonly HashSet<string> _seen = [];
        private int _regions;

        private void Warn(string text)
        {
            if (_seen.Add(text) && _warnings.Count < 200) _warnings.Add(text);
        }

        private static string Safe(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name.ToLowerInvariant()) sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
            return sb.Length == 0 ? "rule" : sb.ToString();
        }

        public TextMateImportResult Run()
        {
            string name = root["name"]?.GetValue<string>() ?? id;
            var doc = new JsonObject { ["$schema"] = "grammar.schema.json", ["formatVersion"] = 1, ["id"] = id, ["name"] = name, ["version"] = "1.0.0" };
            var extensions = new JsonArray();
            var fileNames = new JsonArray();
            foreach (var type in (root["fileTypes"] as JsonArray ?? []).Select(t => t?.GetValue<string>()).OfType<string>())
            {
                if (type.Length == 0) continue;
                if (char.IsUpper(type[0])) { fileNames.Push(JsonValue.Create(type)); continue; }
                string ext = "." + type.TrimStart('.');
                if (known.ForFile("x" + ext) is { } owner) Warn($"The extension {ext} belongs to the built-in grammar {owner.Id} and was not taken over.");
                else extensions.Push(JsonValue.Create(ext));
            }
            if (extensions.Count > 0) doc["extensions"] = extensions;
            if (fileNames.Count > 0) doc["fileNames"] = fileNames;
            if (root["firstLineMatch"]?.GetValue<string>() is { } first && Rewrite(first, out var firstNet, out _)) doc["firstLine"] = firstNet;

            var repository = new JsonObject();
            if (root["repository"] is JsonObject repo)
                foreach (var (key, value) in repo)
                    if (value is JsonObject rule) repository[Safe(key)] = Rules([rule]);
            if (repository.Count > 0) doc["repository"] = repository;
            doc["rules"] = Rules(root["patterns"] as JsonArray ?? []);
            if (root["while"] is not null) Warn("The grammar has a top-level while rule, which is not carried over.");

            string json = doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            return new TextMateImportResult(id, name, json, _warnings);
        }

        private JsonArray Rules(IEnumerable<JsonNode?> patterns)
        {
            var result = new JsonArray();
            foreach (var node in patterns)
                if (node is JsonObject rule) Rule(rule, result);
            return result;
        }

        private void Rule(JsonObject rule, JsonArray into)
        {
            if (rule["disabled"] is { } disabled && disabled.ToJsonString() is "1" or "true") return;
            if (rule["include"]?.GetValue<string>() is { } include) { Include(include, into); return; }
            if (rule["while"] is not null) { Warn($"A while rule ({Describe(rule)}) is not carried over."); return; }
            string? name = rule["name"]?.GetValue<string>();
            if (rule["begin"] is not null && rule["end"] is not null) { Region(rule, into); return; }
            if (rule["match"]?.GetValue<string>() is { } match) { Match(rule, match, name, into); return; }
            if (rule["patterns"] is JsonArray nested) foreach (var child in nested) if (child is JsonObject c) Rule(c, into);
        }

        private static string Describe(JsonObject rule) => rule["name"]?.GetValue<string>() ?? rule["begin"]?.GetValue<string>() ?? rule["match"]?.GetValue<string>() ?? "rule";

        private void Include(string include, JsonArray into)
        {
            if (include is "$self" or "$base") into.Push(new JsonObject { ["include"] = id });
            else if (include.StartsWith('#')) into.Push(new JsonObject { ["include"] = "#" + Safe(include[1..]) });
            else
            {
                int hash = include.IndexOf('#');
                string scope = hash < 0 ? include : include[..hash];
                if (scopeIds.TryGetValue(scope, out var other) && other != id) into.Push(new JsonObject { ["include"] = hash < 0 ? other : other + "#" + Safe(include[(hash + 1)..]) });
                else Warn($"The grammar {scope} is not in the same bundle, so the include of {include} is left out.");
            }
        }

        private void Match(JsonObject rule, string match, string? name, JsonArray into)
        {
            if (!Rewrite(match, out var net, out var groups)) { Warn($"The match {Shorten(match)} could not be converted and was left out."); return; }
            var captures = rule["captures"] as JsonObject;
            string? kind = KindOf(name) ?? KindOf(captures?["0"]?["name"]?.GetValue<string>());
            if (kind is not null || name is not null)
            {
                into.Push(Token(new JsonObject { ["match"] = net }, kind));
                if (captures is not null && captures.Any(c => c.Key != "0" && KindOf(c.Value?["name"]?.GetValue<string>()) is { } k && k != kind)) Warn($"The captures of {Shorten(match)} are not coloured separately because the whole match has a name.");
                return;
            }
            if (captures is null) { into.Push(new JsonObject { ["match"] = net }); return; }
            bool any = false;
            foreach (var (key, value) in captures)
            {
                if (!int.TryParse(key, out var index) || index == 0 || KindOf(value?["name"]?.GetValue<string>()) is not { } groupKind) continue;
                var group = groups.FirstOrDefault(g => g.Index == index);
                if (group is null) { Warn($"Capture {index} of {Shorten(match)} is inside another group and is not coloured."); continue; }
                string pre = net[..group.Start], post = net[group.End..];
                string pattern = (pre.Length > 0 ? "(?<=" + pre + ")" : "") + net[group.Start..group.End] + (post.Length > 0 ? "(?=" + post + ")" : "");
                if (!TryCompile(pattern)) { Warn($"Capture {index} of {Shorten(match)} could not be converted."); continue; }
                into.Push(Token(new JsonObject { ["match"] = pattern }, groupKind));
                any = true;
            }
            if (!any) into.Push(new JsonObject { ["match"] = net });
        }

        private void Region(JsonObject rule, JsonArray into)
        {
            string begin = rule["begin"]!.GetValue<string>(), end = rule["end"]!.GetValue<string>();
            if (!Rewrite(begin, out var beginNet, out _)) { Warn($"The begin pattern {Shorten(begin)} could not be converted; the rule was left out."); return; }
            int beginCount = _count;
            var beginNames = new Dictionary<string, int>(_names, StringComparer.Ordinal);
            if (!Rewrite(end, out var endNet, out _, beginNames)) { Warn($"The end pattern {Shorten(end)} could not be converted; the rule was left out."); return; }
            int endCount = _count;
            string? name = rule["name"]?.GetValue<string>(), content = rule["contentName"]?.GetValue<string>();
            var beginCaptures = rule["beginCaptures"] as JsonObject ?? rule["captures"] as JsonObject;
            var endCaptures = rule["endCaptures"] as JsonObject ?? rule["captures"] as JsonObject;
            string? beginKind = KindOf(beginCaptures?["0"]?["name"]?.GetValue<string>()) ?? KindOf(name);
            string? endKind = KindOf(endCaptures?["0"]?["name"]?.GetValue<string>()) ?? KindOf(name);
            var region = new JsonObject
            {
                ["name"] = "r" + ++_regions,
                ["begin"] = beginNet,
                ["end"] = endNet,
                ["multiline"] = true,
            };
            if ((KindOf(content) ?? KindOf(name)) is { } kind) region["token"] = kind;
            if (beginKind is not null) region["beginToken"] = beginKind;
            if (endKind is not null) region["endToken"] = endKind;
            if (GroupKinds(beginCaptures, beginCount) is { } beginGroups) region["beginCaptures"] = beginGroups;
            if (GroupKinds(endCaptures, endCount) is { } endGroups) region["endCaptures"] = endGroups;
            var inner = Rules(rule["patterns"] as JsonArray ?? []);
            if (inner.Count > 0) region["rules"] = inner;
            into.Push(region);
        }

        /// <summary>The coloured groups (number to token kind) of a captures object; groups the expression does not have and unnamed ones are left out.</summary>
        private static JsonObject? GroupKinds(JsonObject? captures, int groupCount)
        {
            var kinds = new JsonObject();
            foreach (var (key, value) in captures ?? [])
                if (int.TryParse(key, out int index) && index >= 1 && index <= groupCount && KindOf(value?["name"]?.GetValue<string>()) is { } kind) kinds[key] = kind;
            return kinds.Count > 0 ? kinds : null;
        }

        private static JsonObject Token(JsonObject rule, string? kind)
        {
            if (kind is not null) rule["token"] = kind;
            return rule;
        }

        private static string Shorten(string text) => "\"" + (text.Length > 60 ? text[..60] + "…" : text) + "\"";

        private static bool TryCompile(string pattern)
        {
            try { _ = new Regex(pattern); return true; }
            catch (ArgumentException) { return false; }
        }

        // ---- Oniguruma to .NET ----

        private sealed record Group(int Index, int Start, int End);

        /// <summary>A character set as the body of a .NET character class; <c>Excluded</c> means the class is every character but these (graph, print).</summary>
        private readonly record struct CharSet(string Body, bool Excluded = false);

        /// <summary>The Oniguruma definitions of the POSIX bracket classes and of the properties <c>\p{Alpha}</c>, ..., for Unicode text.</summary>
        private static readonly Dictionary<string, CharSet> Posix = new(StringComparer.OrdinalIgnoreCase)
        {
            ["alpha"] = new(@"\p{L}\p{M}"), ["alnum"] = new(@"\p{L}\p{M}\p{Nd}"), ["ascii"] = new(@"\x00-\x7F"), ["blank"] = new(@"\p{Zs}\t"),
            ["cntrl"] = new(@"\p{Cc}\p{Cf}\p{Cn}\p{Co}\p{Cs}"), ["digit"] = new(@"\p{Nd}"), ["lower"] = new(@"\p{Ll}"), ["upper"] = new(@"\p{Lu}"),
            ["graph"] = new(@"\p{Zs}\p{Zl}\p{Zp}\p{Cc}\p{Cn}\p{Cs}", true), ["print"] = new(@"\p{Zl}\p{Zp}\p{Cc}\p{Cn}\p{Cs}", true),
            ["punct"] = new(@"\p{Pc}\p{Pd}\p{Pe}\p{Pf}\p{Pi}\p{Po}\p{Ps}\$\+<=>\^`\|~"), ["space"] = new(@"\p{Zs}\p{Zl}\p{Zp}\t\n\v\f\r\x85"),
            ["word"] = new(@"\p{L}\p{M}\p{Nd}\p{Pc}"), ["xdigit"] = new("0-9A-Fa-f"), ["letter"] = new(@"\p{L}"), ["number"] = new(@"\p{N}"),
        };

        /// <summary>The .NET text of a set, or its complement when <paramref name="negate"/>; false when the complement of a set cannot sit among other members of a class.</summary>
        private static bool Express(CharSet set, bool negate, bool inClass, out string text)
        {
            if (!set.Excluded && set.Body.StartsWith(@"\p{", StringComparison.Ordinal) && set.Body.IndexOf('}') == set.Body.Length - 1)
            {
                text = (negate ? @"\P" : @"\p") + set.Body[2..];
                return true;
            }
            bool exclude = set.Excluded ^ negate;
            text = inClass ? set.Body : "[" + (exclude ? "^" : "") + set.Body + "]";
            return !(inClass && exclude);
        }

        private static readonly Regex Interval = new(@"\G\{(\d+(,\d*)?|,\d+)\}", RegexOptions.CultureInvariant);
        private static readonly Regex LonePosix = new(@"\G\[(\^?)\[:(\^?)([A-Za-z]+):\]\]", RegexOptions.CultureInvariant);

        private Dictionary<string, int> _names = [];
        private int _count;

        /// <summary>Rewrites an Oniguruma expression for .NET. False when it uses what .NET cannot do or the result does not compile; the group list is that of the top-level capturing groups, as positions in <paramref name="net"/>.</summary>
        private bool Rewrite(string onig, out string net, out List<Group> groups, Dictionary<string, int>? beginNames = null)
        {
            groups = [];
            net = "";
            _names = [];
            _count = 0;
            var o = new StringBuilder();
            bool named = System.Text.RegularExpressions.Regex.IsMatch(onig, @"\(\?(<[A-Za-z_]|'|P<)");
            var open = new Stack<(int Index, int Position)>();
            int count = 0;
            int atom = -1; // where the last atom starts in o, for a quantifier with a + after it to wrap
            var found = groups;
            void Wrap(string opener, string closer)
            {
                if (found.Count > 0 && found[^1].Start == atom) found.RemoveAt(found.Count - 1);
                o.Insert(atom, opener).Append(closer);
            }
            try
            {
                for (int i = 0; i < onig.Length; i++)
                {
                    char c = onig[i];
                    if (c == '\\' && i + 1 < onig.Length)
                    {
                        char n = onig[++i];
                        atom = o.Length;
                        switch (n)
                        {
                            case >= '1' and <= '9':
                            {
                                if (beginNames is null) { Warn("A back-reference (\\1) in a match or begin pattern is not supported; the rule was left out."); return false; }
                                int first = i;
                                while (i + 1 < onig.Length && char.IsAsciiDigit(onig[i + 1])) i++;
                                o.Append("(?:(?#begin:").Append(onig, first, i - first + 1).Append("))");
                                break;
                            }
                            case 'k' when i + 1 < onig.Length && onig[i + 1] is '<' or '\'':
                            {
                                if (beginNames is null) { Warn("A named back-reference (\\k<name>) in a match or begin pattern is not supported; the rule was left out."); return false; }
                                int close = onig.IndexOf(onig[i + 1] == '<' ? '>' : '\'', i + 2);
                                string reference = close < 0 ? "" : onig[(i + 2)..close];
                                if (close < 0 || !(int.TryParse(reference, out int number) || beginNames.TryGetValue(reference, out number))) { Warn($"The back-reference \\k<{reference}> names no group of the begin pattern; the rule was left out."); return false; }
                                o.Append("(?:(?#begin:").Append(number).Append("))");
                                i = close;
                                break;
                            }
                            case 'h': o.Append("[0-9a-fA-F]"); break;
                            case 'H': o.Append("[^0-9a-fA-F]"); break;
                            case 'R': o.Append(@"(?>\r\n|[\n\v\f\r\x85\u2028\u2029])"); break;
                            case 'X': o.Append(@"(?:\P{M}\p{M}*)"); break;
                            case 'G': o.Append(@"\G"); break; // the engine matches from the scan position, which is where \G holds
                            case 'K': Warn(@"\K (keep out) is not supported."); return false;
                            case 'p' or 'P' when i + 1 < onig.Length && onig[i + 1] == '{':
                            {
                                int close = onig.IndexOf('}', i);
                                if (close < 0) return false;
                                string prop = onig[(i + 2)..close];
                                bool negate = n == 'P' ^ prop.StartsWith('^');
                                prop = prop.TrimStart('^');
                                if (Posix.TryGetValue(prop, out var set))
                                {
                                    if (!Express(set, negate, false, out var text)) return false;
                                    o.Append(text);
                                }
                                else o.Append(negate ? @"\P{" : @"\p{").Append(prop).Append('}');
                                i = close;
                                break;
                            }
                            default: o.Append('\\').Append(n); break;
                        }
                        continue;
                    }
                    if (c == '[') { atom = o.Length; i = CharacterClass(onig, i, o); continue; }
                    if (c == '{' && Interval.Match(onig, i) is { Success: true } interval)
                    {
                        o.Append(interval.Value);
                        i += interval.Length - 1;
                        if (i + 1 < onig.Length && onig[i + 1] == '+' && atom >= 0) { Wrap("(?:", ")+"); i++; } // in Oniguruma {n,m}+ repeats the interval, it is not possessive
                        continue;
                    }
                    if (c == '(')
                    {
                        if (i + 1 < onig.Length && onig[i + 1] == '?')
                        {
                            string rest = onig[(i + 2)..];
                            if (rest.StartsWith('~')) { Warn("The absence operator (?~...) is not supported."); return false; }
                            if (rest.StartsWith("#", StringComparison.Ordinal)) { int end = onig.IndexOf(')', i); if (end < 0) return false; i = end; continue; }
                            bool capturing = rest.StartsWith('\'') || rest.StartsWith("P<", StringComparison.Ordinal) || (rest.StartsWith('<') && rest.Length > 1 && rest[1] is not ('=' or '!'));
                            if (capturing)
                            {
                                int nameStart = rest.StartsWith("P<", StringComparison.Ordinal) ? 2 : 1, nameEnd = rest.IndexOfAny(['>', '\''], nameStart);
                                if (nameEnd > nameStart) _names[rest[nameStart..nameEnd]] = count + 1;
                                if (rest.StartsWith("P<", StringComparison.Ordinal)) { o.Append("(?<"); i += 3; open.Push((++count, o.Length - 3)); continue; }
                                open.Push((++count, o.Length));
                            }
                            else open.Push((-1, o.Length));
                            o.Append("(?");
                            i++;
                            continue;
                        }
                        if (named) { o.Append("(?:"); open.Push((-1, o.Length - 3)); }
                        else { o.Append('('); open.Push((++count, o.Length - 1)); }
                        continue;
                    }
                    if (c == ')')
                    {
                        if (open.Count == 0) return false;
                        var (index, position) = open.Pop();
                        o.Append(')');
                        atom = position;
                        if (index > 0 && open.Count == 0) groups.Add(new Group(index, position, o.Length));
                        continue;
                    }
                    if (c is '*' or '+' or '?')
                    {
                        if (i + 1 < onig.Length && onig[i + 1] == '+' && atom >= 0) { Wrap("(?>", c + ")"); i++; }
                        else o.Append(c);
                        continue;
                    }
                    atom = o.Length;
                    o.Append(c);
                }
                if (open.Count != 0) return false;
                _count = count;
                net = o.ToString();
                _ = new Regex(net);
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        /// <summary>Copies a character class starting at <paramref name="start"/>; returns the index of its closing bracket.</summary>
        private int CharacterClass(string onig, int start, StringBuilder o)
        {
            if (LonePosix.Match(onig, start) is { Success: true } lone && Posix.TryGetValue(lone.Groups[3].Value, out var only))
            {
                if (!Express(only, lone.Groups[1].Length + lone.Groups[2].Length == 1, false, out var whole)) throw new ArgumentException("posix class");
                o.Append(whole);
                return start + lone.Length - 1;
            }
            o.Append('[');
            int i = start + 1;
            if (i < onig.Length && onig[i] == '^') { o.Append('^'); i++; }
            if (i < onig.Length && onig[i] == ']') { o.Append(@"\]"); i++; }
            for (; i < onig.Length; i++)
            {
                char c = onig[i];
                if (c == ']') { o.Append(']'); return i; }
                if (c == '\\' && i + 1 < onig.Length)
                {
                    char n = onig[++i];
                    if (n == 'h') o.Append("0-9a-fA-F");
                    else if (n is 'p' or 'P' && i + 1 < onig.Length && onig[i + 1] == '{' && onig.IndexOf('}', i) is var close && close > 0)
                    {
                        string prop = onig[(i + 2)..close];
                        bool negate = n == 'P' ^ prop.StartsWith('^');
                        prop = prop.TrimStart('^');
                        if (Posix.TryGetValue(prop, out var set))
                        {
                            if (!Express(set, negate, true, out var text)) { Warn($"The property {prop} negated among other members of a character class is not supported."); throw new ArgumentException("negated property"); }
                            o.Append(text);
                        }
                        else o.Append(negate ? @"\P{" : @"\p{").Append(prop).Append('}');
                        i = close;
                    }
                    else o.Append('\\').Append(n);
                }
                else if (c == '[' && i + 1 < onig.Length && onig[i + 1] == ':')
                {
                    int close = onig.IndexOf(":]", i + 2, StringComparison.Ordinal);
                    string posix = close < 0 ? "" : onig[(i + 2)..close];
                    if (Posix.TryGetValue(posix.TrimStart('^'), out var set) && Express(set, posix.StartsWith('^'), true, out var text)) { o.Append(text); i = close + 1; }
                    else { Warn("A nested character class or a negated POSIX class among other members of a class is not supported."); throw new ArgumentException("nested class"); }
                }
                else if (c == '[') { Warn("A nested character class is not supported."); throw new ArgumentException("nested class"); }
                else if (c == '&' && i + 1 < onig.Length && onig[i + 1] == '&') { Warn("An intersection of character classes (&&) is not supported."); throw new ArgumentException("intersection"); }
                else o.Append(c);
            }
            throw new ArgumentException("unterminated class");
        }
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Syntax;

/// <summary>Reads a grammar file strictly: unknown properties and malformed values are errors, so a typo in a rule is not silently ignored.</summary>
internal static class GrammarParser
{
    private static readonly HashSet<string> TopKeys = ["$schema", "formatVersion", "id", "name", "version", "extensions", "fileNames", "firstLine", "caseSensitive", "wordPattern", "keywords", "comments", "repository", "rules"];
    private static readonly HashSet<string> CommentKeys = ["line", "block"];
    private static readonly HashSet<string> GroupKeys = ["name", "token", "words", "caseSensitive"];
    private static readonly HashSet<string> RuleKeys = ["match", "words", "operators", "begin", "include", "token", "end", "beginToken", "endToken", "multiline", "rules", "embed", "name"];
    private static readonly Regex KindPattern = new(@"^[a-z][a-z0-9]*(\.[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex IdPattern = new(@"^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant);

    public static Grammar Parse(string json, GrammarRegistry registry)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException e)
        {
            throw new GrammarException("Grammar is not valid JSON: " + e.Message);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new GrammarException("Grammar must be a JSON object.");
            CheckKeys(root, TopKeys, "grammar");

            if (!root.TryGetProperty("formatVersion", out var fv) || fv.ValueKind != JsonValueKind.Number || !fv.TryGetInt32(out int version))
                throw new GrammarException("Grammar needs an integer formatVersion.");
            if (version != Grammar.SupportedFormatVersion)
                throw new GrammarException($"Grammar format version {version} is not supported (this build reads {Grammar.SupportedFormatVersion}).");

            string id = RequiredString(root, "id", "grammar");
            if (!IdPattern.IsMatch(id)) throw new GrammarException($"Grammar id '{id}' must be lower case letters, digits and '-'.");
            string wordPattern = OptionalString(root, "wordPattern") ?? Grammar.DefaultWordPattern;
            CheckRegex(wordPattern, id, "wordPattern");
            string? firstLine = OptionalString(root, "firstLine");
            if (firstLine is not null) CheckRegex(firstLine, id, "firstLine");

            var g = new Grammar(registry, id, OptionalString(root, "name") ?? id, OptionalString(root, "version"), OptionalBool(root, "caseSensitive") ?? true,
                wordPattern, StringList(root, "extensions"), StringList(root, "fileNames"), firstLine);

            if (root.TryGetProperty("keywords", out var groups))
            {
                if (groups.ValueKind != JsonValueKind.Array) throw new GrammarException($"{id}: keywords must be an array.");
                foreach (var group in groups.EnumerateArray()) ParseGroup(g, group);
            }

            if (root.TryGetProperty("comments", out var comments))
            {
                if (comments.ValueKind != JsonValueKind.Object) throw new GrammarException($"{id}: comments must be an object.");
                CheckKeys(comments, CommentKeys, id + ".comments");
                g.LineComment = OptionalString(comments, "line") is { Length: > 0 } line ? line : comments.TryGetProperty("line", out _)
                    ? throw new GrammarException($"{id}.comments.line must not be empty.") : null;
                if (comments.TryGetProperty("block", out _))
                {
                    var block = StringList(comments, "block");
                    if (block.Count != 2 || block.Any(m => m.Length == 0)) throw new GrammarException($"{id}.comments.block must be two non-empty markers.");
                    g.BlockComment = (block[0], block[1]);
                }
            }

            if (root.TryGetProperty("repository", out var repo))
            {
                if (repo.ValueKind != JsonValueKind.Object) throw new GrammarException($"{id}: repository must be an object.");
                foreach (var entry in repo.EnumerateObject())
                {
                    var list = new List<Rule>();
                    ParseRules(g, entry.Value, list, $"{id}.repository.{entry.Name}");
                    g.Repository[entry.Name] = list;
                }
            }

            if (root.TryGetProperty("rules", out var rules)) ParseRules(g, rules, g.Root.Rules, id + ".rules");
            return g;
        }
    }

    private static void ParseGroup(Grammar g, JsonElement group)
    {
        if (group.ValueKind != JsonValueKind.Object) throw new GrammarException($"{g.Id}: a keyword group must be an object.");
        CheckKeys(group, GroupKeys, g.Id + ".keywords");
        string name = RequiredString(group, "name", g.Id + ".keywords");
        string kind = Kind(OptionalString(group, "token") ?? "keyword", $"{g.Id}.keywords.{name}");
        if (!group.TryGetProperty("words", out _)) throw new GrammarException($"{g.Id}.keywords.{name} needs a list of words.");
        bool cs = OptionalBool(group, "caseSensitive") ?? g.CaseSensitive;
        foreach (var w in StringList(group, "words")) g.AddWord(w, kind, cs);
    }

    private static void ParseRules(Grammar g, JsonElement array, List<Rule> into, string path)
    {
        if (array.ValueKind != JsonValueKind.Array) throw new GrammarException($"{path} must be an array of rules.");
        int index = 0;
        foreach (var e in array.EnumerateArray()) into.Add(ParseRule(g, e, $"{path}[{index++}]"));
    }

    private static Rule ParseRule(Grammar g, JsonElement e, string path)
    {
        if (e.ValueKind != JsonValueKind.Object) throw new GrammarException($"{path} must be an object.");
        CheckKeys(e, RuleKeys, path);
        string? token = OptionalString(e, "token") is { } t ? Kind(t, path) : null;
        bool Has(string key) => e.TryGetProperty(key, out _);
        int kinds = new[] { "match", "words", "operators", "begin", "include" }.Count(Has);
        if (kinds != 1) throw new GrammarException($"{path} needs exactly one of match, words, operators, begin, include.");

        if (Has("include")) return new IncludeRule { Owner = g, Target = RequiredString(e, "include", path) };
        if (Has("words"))
        {
            if (e.GetProperty("words").ValueKind != JsonValueKind.True) throw new GrammarException($"{path}.words must be true.");
            return new MatchRule { Owner = g, Pattern = "", IsWords = true };
        }
        if (Has("match"))
        {
            string p = RequiredString(e, "match", path);
            CheckRegex(p, g.Id, path);
            return new MatchRule { Owner = g, Pattern = p, Token = token };
        }
        if (Has("operators"))
        {
            var ops = StringList(e, "operators");
            if (ops.Count == 0) throw new GrammarException($"{path}.operators is empty.");
            string p = string.Join('|', ops.OrderByDescending(o => o.Length).Select(Regex.Escape));
            return new MatchRule { Owner = g, Pattern = p, Token = token ?? TokenKinds.Operator };
        }

        string begin = RequiredString(e, "begin", path);
        string? end = OptionalString(e, "end") ?? throw new GrammarException($"{path} needs an end pattern.");
        CheckRegex(begin, g.Id, path + ".begin");
        CheckRegex(end, g.Id, path + ".end");
        var ctx = new ContextDef(g)
        {
            Name = OptionalString(e, "name") ?? token ?? "region",
            Begin = begin,
            End = end,
            Token = token,
            BeginToken = OptionalString(e, "beginToken") is { } bt ? Kind(bt, path) : token,
            EndToken = OptionalString(e, "endToken") is { } et ? Kind(et, path) : token,
            Multiline = OptionalBool(e, "multiline") ?? false,
            Embed = OptionalString(e, "embed"),
        };
        if (e.TryGetProperty("rules", out var inner)) ParseRules(g, inner, ctx.Rules, path + ".rules");
        return ctx;
    }

    private static string Kind(string kind, string path) =>
        KindPattern.IsMatch(kind) ? string.Intern(kind) : throw new GrammarException($"{path}: token '{kind}' must be dotted lower case names.");

    private static void CheckRegex(string pattern, string grammar, string path)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException e)
        {
            throw new GrammarException($"{grammar}: {path}: {e.Message}");
        }
    }

    private static void CheckKeys(JsonElement e, HashSet<string> allowed, string path)
    {
        foreach (var p in e.EnumerateObject())
            if (!allowed.Contains(p.Name)) throw new GrammarException($"{path}: unknown property '{p.Name}'.");
    }

    private static string RequiredString(JsonElement e, string key, string path) =>
        OptionalString(e, key) ?? throw new GrammarException($"{path} needs a string '{key}'.");

    private static string? OptionalString(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : throw new GrammarException($"'{key}' must be a string.");
    }

    private static bool? OptionalBool(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var v)) return null;
        return v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : throw new GrammarException($"'{key}' must be true or false.");
    }

    private static List<string> StringList(JsonElement e, string key)
    {
        var list = new List<string>();
        if (!e.TryGetProperty(key, out var v)) return list;
        if (v.ValueKind != JsonValueKind.Array) throw new GrammarException($"'{key}' must be an array of strings.");
        foreach (var item in v.EnumerateArray())
            list.Add(item.ValueKind == JsonValueKind.String ? item.GetString()! : throw new GrammarException($"'{key}' must hold only strings."));
        return list;
    }
}

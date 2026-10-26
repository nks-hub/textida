using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Syntax;

/// <summary>A grammar file is malformed, uses an unsupported format version or refers to something that does not exist.</summary>
public sealed class GrammarException(string message) : Exception(message);

/// <summary>
/// Compiled form of one grammar file (format described by <c>grammar.schema.json</c>). Immutable after loading and safe to share between threads.
/// Rules are tried at every position of the line at once: the leftmost match wins, on a tie the rule listed first.
/// </summary>
public sealed class Grammar
{
    /// <summary>The format version this build reads. A file with another <c>formatVersion</c> is rejected.</summary>
    public const int SupportedFormatVersion = 1;

    internal const string DefaultWordPattern = @"[\p{L}_][\p{L}\p{N}_]*";

    private readonly Dictionary<string, string> _caseSensitiveWords = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _caseInsensitiveWords = new(StringComparer.OrdinalIgnoreCase);

    internal Grammar(GrammarRegistry registry, string id, string name, string? version, bool caseSensitive, string wordPattern,
        IReadOnlyList<string> extensions, IReadOnlyList<string> fileNames, string? firstLine)
    {
        Registry = registry;
        Id = id;
        Name = name;
        Version = version;
        CaseSensitive = caseSensitive;
        WordPattern = wordPattern;
        Extensions = extensions;
        FileNames = fileNames;
        FirstLinePattern = firstLine;
        Root = new ContextDef(this) { Name = "", IsRoot = true, Multiline = true };
    }

    public string Id { get; }
    public string Name { get; }
    /// <summary>Version of the grammar itself (free text), not of the file format.</summary>
    public string? Version { get; }
    public bool CaseSensitive { get; }
    public IReadOnlyList<string> Extensions { get; }
    public IReadOnlyList<string> FileNames { get; }
    /// <summary>Regular expression a first line must match for a file without a known extension (shebang, XML prolog).</summary>
    public string? FirstLinePattern { get; }

    internal GrammarRegistry Registry { get; }
    internal string WordPattern { get; }
    internal ContextDef Root { get; }
    internal Dictionary<string, List<Rule>> Repository { get; } = new(StringComparer.Ordinal);

    /// <summary>State at the start of the first line.</summary>
    public LineState InitialState => Root.Push(null);

    internal void AddWord(string word, string kind, bool caseSensitive) =>
        (caseSensitive ? _caseSensitiveWords : _caseInsensitiveWords).TryAdd(word, kind);

    internal string? LookupWord(ReadOnlySpan<char> word)
    {
        if (_caseSensitiveWords.Count > 0 && _caseSensitiveWords.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(word, out var kind)) return kind;
        if (_caseInsensitiveWords.Count > 0 && _caseInsensitiveWords.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(word, out kind)) return kind;
        return null;
    }

    /// <summary>Compiles every context now, so a bad regular expression or a missing include surfaces as <see cref="GrammarException"/>.</summary>
    public void Validate()
    {
        var seen = new HashSet<ContextDef>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<ContextDef>();
        queue.Enqueue(Root);
        foreach (var rules in Repository.Values) Enqueue(rules);
        while (queue.Count > 0)
        {
            var ctx = queue.Dequeue();
            if (!seen.Add(ctx)) continue;
            ctx.GetMatcher();
            Enqueue(ctx.Rules);
        }

        void Enqueue(List<Rule> rules)
        {
            foreach (var r in rules)
                if (r is ContextDef c) queue.Enqueue(c);
        }
    }

    public static Grammar Parse(string json, GrammarRegistry registry) => GrammarParser.Parse(json, registry);

    public override string ToString() => $"{Id} {Version}";
}

/// <summary>Grammars known to an editor, looked up by id, file name or extension.</summary>
public sealed class GrammarRegistry
{
    private readonly Dictionary<string, Grammar> _byId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Compile the regular expressions to IL where the runtime can; the interpreter is the fallback (NativeAOT).</summary>
    public bool CompileRegexes { get; init; } = RuntimeFeature.IsDynamicCodeCompiled;

    public IEnumerable<Grammar> All => _byId.Values;

    /// <summary>Parses a grammar file and registers it; a grammar with the same id is replaced.</summary>
    public Grammar Add(string json)
    {
        var g = Grammar.Parse(json, this);
        _byId[g.Id] = g;
        return g;
    }

    public Grammar? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Grammar for a file name: exact file name first, then the longest matching extension.</summary>
    public Grammar? ForFile(string fileName)
    {
        fileName = Path.GetFileName(fileName);
        Grammar? best = null;
        int bestLen = 0;
        foreach (var g in _byId.Values)
        {
            if (g.FileNames.Any(n => string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase))) return g;
            foreach (var ext in g.Extensions)
                if (ext.Length > bestLen && fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) (best, bestLen) = (g, ext.Length);
        }
        return best;
    }

    /// <summary>Grammar whose <see cref="Grammar.FirstLinePattern"/> matches the first line.</summary>
    public Grammar? ForFirstLine(string firstLine) =>
        _byId.Values.FirstOrDefault(g => g.FirstLinePattern is { } p && Regex.IsMatch(firstLine, p, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));

    public void Validate()
    {
        foreach (var g in _byId.Values) g.Validate();
    }
}

internal abstract class Rule
{
    public required Grammar Owner { get; init; }
}

internal sealed class MatchRule : Rule
{
    public required string Pattern { get; init; }
    public string? Token { get; init; }
    /// <summary>The match is a word of the grammar: the token comes from the keyword groups and an unlisted word is plain.</summary>
    public bool IsWords { get; init; }
}

internal sealed class IncludeRule : Rule
{
    public required string Target { get; init; }
}

/// <summary>A region with its own rules: the root, a string, a comment, an embedded language. Its identity is what a <see cref="LineState"/> frame refers to.</summary>
internal sealed class ContextDef : Rule
{
    private readonly ConcurrentDictionary<object, LineState> _states = new(ReferenceEqualityComparer.Instance);
    private Matcher? _matcher;

    [SetsRequiredMembers]
    public ContextDef(Grammar owner) => Owner = owner;

    public string Name { get; init; } = "";
    public bool IsRoot { get; init; }
    public string? Begin { get; init; }
    public string? End { get; init; }
    public string? Token { get; init; }
    public string? BeginToken { get; init; }
    public string? EndToken { get; init; }
    public bool Multiline { get; init; }
    public string? Embed { get; init; }
    public List<Rule> Rules { get; } = [];

    public LineState Push(LineState? parent) => _states.GetOrAdd(parent ?? (object)this, _ => new LineState(this, parent));

    public Matcher GetMatcher() => _matcher ?? Interlocked.CompareExchange(ref _matcher, Build(), null) ?? _matcher;

    private Matcher Build()
    {
        var alts = new List<Alt>();
        var sb = new StringBuilder();

        void Add(string pattern, Grammar owner, Alt alt)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append("(?<r").Append(alts.Count).Append(">(?").Append(owner.CaseSensitive ? ":" : "i:").Append(pattern).Append("))");
            alts.Add(alt);
        }

        if (End is not null) Add(End, Owner, new Alt(AltKind.End, null, null, Owner));
        var visiting = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Collect(IEnumerable<Rule> rules)
        {
            foreach (var rule in rules)
            {
                switch (rule)
                {
                    case MatchRule { IsWords: true } w: Add(w.Owner.WordPattern, w.Owner, new Alt(AltKind.Word, w.Token, null, w.Owner)); break;
                    case MatchRule m: Add(m.Pattern, m.Owner, new Alt(AltKind.Match, m.Token, null, m.Owner)); break;
                    case ContextDef c: Add(c.Begin!, c.Owner, new Alt(AltKind.Begin, null, c, c.Owner)); break;
                    case IncludeRule i:
                        var target = Resolve(i);
                        if (visiting.Add(target)) Collect(target);
                        break;
                }
            }
        }

        Collect(Rules);
        if (Embed is not null)
        {
            var guest = Owner.Registry.Find(Embed) ?? throw new GrammarException($"{Owner.Id}: context '{Name}' embeds unknown grammar '{Embed}'.");
            if (visiting.Add(guest.Root.Rules)) Collect(guest.Root.Rules);
        }

        if (alts.Count == 0) sb.Append("(?!)");
        var options = RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | (Owner.Registry.CompileRegexes ? RegexOptions.Compiled : RegexOptions.None);
        Regex re;
        try
        {
            re = new Regex(sb.ToString(), options, TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException e)
        {
            throw new GrammarException($"{Owner.Id}: context '{Name}': {e.Message}");
        }
        var groups = new int[alts.Count];
        for (int i = 0; i < groups.Length; i++) groups[i] = re.GroupNumberFromName("r" + i);
        return new Matcher(re, [.. alts], groups);
    }

    private static List<Rule> Resolve(IncludeRule include)
    {
        var owner = include.Owner;
        string target = include.Target;
        int hash = target.IndexOf('#');
        string lang = hash < 0 ? target : target[..hash];
        var grammar = lang.Length == 0 ? owner : owner.Registry.Find(lang) ?? throw new GrammarException($"{owner.Id}: include of unknown grammar '{lang}'.");
        if (hash < 0) return grammar.Root.Rules;
        string name = target[(hash + 1)..];
        return grammar.Repository.TryGetValue(name, out var rules) ? rules : throw new GrammarException($"{owner.Id}: include of unknown repository entry '{target}'.");
    }
}

internal enum AltKind { End, Match, Word, Begin }

internal readonly record struct Alt(AltKind Kind, string? Token, ContextDef? Child, Grammar Owner);

internal sealed record Matcher(Regex Regex, Alt[] Alts, int[] Groups);

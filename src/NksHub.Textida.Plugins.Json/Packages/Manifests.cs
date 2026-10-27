using System.Text.RegularExpressions;

namespace NksHub.Textida.Plugins.Json.Packages;

/// <summary>
/// A dependency found in a manifest, as character offsets of the text. The name span covers the name only (no quotes); the version span covers the version number
/// without the operator in front of it (<c>^</c>, <c>~&gt;</c>, <c>&gt;=</c>). <see cref="VersionStart"/> is -1 when the entry has no version yet.
/// <see cref="Pinned"/> is true when the entry asks for exactly that version, the only case a newer release is marked.
/// </summary>
public sealed record Dependency(string Name, int NameStart, int NameEnd, string? Version, int VersionStart, int VersionEnd, bool Pinned);

/// <summary>
/// Reads the dependencies out of the seven manifest formats. The readers are tolerant on purpose: a name or version that is still being typed (an open quote,
/// a missing value) is found too, so completion works in a file that does not parse yet. They are line or token based, not full parsers.
/// </summary>
public static partial class Manifests
{
    private const string Operators = "^~=<>! ";

    private static Dependency Named(string name, int start) => new(name, start, start + name.Length, null, -1, -1, false);

    /// <summary>An entry whose version text is <paramref name="constraint"/> starting at <paramref name="at"/>; a leading operator is left out of the version span.</summary>
    private static Dependency With(string name, int nameStart, string constraint, int at)
    {
        int skip = 0;
        while (skip < constraint.Length && Operators.Contains(constraint[skip])) skip++;
        var op = constraint[..skip].Trim();
        var rest = constraint[skip..];
        return new Dependency(name, nameStart, nameStart + name.Length, rest, at + skip, at + constraint.Length, op is "" or "=" or "==" && Versions.IsPlain(rest));
    }

    // ---- package.json and composer.json ----

    private sealed class Frame
    {
        public bool IsObject;
        public string? Section;
        public string? Key;
        public bool AfterColon;
        public int Entry = -1;
    }

    /// <summary>The members of the objects named in <paramref name="sections"/> at the top level of a JSON text, with string values as versions.</summary>
    public static IReadOnlyList<Dependency> ReadJson(string text, params string[] sections)
    {
        var result = new List<Dependency>();
        var frames = new List<Frame>();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            var top = frames.Count > 0 ? frames[^1] : null;
            switch (c)
            {
                case '{':
                case '[':
                    frames.Add(new Frame { IsObject = c == '{', Section = frames.Count == 1 && top!.AfterColon && top.Key is { } k && sections.Contains(k) ? k : null });
                    break;
                case '}':
                case ']':
                    if (frames.Count > 0) frames.RemoveAt(frames.Count - 1);
                    break;
                case ':':
                    if (top is { IsObject: true }) top.AfterColon = true;
                    break;
                case ',':
                    if (top is not null) { top.AfterColon = false; top.Entry = -1; }
                    break;
                case '"':
                    int start = i + 1, end = start;
                    while (end < text.Length && text[end] != '"' && text[end] != '\n')
                    {
                        if (text[end] == '\\' && end + 1 < text.Length && text[end + 1] != '\n') end++;
                        end++;
                    }
                    int close = end < text.Length && text[end] == '"' ? end : -1;
                    if (close < 0 && end > start && text[end - 1] == '\r') end--;
                    i = close >= 0 ? close : end - 1;
                    if (top is not { IsObject: true }) break;
                    if (!top.AfterColon)
                    {
                        top.Key = text[start..end];
                        if (top.Section is not null)
                        {
                            top.Entry = result.Count;
                            result.Add(Named(top.Key, start));
                        }
                    }
                    else if (top.Section is not null && top.Entry >= 0)
                        result[top.Entry] = With(top.Key!, result[top.Entry].NameStart, text[start..end], start);
                    break;
            }
        }
        return result;
    }

    public static IReadOnlyList<Dependency> ReadNpm(string text) => ReadJson(text, "dependencies", "devDependencies", "peerDependencies", "optionalDependencies");

    /// <summary>Composer packages are <c>vendor/name</c>; the entries <c>php</c> and <c>ext-*</c> name the platform and are not packages.</summary>
    public static IReadOnlyList<Dependency> ReadComposer(string text) => ReadJson(text, "require", "require-dev");

    // ---- requirements.txt ----

    [GeneratedRegex(@"^(?<name>[A-Za-z0-9][A-Za-z0-9._-]*)(?:\[[^\]\r\n]*\])?[ \t]*(?:(?<op>===|==|~=|!=|<=|>=|<|>)[ \t]*(?<ver>[^\s;,#]*))?", RegexOptions.CultureInvariant)]
    private static partial Regex Requirement();

    public static IReadOnlyList<Dependency> ReadRequirements(string text)
    {
        var result = new List<Dependency>();
        foreach (var (line, offset) in Lines(text))
        {
            int indent = line.Length - line.TrimStart().Length;
            if (indent == line.Length || line[indent] is '#' or '-' || line.Contains("://", StringComparison.Ordinal)) continue;
            var m = Requirement().Match(line[indent..]);
            if (!m.Success) continue;
            var name = m.Groups["name"];
            int at = offset + indent;
            if (!m.Groups["ver"].Success) { result.Add(Named(name.Value, at + name.Index)); continue; }
            var ver = m.Groups["ver"];
            result.Add(new Dependency(name.Value, at + name.Index, at + name.Index + name.Length, ver.Value, at + ver.Index, at + ver.Index + ver.Length,
                m.Groups["op"].Value is "==" or "===" && Versions.IsPlain(ver.Value)));
        }
        return result;
    }

    // ---- .csproj ----

    [GeneratedRegex(@"<PackageReference\b(?<attrs>[^<>]*)", RegexOptions.CultureInvariant)]
    private static partial Regex PackageReference();

    [GeneratedRegex(@"(?<![\w.])(?<key>Include|Version)\s*=\s*""(?<v>[^""<>\r\n]*)", RegexOptions.CultureInvariant)]
    private static partial Regex Attribute();

    public static IReadOnlyList<Dependency> ReadNuGet(string text)
    {
        var result = new List<Dependency>();
        foreach (Match tag in PackageReference().Matches(text))
        {
            var attrs = tag.Groups["attrs"];
            Group? include = null, version = null;
            foreach (Match a in Attribute().Matches(attrs.Value))
            {
                if (a.Groups["key"].Value == "Include") include = a.Groups["v"];
                else version = a.Groups["v"];
            }
            if (include is null) continue;
            int at = attrs.Index;
            result.Add(version is null ? Named(include.Value, at + include.Index) : With(include.Value, at + include.Index, version.Value, at + version.Index));
        }
        return result;
    }

    // ---- go.mod ----

    public static IReadOnlyList<Dependency> ReadGo(string text)
    {
        var result = new List<Dependency>();
        bool block = false;
        foreach (var (full, offset) in Lines(text))
        {
            int comment = full.IndexOf("//", StringComparison.Ordinal);
            var line = comment >= 0 ? full[..comment] : full;
            int indent = line.Length - line.TrimStart().Length;
            var body = line[indent..].TrimEnd();
            if (body.Length == 0) continue;
            int at = offset + indent;
            if (!block && body.StartsWith("require", StringComparison.Ordinal) && (body.Length == 7 || body[7] is ' ' or '\t' or '('))
            {
                body = body[7..];
                at += 7;
                int ws = body.Length - body.TrimStart().Length;
                body = body[ws..];
                at += ws;
                if (body.StartsWith('(')) { block = true; continue; }
            }
            else if (block && body[0] == ')') { block = false; continue; }
            else if (!block) continue;
            int space = body.IndexOfAny([' ', '\t']);
            if (space < 0) { result.Add(Named(body, at)); continue; }
            int vs = space;
            while (vs < body.Length && body[vs] is ' ' or '\t') vs++;
            result.Add(With(body[..space], at, body[vs..], at + vs));
        }
        return result;
    }

    // ---- Cargo.toml ----

    [GeneratedRegex(@"^\s*\[(?<s>[^\]\r\n]+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex TomlHeader();

    [GeneratedRegex(@"^(?:workspace\.)?(?:dev-|build-)?dependencies$|^target\..+\.(?:dev-|build-)?dependencies$", RegexOptions.CultureInvariant)]
    private static partial Regex DependencyTable();

    [GeneratedRegex(@"^(?:workspace\.)?(?:dev-|build-)?dependencies\.(?<n>[^\s.]+)$|^target\..+\.(?:dev-|build-)?dependencies\.(?<n>[^\s.]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex DetailTable();

    [GeneratedRegex(@"^(?<name>[A-Za-z0-9_-]+)[ \t]*(?:=[ \t]*(?:""(?<v>[^""\r\n]*)|\{(?<inline>.*)))?", RegexOptions.CultureInvariant)]
    private static partial Regex CargoEntry();

    [GeneratedRegex(@"\bversion[ \t]*=[ \t]*""(?<v>[^""\r\n]*)", RegexOptions.CultureInvariant)]
    private static partial Regex CargoVersion();

    public static IReadOnlyList<Dependency> ReadCargo(string text)
    {
        var result = new List<Dependency>();
        string? table = null, detail = null;
        int detailName = -1;
        foreach (var (line, offset) in Lines(text))
        {
            if (TomlHeader().Match(line) is { Success: true } header)
            {
                table = header.Groups["s"].Value.Trim();
                var d = DetailTable().Match(table);
                detail = d.Success ? d.Groups["n"].Value : null;
                detailName = d.Success ? offset + header.Groups["s"].Index + header.Groups["s"].Value.IndexOf(detail!, StringComparison.Ordinal) : -1;
                if (detail is not null) result.Add(Named(detail, detailName));
                continue;
            }
            int indent = line.Length - line.TrimStart().Length;
            if (indent == line.Length || line[indent] == '#') continue;
            int at = offset + indent;
            if (detail is not null)
            {
                var v = CargoVersion().Match(line[indent..]);
                if (v.Success && line[indent..].StartsWith("version", StringComparison.Ordinal))
                    result[^1] = With(detail, detailName, v.Groups["v"].Value, at + v.Groups["v"].Index);
                continue;
            }
            if (table is null || !DependencyTable().IsMatch(table)) continue;
            var m = CargoEntry().Match(line[indent..]);
            if (!m.Success) continue;
            var name = m.Groups["name"];
            if (m.Groups["v"].Success) result.Add(With(name.Value, at + name.Index, m.Groups["v"].Value, at + m.Groups["v"].Index));
            else if (m.Groups["inline"].Success && CargoVersion().Match(m.Groups["inline"].Value) is { Success: true } iv)
                result.Add(With(name.Value, at + name.Index, iv.Groups["v"].Value, at + m.Groups["inline"].Index + iv.Groups["v"].Index));
            else result.Add(Named(name.Value, at + name.Index));
        }
        return result;
    }

    // ---- Gemfile ----

    [GeneratedRegex(@"^[ \t]*gem[ \t]+['""](?<name>[^'""\r\n]*)['""]?(?:[ \t]*,[ \t]*['""](?<ver>[^'""\r\n]*))?", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex GemLine();

    public static IReadOnlyList<Dependency> ReadGems(string text)
    {
        var result = new List<Dependency>();
        foreach (Match m in GemLine().Matches(text))
        {
            var name = m.Groups["name"];
            result.Add(m.Groups["ver"].Success ? With(name.Value, name.Index, m.Groups["ver"].Value, m.Groups["ver"].Index) : Named(name.Value, name.Index));
        }
        return result;
    }

    /// <summary>The lines of the text without their line ends, with the character offset where each starts.</summary>
    private static IEnumerable<(string Line, int Offset)> Lines(string text)
    {
        int start = 0;
        while (start <= text.Length)
        {
            int end = text.IndexOf('\n', start);
            if (end < 0) end = text.Length;
            int stop = end > start && text[end - 1] == '\r' ? end - 1 : end;
            yield return (text[start..stop], start);
            start = end + 1;
        }
    }
}

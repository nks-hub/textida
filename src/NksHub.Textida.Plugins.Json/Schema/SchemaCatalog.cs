using System.Reflection;
using System.Text.RegularExpressions;

using NksHub.Textida.Core.Data;
namespace NksHub.Textida.Plugins.Json.Schema;

/// <summary><see cref="Markdown"/> is a Markdown file whose YAML frontmatter is the data.</summary>
public enum DataDialect { Json, Jsonc, Toml, Yaml, Markdown }

/// <summary>
/// A schema the plugin knows. <see cref="FileMatch"/> are globs; a glob without '/' matches the file name, any other is matched against the end of the path. <see cref="Dialect"/> overrides the dialect of the file extension.
/// A schema with a <see cref="Resource"/> is carried by the plugin. Without one, <see cref="Url"/> says where it is: an http(s) address (fetched only after the user allowed that host) or a file path.
/// On a schema that is carried, <see cref="Url"/> is the published address, so a file whose <c>$schema</c> names it gets the carried copy and no request is made.
/// </summary>
public sealed record SchemaEntry(string Id, string Name, string Resource, string[] FileMatch, DataDialect? Dialect = null, string? Url = null)
{
    /// <summary>True when the plugin carries the text of the schema.</summary>
    public bool IsBundled => Resource.Length > 0;
}

/// <summary>A rule of the user: a file, folder or mask (<see cref="Glob"/>) mapped to a schema (an id of a known schema, an address or a file path) and optionally to a dialect.</summary>
public sealed record SchemaMapping(string Glob, string SchemaId, DataDialect? Dialect = null);

/// <summary>What the check of one file found. <see cref="Schema"/> is null when no schema applies (the status line shows "no schema").</summary>
public sealed record SchemaCheckResult(SchemaEntry? Schema, DataDialect Dialect, DataSyntaxException? Syntax, IReadOnlyList<SchemaIssue> Issues);

/// <summary>Known schemas, the user's mappings, and the check of a file. Nothing here touches the network; the text of a schema comes from <see cref="SchemaLoader"/>.</summary>
public sealed partial class SchemaCatalog(IEnumerable<SchemaMapping> userMappings)
{
    public static readonly IReadOnlyList<SchemaEntry> Bundled =
    [
        new("package.json", "package.json", "schemas/package.schema.json", ["package.json"], null, "https://json.schemastore.org/package.json"),
        new("cargo", "Cargo.toml", "schemas/cargo.schema.json", ["Cargo.toml"], null, "https://json.schemastore.org/cargo.json"),
        new("claude-settings", "Claude Code settings", "schemas/claude-settings.schema.json", [".claude/settings*.json"], DataDialect.Json),
        new("mcp", ".mcp.json", "schemas/mcp.schema.json", [".mcp.json"], DataDialect.Json),
        new("codex-config", "Codex config.toml", "schemas/codex-config.schema.json", [".codex/config.toml"], DataDialect.Toml),
        new("opencode", "opencode.json", "schemas/opencode.schema.json", ["opencode.json", "opencode.jsonc"], DataDialect.Jsonc),
        new("github-workflow", "GitHub Actions workflow", "schemas/github-workflow.schema.json", [".github/workflows/*.yml", ".github/workflows/*.yaml"], DataDialect.Yaml, "https://json.schemastore.org/github-workflow.json"),
        new("skill-claude", "SKILL.md (Claude Code)", "schemas/skill-claude.schema.json", [".claude/skills/**/SKILL.md"], DataDialect.Markdown),
        new("skill-agentskills", "SKILL.md (Agent Skills)", "schemas/skill-agentskills.schema.json", ["SKILL.md"], DataDialect.Markdown),
        new("agent-claude", "Claude Code agent", "schemas/agent-claude.schema.json", ["agents/*.md"], DataDialect.Markdown),
        new("rule-claude", "Claude Code rule", "schemas/rule-claude.schema.json", [".claude/rules/**/*.md", "rules/*.md"], DataDialect.Markdown),
        new("rule-cursor", "Cursor rule", "schemas/rule-cursor.schema.json", ["*.mdc"], DataDialect.Markdown),
        new("textida-settings", "Textida settings", "schemas/textida-settings.schema.json", []),
        new("textida-keymap", "Textida keymap", "schemas/textida-keymap.schema.json", []),
    ];

    /// <summary>
    /// A small subset of the JSON Schema Store catalog: the name, the file masks and the address of schemas of common configuration files. Only these facts are carried; the schema itself is
    /// fetched from the address after the user allowed that host, and kept in the schema cache. The dialect is JSONC where the tool that reads the file accepts comments.
    /// </summary>
    public static readonly IReadOnlyList<SchemaEntry> Store =
    [
        new("tsconfig", "tsconfig.json", "", ["tsconfig.json", "tsconfig.*.json"], DataDialect.Jsonc, "https://json.schemastore.org/tsconfig.json"),
        new("jsconfig", "jsconfig.json", "", ["jsconfig.json"], DataDialect.Jsonc, "https://json.schemastore.org/jsconfig.json"),
        new("composer", "composer.json", "", ["composer.json"], null, "https://json.schemastore.org/composer.json"),
        new("eslintrc", ".eslintrc.json", "", [".eslintrc.json"], DataDialect.Jsonc, "https://json.schemastore.org/eslintrc.json"),
        new("prettierrc", ".prettierrc.json", "", [".prettierrc.json"], null, "https://json.schemastore.org/prettierrc.json"),
        new("stylelintrc", ".stylelintrc.json", "", [".stylelintrc.json"], DataDialect.Jsonc, "https://json.schemastore.org/stylelintrc.json"),
        new("appsettings", "appsettings.json", "", ["appsettings.json", "appsettings.*.json"], DataDialect.Jsonc, "https://json.schemastore.org/appsettings.json"),
        new("global-json", "global.json", "", ["global.json"], DataDialect.Jsonc, "https://json.schemastore.org/global.json"),
        new("launchsettings", "launchSettings.json", "", ["launchSettings.json"], DataDialect.Jsonc, "https://json.schemastore.org/launchsettings.json"),
        new("pubspec", "pubspec.yaml", "", ["pubspec.yaml"], DataDialect.Yaml, "https://json.schemastore.org/pubspec.json"),
        new("pyproject", "pyproject.toml", "", ["pyproject.toml"], DataDialect.Toml, "https://json.schemastore.org/pyproject.json"),
        new("dependabot", "Dependabot", "", [".github/dependabot.yml", ".github/dependabot.yaml"], DataDialect.Yaml, "https://json.schemastore.org/dependabot-2.0.json"),
        new("github-action", "GitHub Action", "", ["action.yml", "action.yaml"], DataDialect.Yaml, "https://json.schemastore.org/github-action.json"),
        new("gitlab-ci", "GitLab CI", "", [".gitlab-ci.yml", ".gitlab-ci.yaml"], DataDialect.Yaml, "https://json.schemastore.org/gitlab-ci.json"),
        new("mkdocs", "mkdocs.yml", "", ["mkdocs.yml", "mkdocs.yaml"], DataDialect.Yaml, "https://json.schemastore.org/mkdocs-1.0.json"),
        new("golangci-lint", "golangci-lint", "", [".golangci.yml", ".golangci.yaml"], DataDialect.Yaml, "https://json.schemastore.org/golangci-lint.json"),
    ];

    private SchemaMapping[] _user = [.. userMappings];

    /// <summary>Replaces the rules of the user; a check that is running keeps the rules it started with.</summary>
    public void SetMappings(IEnumerable<SchemaMapping> userMappings) => Volatile.Write(ref _user, [.. userMappings]);

    public static DataDialect DialectOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".toml" => DataDialect.Toml,
        ".jsonc" => DataDialect.Jsonc,
        ".yaml" or ".yml" => DataDialect.Yaml,
        ".md" or ".mdc" => DataDialect.Markdown,
        _ => DataDialect.Json,
    };

    /// <summary>A key that makes two spellings of one published address equal: the host without case, the path without a trailing slash, fragment and <c>.json</c>, and either scheme.</summary>
    private static string AddressKey(string address)
    {
        var text = address.Trim().TrimEnd('#');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return text;
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path = path[..^5];
        return uri.Host.ToLowerInvariant() + path;
    }

    private static string NameOfAddress(string address)
    {
        var text = address.Trim().TrimEnd('#');
        var last = Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? Path.GetFileName(uri.AbsolutePath.TrimEnd('/')) : Path.GetFileName(text.Replace('\\', '/').TrimEnd('/'));
        return last.Length > 0 ? last : text;
    }

    /// <summary>The known schema published at <paramref name="address"/>, or a new entry for an address the catalog does not know (fetched or read when it is used). Null for an empty address.</summary>
    public static SchemaEntry? ByAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > 2048) return null;
        var key = AddressKey(address);
        foreach (var e in Bundled.Concat(Store))
            if (e.Url is not null && AddressKey(e.Url) == key) return e;
        return new SchemaEntry("url:" + address.Trim(), NameOfAddress(address), "", [], null, address.Trim());
    }

    /// <summary>An entry by its id; failing that, by address when the text looks like one (<c>http(s)://</c>, <c>file:</c> or a path with a separator or a <c>.json</c> ending).</summary>
    private static SchemaEntry? Find(string idOrAddress)
    {
        if (Bundled.Concat(Store).FirstOrDefault(b => b.Id == idOrAddress) is { } known) return known;
        bool address = idOrAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || idOrAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || idOrAddress.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || idOrAddress.Contains('/') || idOrAddress.Contains('\\') || idOrAddress.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        return address ? ByAddress(idOrAddress) : null;
    }

    [GeneratedRegex(@"^[ \t]*#[ \t]*yaml-language-server:[ \t]*\$schema=(?<u>\S+)", RegexOptions.Multiline | RegexOptions.CultureInvariant, 200)]
    private static partial Regex YamlModeline();

    [GeneratedRegex(@"^#:schema[ \t]+(?<u>\S+)", RegexOptions.Multiline | RegexOptions.CultureInvariant, 200)]
    private static partial Regex TomlDirective();

    [GeneratedRegex(@"^\s*\{(?:\s*(?://[^\r\n]*|/\*.*?\*/))*\s*""\$schema""\s*:\s*""(?<u>[^""\\\r\n]*)""", RegexOptions.Singleline | RegexOptions.CultureInvariant, 200)]
    private static partial Regex JsonFirstMember();

    /// <summary>
    /// The schema a file names itself: <c>"$schema"</c> at the top of a JSON or JSONC object (found even while the rest of the file does not parse), the comment
    /// <c># yaml-language-server: $schema=ADDRESS</c> in YAML and <c>#:schema ADDRESS</c> in TOML. Null when it names none.
    /// </summary>
    public static string? DeclaredSchema(DataDialect dialect, string text, int maxDepth)
    {
        try
        {
            switch (dialect)
            {
                case DataDialect.Yaml: return YamlModeline().Match(text) is { Success: true } y ? y.Groups["u"].Value : null;
                case DataDialect.Toml: return TomlDirective().Match(text) is { Success: true } t ? t.Groups["u"].Value : null;
                case DataDialect.Json or DataDialect.Jsonc:
                    if (JsonFirstMember().Match(text) is { Success: true } first) return first.Groups["u"].Value;
                    if (text.Contains("\"$schema\"", StringComparison.Ordinal) && JsonDataReader.TryParse(text, dialect == DataDialect.Jsonc, maxDepth, out var node, out _) && node!.Get("$schema") is { Kind: DataKind.String } declared) return declared.Text;
                    return null;
                default: return null;
            }
        }
        catch (RegexMatchTimeoutException) { return null; }
    }

    /// <summary>The schema for <paramref name="path"/> and the dialect of the file; the user's rules come first, in order, then the bundled masks, then the masks of the catalog subset.</summary>
    public (SchemaEntry? Schema, DataDialect Dialect) Resolve(string path) => Resolve(path, null);

    /// <summary>As <see cref="Resolve(string)"/>, but a schema the file names itself (<paramref name="declared"/>, see <see cref="DeclaredSchema"/>) wins over every mask.</summary>
    public (SchemaEntry? Schema, DataDialect Dialect) Resolve(string path, string? declared)
    {
        var dialect = DialectOf(path);
        if (declared is { Length: > 0 } && ByAddress(declared) is { } named) return (named, named.Dialect ?? dialect);
        foreach (var m in Volatile.Read(ref _user))
        {
            if (!Glob.IsMatch(m.Glob, path)) continue;
            if (Find(m.SchemaId) is { } e) return (e, m.Dialect ?? e.Dialect ?? dialect);
        }
        foreach (var b in Bundled.Concat(Store))
            if (b.FileMatch.Any(g => Glob.IsMatch(g, path))) return (b, b.Dialect ?? dialect);
        return (null, dialect);
    }

    public static string LoadSchemaText(SchemaEntry e)
    {
        using var s = typeof(SchemaCatalog).Assembly.GetManifestResourceStream(e.Resource) ?? throw new InvalidOperationException($"Missing schema resource {e.Resource}.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static bool TryParse(string text, DataDialect dialect, int maxDepth, out DataNode? node, out DataSyntaxException? error) => dialect switch
    {
        DataDialect.Toml => TomlDataReader.TryParse(text, maxDepth, out node, out error),
        DataDialect.Yaml => YamlDataReader.TryParse(text, maxDepth, out node, out error),
        DataDialect.Markdown => YamlDataReader.TryParseFrontmatter(text, maxDepth, out node, out error),
        _ => JsonDataReader.TryParse(text, dialect == DataDialect.Jsonc, maxDepth, out node, out error),
    };

    /// <summary>Parses the file in its dialect and checks it against the schema that applies to <paramref name="path"/> (a carried one; a schema that has to be loaded is not checked here, see the overload).</summary>
    public SchemaCheckResult Check(string path, string text, SchemaOptions options)
    {
        var (schema, dialect) = Resolve(path, DeclaredSchema(DialectOf(path), text, options.MaxDepth));
        return Check(path, text, options, schema, dialect, schema is { IsBundled: true } ? LoadSchemaText(schema) : null);
    }

    /// <summary>
    /// Parses the file in <paramref name="dialect"/> and checks it against <paramref name="schemaText"/>, the text of <paramref name="schema"/>. Without the text (a schema that could not be loaded) only the syntax is checked.
    /// </summary>
    public SchemaCheckResult Check(string path, string text, SchemaOptions options, SchemaEntry? schema, DataDialect dialect, string? schemaText)
    {
        DataNode? schemaNode = null;
        if (schema is not null && schemaText is not null && !TryParse(schemaText, DataDialect.Json, options.MaxDepth, out schemaNode, out _) && schema.IsBundled)
            throw new InvalidOperationException($"The bundled schema {schema.Id} is not valid JSON.");
        return Check(path, text, options, schema, dialect, schemaNode);
    }

    /// <summary>As the overload with the text of the schema, for a schema that is parsed already (and whose references were linked, see <see cref="SchemaOptions.Links"/>).</summary>
    public SchemaCheckResult Check(string path, string text, SchemaOptions options, SchemaEntry? schema, DataDialect dialect, DataNode? schemaNode)
    {
        if (!TryParse(text, dialect, options.MaxDepth, out var data, out var syntax)) return new(schema, dialect, syntax, []);
        if (schema is null || schemaNode is null) return new(schema, dialect, null, []);
        var issues = new SchemaValidator(schemaNode, options).Validate(data!);
        if (schema.Id == "skill-agentskills") issues.AddRange(FolderNameIssues(path, data!));
        return new(schema, dialect, null, issues);
    }

    /// <summary>The Agent Skills specification says <c>name</c> must match the parent folder; Claude Code lets it differ, so only that profile asks.</summary>
    private static IEnumerable<SchemaIssue> FolderNameIssues(string path, DataNode data)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || data.Get("name") is not { Kind: DataKind.String } name || name.Text == parts[^2]) yield break;
        yield return new SchemaIssue("folder", $"The name must match the folder name '{parts[^2]}'.", name.Start, name.End, "/name", [parts[^2]]);
    }

    /// <summary>The schema of the property <paramref name="name"/> inside the object schema <paramref name="s"/>: a named property, else the first matching pattern, else <c>additionalProperties</c>.</summary>
    internal static DataNode? ChildSchema(DataNode s, string name, SchemaOptions options)
    {
        if (s.Get("properties")?.Get(name) is { } named) return named;
        if (s.Get("patternProperties") is { Kind: DataKind.Object } patterns)
            foreach (var m in patterns.Members)
            {
                try { if (Regex.IsMatch(name, m.Name, RegexOptions.CultureInvariant, options.RegexTimeout)) return m.Value; }
                catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException) { /* a pattern that cannot be used matches nothing */ }
            }
        return s.Get("additionalProperties") is { Kind: DataKind.Object } any ? any : null;
    }

    /// <summary>
    /// Follows the data from the root to the innermost object or array around <paramref name="offset"/> (with <paramref name="scalars"/>, down to a value that stands there) and returns it together with the schemas that apply to it.
    /// </summary>
    internal static (DataNode Node, List<DataNode> Schemas) Locate(DataNode schemaRoot, DataNode data, int offset, SchemaOptions options, bool scalars)
    {
        var schemas = new List<DataNode> { schemaRoot };
        var node = data;
        while (true)
        {
            DataNode? inner = null;
            foreach (var m in node.Members)
                if (offset > m.NameEnd && offset <= m.Value.End && offset >= m.Value.Start && (scalars || m.Value.Kind is DataKind.Object or DataKind.Array)) { inner = m.Value; schemas = schemas.SelectMany(s => Flatten(schemaRoot, s, options)).Select(s => ChildSchema(s, m.Name, options)).OfType<DataNode>().ToList(); break; }
            if (inner is null && node.Kind == DataKind.Array)
                foreach (var i in node.Items)
                    if (offset >= i.Start && offset <= i.End && (scalars || i.Kind is DataKind.Object or DataKind.Array)) { inner = i; schemas = schemas.SelectMany(s => Flatten(schemaRoot, s, options)).Select(s => s.Get("items")).OfType<DataNode>().ToList(); break; }
            if (inner is null) break;
            node = inner;
        }
        return (node, schemas);
    }

    /// <summary>Property names the schema allows in the object around <paramref name="offset"/> and that are not written there yet.</summary>
    public static IReadOnlyList<string> CompleteKeys(DataNode schemaRoot, DataNode data, int offset, SchemaOptions options)
    {
        var (node, schemas) = Locate(schemaRoot, data, offset, options, scalars: false);
        if (node.Kind != DataKind.Object) return [];
        var existing = node.Members.Select(m => m.Name).ToHashSet();
        return schemas.SelectMany(s => Flatten(schemaRoot, s, options))
            .Where(s => s.Get("properties") is { Kind: DataKind.Object })
            .SelectMany(s => s.Get("properties")!.Members.Select(m => m.Name))
            .Where(n => !existing.Contains(n)).Distinct().Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>The schema with its <c>$ref</c>, <c>allOf</c>, <c>oneOf</c> and <c>anyOf</c> parts folded in, as a list of schemas to read <c>properties</c> from. A schema that is reached twice is listed once, so a reference cycle ends.</summary>
    internal static IEnumerable<DataNode> Flatten(DataNode root, DataNode s, SchemaOptions options) => Flatten(root, s, options, options.MaxDepth, []);

    private static IEnumerable<DataNode> Flatten(DataNode root, DataNode s, SchemaOptions options, int depth, HashSet<DataNode> seen)
    {
        if (depth < 0 || s.Kind != DataKind.Object || !seen.Add(s)) yield break;
        yield return s;
        if (s.Get("$ref") is { Kind: DataKind.String } r && SchemaValidator.TargetOf(root, r, options) is { } t)
            foreach (var x in Flatten(root, t, options, depth - 1, seen)) yield return x;
        foreach (var combinator in new[] { "allOf", "oneOf", "anyOf" }) // alternatives are all candidates for a suggestion
            if (s.Get(combinator) is { Kind: DataKind.Array } parts)
                foreach (var a in parts.Items) foreach (var x in Flatten(root, a, options, depth - 1, seen)) yield return x;
    }
}

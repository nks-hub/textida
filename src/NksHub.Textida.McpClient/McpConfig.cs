using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.McpClient;

/// <summary>The agents whose list of MCP servers the editor reads and writes snippets for.</summary>
public enum McpAgent { ClaudeCode, Cursor, Codex, OpenCode }

public enum McpTransport { Stdio, Remote }

/// <summary>
/// One MCP server as an agent's configuration states it. Values of <paramref name="Env"/> and <paramref name="Headers"/> are kept as written (they are what a conversion and a
/// start need) and are masked everywhere the user or a log sees them.
/// </summary>
/// <param name="RemoteType">The transport word the file used for a remote server (<c>http</c>, <c>sse</c>), kept for a conversion to the same agent.</param>
/// <param name="SourceFile">The file it was read from; null for an entry made in memory.</param>
/// <param name="Scope">Where in that file: <c>user</c>, <c>project</c> or the path of a project of <c>~/.claude.json</c>.</param>
public sealed record McpServerEntry(
    string Name,
    McpAgent Agent,
    McpTransport Transport,
    string? Command,
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string, string> Env,
    string? Url,
    IReadOnlyDictionary<string, string> Headers,
    string? Cwd = null,
    bool Enabled = true,
    string? RemoteType = null,
    string? SourceFile = null,
    string? Scope = null)
{
    /// <summary>The line shown in the list: the command or the address, secrets masked.</summary>
    public string Summary
    {
        get
        {
            var secrets = Env.Values.Concat(Headers.Values).Where(v => !McpText.IsReference(v));
            return Transport == McpTransport.Remote
                ? McpText.MaskUrl(Url ?? "")
                : McpText.MaskSecrets(string.Join(' ', new[] { Command ?? "" }.Concat(Args)), secrets);
        }
    }
}

/// <summary>What the scan of the configuration files found, and which files it could not read.</summary>
public sealed record McpScan(IReadOnlyList<McpServerEntry> Servers, IReadOnlyList<McpScanProblem> Problems);

public sealed record McpScanProblem(string File, string Message);

public enum McpNoteKind
{
    /// <summary>The target has no setting for the working directory, so it is not in the snippet.</summary>
    WorkingDirectoryDropped,
    /// <summary>The server is switched off in the source and the target has no such flag, so it will be on.</summary>
    DisabledDropped,
    /// <summary>Codex allows only letters, digits, - and _ in a name; <see cref="McpNote.Detail"/> is the name used.</summary>
    Renamed,
}

/// <summary>Something a conversion could not keep; the caller words it for the user.</summary>
public sealed record McpNote(McpNoteKind Kind, string Server, string? Detail);

/// <summary>A snippet in the format of one agent, and what that format could not carry over.</summary>
public sealed record McpRendering(string Text, IReadOnlyList<McpNote> Notes);

/// <summary>
/// Reads the MCP server lists of Claude Code (<c>.mcp.json</c>, <c>~/.claude.json</c>), Cursor (<c>.cursor/mcp.json</c>), Codex (<c>~/.codex/config.toml</c>) and OpenCode
/// (<c>opencode.json</c> or <c>opencode.jsonc</c>) and writes a snippet for any of them. Files are only opened for reading; nothing here writes to a file of another tool.
/// </summary>
public static class McpConfig
{
    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    // ---- finding and reading files ----

    /// <summary>
    /// Reads every list that exists: the files of the user's folder and the files of each folder in <paramref name="projectDirectories"/> (the caller passes the folder of
    /// the document and its parents). A file that cannot be read is a problem, not a failure of the scan.
    /// </summary>
    public static McpScan Scan(string home, IEnumerable<string> projectDirectories)
    {
        var servers = new List<McpServerEntry>();
        var problems = new List<McpScanProblem>();
        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        void Read(McpAgent agent, string path, string scope)
        {
            var full = Path.GetFullPath(path);
            if (!File.Exists(full) || !seen.Add(full)) return;
            try
            {
                var file = ReadFile(agent, full, scope);
                servers.AddRange(file.Entries);
                problems.AddRange(file.Problems.Select(p => new McpScanProblem(full, p)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or JsonException)
            {
                problems.Add(new McpScanProblem(full, e.Message));
            }
        }

        foreach (var dir in projectDirectories)
        {
            Read(McpAgent.ClaudeCode, Path.Combine(dir, ".mcp.json"), "project");
            Read(McpAgent.Cursor, Path.Combine(dir, ".cursor", "mcp.json"), "project");
            Read(McpAgent.OpenCode, Path.Combine(dir, "opencode.json"), "project");
            Read(McpAgent.OpenCode, Path.Combine(dir, "opencode.jsonc"), "project");
        }
        Read(McpAgent.ClaudeCode, Path.Combine(home, ".claude.json"), "user");
        Read(McpAgent.Cursor, Path.Combine(home, ".cursor", "mcp.json"), "user");
        Read(McpAgent.Codex, Path.Combine(home, ".codex", "config.toml"), "user");
        Read(McpAgent.OpenCode, Path.Combine(home, ".config", "opencode", "opencode.json"), "user");
        Read(McpAgent.OpenCode, Path.Combine(home, ".config", "opencode", "opencode.jsonc"), "user");
        return new McpScan(servers, problems);
    }

    /// <summary>The folder and every parent of it, nearest first; where agents look for a project's files.</summary>
    public static IEnumerable<string> WithParents(string directory)
    {
        for (var dir = new DirectoryInfo(directory); dir is not null; dir = dir.Parent) yield return dir.FullName;
    }

    private static McpFileRead ReadFile(McpAgent agent, string path, string scope)
    {
        string text;
        // shared with writers: an agent may be saving its file while the list is read
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(fs, Encoding.UTF8, true))
            text = reader.ReadToEnd();
        return Read(agent, text, path, scope);
    }

    public sealed record McpFileRead(IReadOnlyList<McpServerEntry> Entries, IReadOnlyList<string> Problems);

    /// <summary>Parses the text of one configuration file. Throws <see cref="FormatException"/> or <see cref="JsonException"/> when the file as a whole is not valid.</summary>
    public static McpFileRead Read(McpAgent agent, string text, string? sourceFile = null, string? scope = null)
    {
        var entries = new List<McpServerEntry>();
        var problems = new List<string>();
        if (agent == McpAgent.Codex)
        {
            var root = MiniToml.Parse(text);
            if (root.TryGetValue("mcp_servers", out var table) && table is Dictionary<string, object> servers)
                foreach (var (name, value) in servers)
                    Add(entries, problems, name, value is Dictionary<string, object> t ? FromToml(name, t, sourceFile, scope) : null);
            return new McpFileRead(entries, problems);
        }
        using var doc = JsonDocument.Parse(text, Lenient);
        var top = doc.RootElement;
        if (top.ValueKind != JsonValueKind.Object) throw new FormatException("The file does not hold a JSON object.");
        if (agent == McpAgent.OpenCode)
        {
            if (top.TryGetProperty("mcp", out var mcp) && mcp.ValueKind == JsonValueKind.Object)
                foreach (var p in mcp.EnumerateObject()) Add(entries, problems, p.Name, FromOpenCode(p.Name, p.Value, sourceFile, scope));
            return new McpFileRead(entries, problems);
        }
        void FromServers(JsonElement servers, string? entryScope)
        {
            if (servers.ValueKind != JsonValueKind.Object) return;
            foreach (var p in servers.EnumerateObject()) Add(entries, problems, p.Name, FromJson(agent, p.Name, p.Value, sourceFile, entryScope));
        }
        if (top.TryGetProperty("mcpServers", out var list)) FromServers(list, scope);
        // ~/.claude.json keeps the servers of a project (local scope) under projects/<path>/mcpServers
        if (agent == McpAgent.ClaudeCode && top.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Object)
            foreach (var project in projects.EnumerateObject())
                if (project.Value.ValueKind == JsonValueKind.Object && project.Value.TryGetProperty("mcpServers", out var local)) FromServers(local, project.Name);
        return new McpFileRead(entries, problems);
    }

    private static void Add(List<McpServerEntry> entries, List<string> problems, string name, McpServerEntry? entry)
    {
        if (entry is null) problems.Add($"'{name}' has neither a command nor an address.");
        else entries.Add(entry);
    }

    private static IReadOnlyDictionary<string, string> StringMap(JsonElement e, string member)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (e.TryGetProperty(member, out var obj) && obj.ValueKind == JsonValueKind.Object)
            foreach (var p in obj.EnumerateObject()) map[p.Name] = Scalar(p.Value);
        return map;
    }

    private static string Scalar(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString()!,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "",
        _ => v.GetRawText(),
    };

    private static string? Str(JsonElement e, string member) => e.TryGetProperty(member, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static McpServerEntry? FromJson(McpAgent agent, string name, JsonElement e, string? file, string? scope)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        bool enabled = !(e.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True);
        if (Str(e, "command") is { Length: > 0 } command)
        {
            var args = e.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(Scalar).ToList() : [];
            return new McpServerEntry(name, agent, McpTransport.Stdio, command, args, StringMap(e, "env"), null, new Dictionary<string, string>(), Str(e, "cwd"), enabled, null, file, scope);
        }
        if (Str(e, "url") is { Length: > 0 } url)
            return new McpServerEntry(name, agent, McpTransport.Remote, null, [], new Dictionary<string, string>(), url, StringMap(e, "headers"), null, enabled, Str(e, "type"), file, scope);
        return null;
    }

    private static McpServerEntry? FromOpenCode(string name, JsonElement e, string? file, string? scope)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        bool enabled = !(e.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False);
        if (e.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.Array && c.GetArrayLength() > 0)
        {
            var parts = c.EnumerateArray().Select(Scalar).ToList();
            return new McpServerEntry(name, McpAgent.OpenCode, McpTransport.Stdio, parts[0], parts.Skip(1).ToList(), StringMap(e, "environment"), null, new Dictionary<string, string>(), null, enabled, null, file, scope);
        }
        if (Str(e, "url") is { Length: > 0 } url)
            return new McpServerEntry(name, McpAgent.OpenCode, McpTransport.Remote, null, [], new Dictionary<string, string>(), url, StringMap(e, "headers"), null, enabled, null, file, scope);
        return null;
    }

    private static IReadOnlyDictionary<string, string> TomlMap(Dictionary<string, object> t, string member)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (t.TryGetValue(member, out var v) && v is Dictionary<string, object> table)
            foreach (var (key, value) in table) map[key] = value is bool b ? (b ? "true" : "false") : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        return map;
    }

    private static McpServerEntry? FromToml(string name, Dictionary<string, object> t, string? file, string? scope)
    {
        bool enabled = !(t.TryGetValue("enabled", out var en) && en is false);
        t.TryGetValue("cwd", out var cwd);
        if (t.TryGetValue("command", out var c) && c is string { Length: > 0 } command)
        {
            var args = t.TryGetValue("args", out var a) && a is List<object> list ? list.Select(x => Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture) ?? "").ToList() : [];
            return new McpServerEntry(name, McpAgent.Codex, McpTransport.Stdio, command, args, TomlMap(t, "env"), null, new Dictionary<string, string>(), cwd as string, enabled, null, file, scope);
        }
        if (t.TryGetValue("url", out var u) && u is string { Length: > 0 } url)
        {
            var headers = new Dictionary<string, string>(TomlMap(t, "http_headers"), StringComparer.Ordinal);
            if (t.TryGetValue("bearer_token_env_var", out var token) && token is string { Length: > 0 } variable) headers["Authorization"] = "Bearer ${" + variable + "}";
            return new McpServerEntry(name, McpAgent.Codex, McpTransport.Remote, null, [], new Dictionary<string, string>(), url, headers, null, enabled, null, file, scope);
        }
        return null;
    }

    // ---- writing a snippet ----

    public static string FileHint(McpAgent agent) => agent switch
    {
        McpAgent.ClaudeCode => ".mcp.json",
        McpAgent.Cursor => ".cursor/mcp.json",
        McpAgent.Codex => "~/.codex/config.toml",
        _ => "opencode.json",
    };

    /// <summary>
    /// The entries in the format of <paramref name="target"/>, ready to paste into its file. With <paramref name="maskValues"/> every value of the environment and of the headers
    /// is replaced by <c>***</c> (a plain <c>${NAME}</c> reference stays), so the snippet can be shown or logged; without it the snippet is complete.
    /// </summary>
    public static McpRendering Render(IReadOnlyList<McpServerEntry> entries, McpAgent target, bool maskValues)
    {
        var notes = new List<McpNote>();
        string Value(string v) => maskValues ? McpText.MaskValue(v) : v;
        // an address can carry a key in its query string, and an argument can repeat a value of the environment
        if (maskValues)
            entries = [.. entries.Select(e => e with
            {
                Url = e.Url is null ? null : McpText.MaskUrl(e.Url),
                Args = [.. e.Args.Select(a => McpText.MaskSecrets(a, e.Env.Values.Concat(e.Headers.Values).Where(v => !McpText.IsReference(v))))],
            })];
        if (target == McpAgent.Codex) return new McpRendering(RenderToml(entries, notes, Value), notes);

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteStartObject(target == McpAgent.OpenCode ? "mcp" : "mcpServers");
            foreach (var e in entries)
            {
                w.WriteStartObject(e.Name);
                if (target == McpAgent.OpenCode) WriteOpenCode(w, e, notes, Value);
                else WriteClaudeOrCursor(w, e, target, notes, Value);
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return new McpRendering(Encoding.UTF8.GetString(buffer.WrittenSpan), notes);
    }

    private static void WriteMap(Utf8JsonWriter w, string name, IReadOnlyDictionary<string, string> map, Func<string, string> value)
    {
        if (map.Count == 0) return;
        w.WriteStartObject(name);
        foreach (var (k, v) in map) w.WriteString(k, value(v));
        w.WriteEndObject();
    }

    private static void WriteClaudeOrCursor(Utf8JsonWriter w, McpServerEntry e, McpAgent target, List<McpNote> notes, Func<string, string> value)
    {
        if (e.Transport == McpTransport.Stdio)
        {
            if (target == McpAgent.ClaudeCode) w.WriteString("type", "stdio");
            w.WriteString("command", e.Command);
            w.WriteStartArray("args");
            foreach (var a in e.Args) w.WriteStringValue(a);
            w.WriteEndArray();
            WriteMap(w, "env", e.Env, value);
            if (e.Cwd is not null) notes.Add(new McpNote(McpNoteKind.WorkingDirectoryDropped, e.Name, null));
        }
        else
        {
            if (target == McpAgent.ClaudeCode) w.WriteString("type", e.RemoteType ?? "http");
            w.WriteString("url", e.Url);
            WriteMap(w, "headers", e.Headers, value);
        }
        if (!e.Enabled) notes.Add(new McpNote(McpNoteKind.DisabledDropped, e.Name, null));
    }

    private static void WriteOpenCode(Utf8JsonWriter w, McpServerEntry e, List<McpNote> notes, Func<string, string> value)
    {
        if (e.Transport == McpTransport.Stdio)
        {
            w.WriteString("type", "local");
            w.WriteStartArray("command");
            w.WriteStringValue(e.Command);
            foreach (var a in e.Args) w.WriteStringValue(a);
            w.WriteEndArray();
            WriteMap(w, "environment", e.Env, value);
            if (e.Cwd is not null) notes.Add(new McpNote(McpNoteKind.WorkingDirectoryDropped, e.Name, null));
        }
        else
        {
            w.WriteString("type", "remote");
            w.WriteString("url", e.Url);
            WriteMap(w, "headers", e.Headers, value);
        }
        w.WriteBoolean("enabled", e.Enabled);
    }

    /// <summary>Names of Codex tables are bare keys, so anything else is replaced by an underscore.</summary>
    public static string CodexName(string name) => new([.. name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_')]);

    private static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
            sb.Append(c switch { '"' => "\\\"", '\\' => "\\\\", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t", < ' ' => $"\\u{(int)c:X4}", _ => c.ToString() });
        return sb.Append('"').ToString();
    }

    private static string Key(string key) => key.Length > 0 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? key : Quote(key);

    private static string RenderToml(IReadOnlyList<McpServerEntry> entries, List<McpNote> notes, Func<string, string> value)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            var name = CodexName(e.Name);
            if (name != e.Name) notes.Add(new McpNote(McpNoteKind.Renamed, e.Name, name));
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("[mcp_servers.").Append(name).Append("]\n");
            var tables = new StringBuilder(); // sub-tables must come after every plain key of the server, or the keys would land in them
            if (e.Transport == McpTransport.Stdio)
            {
                sb.Append("command = ").Append(Quote(e.Command ?? "")).Append('\n');
                if (e.Args.Count > 0) sb.Append("args = [").Append(string.Join(", ", e.Args.Select(Quote))).Append("]\n");
                if (e.Cwd is not null) sb.Append("cwd = ").Append(Quote(e.Cwd)).Append('\n');
                if (e.Env.Count > 0)
                {
                    tables.Append("\n[mcp_servers.").Append(name).Append(".env]\n");
                    foreach (var (k, v) in e.Env) tables.Append(Key(k)).Append(" = ").Append(Quote(value(v))).Append('\n');
                }
            }
            else
            {
                sb.Append("url = ").Append(Quote(e.Url ?? "")).Append('\n');
                var headers = e.Headers.ToDictionary(h => h.Key, h => h.Value);
                if (headers.TryGetValue("Authorization", out var auth) && auth.StartsWith("Bearer ${", StringComparison.Ordinal) && McpText.IsReference(auth["Bearer ".Length..]))
                {
                    sb.Append("bearer_token_env_var = ").Append(Quote(auth["Bearer ${".Length..^1])).Append('\n');
                    headers.Remove("Authorization");
                }
                if (headers.Count > 0)
                {
                    tables.Append("\n[mcp_servers.").Append(name).Append(".http_headers]\n");
                    foreach (var (k, v) in headers) tables.Append(Key(k)).Append(" = ").Append(Quote(value(v))).Append('\n');
                }
            }
            if (!e.Enabled) sb.Append("enabled = false\n");
            sb.Append(tables);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Checks a snippet against the format of <paramref name="target"/> by reading it back the way the editor reads that agent's file: it must parse, hold at least one server,
    /// and every server must have a command or an address. Returns the problems; an empty list means the snippet is valid.
    /// </summary>
    public static IReadOnlyList<string> Check(McpAgent target, string snippet)
    {
        try
        {
            var read = Read(target, snippet);
            var problems = new List<string>(read.Problems);
            if (read.Entries.Count == 0 && problems.Count == 0) problems.Add("The snippet holds no server.");
            foreach (var e in read.Entries)
                if (target == McpAgent.Codex && CodexName(e.Name) != e.Name) problems.Add($"'{e.Name}' is not a valid Codex table name.");
            return problems;
        }
        catch (Exception e) when (e is FormatException or JsonException) { return [e.Message]; }
    }

    // ---- starting ----

    /// <summary>
    /// Everything needed to start a stdio entry: <c>${NAME}</c> and <c>${NAME:-default}</c> are replaced from <paramref name="variables"/>, the program is looked up in the PATH only
    /// and refused if it is a batch file (an <c>npx</c> or <c>npm</c> shim of Node.js is replaced by <c>node</c> and the script the shim runs). Nothing is started here.
    /// </summary>
    /// <exception cref="McpClientException">The entry is remote, a variable has no value or the program is not found.</exception>
    public static McpLaunch BuildLaunch(McpServerEntry entry, Func<string, string?> variables, string defaultDirectory)
    {
        if (entry.Transport != McpTransport.Stdio || string.IsNullOrWhiteSpace(entry.Command))
            throw new McpClientException("Only a server that the editor can start (a command) can be tried; this one is reached by an address.");
        var env = entry.Env.ToDictionary(e => e.Key, e => Expand(e.Value, variables));
        var args = entry.Args.Select(a => Expand(a, variables)).ToList();
        var command = Expand(entry.Command, variables);
        if (NodeShim(command, args) is { } shim) (command, args) = shim;
        try
        {
            // the variables are done; a dollar sign left in the text is literal and must survive the tool resolver
            string Literal(string s) => s.Replace("$", "$$", StringComparison.Ordinal);
            var cwd = entry.Cwd is { Length: > 0 } c ? Literal(Expand(c, variables)) : null;
            var tool = ToolRunner.Resolve(new ToolSpec("mcp:" + entry.Name, Literal(command), [.. args.Select(Literal)], cwd), _ => null, defaultDirectory);
            if (ToolRunner.IsBatch(tool.Executable))
                throw new McpClientException($"{tool.Executable} is a batch file; an MCP server has to be started as a program, not through cmd.exe.");
            return new McpLaunch(tool.Executable, [.. args], tool.WorkingDirectory, env, [.. env.Values.Where(v => v.Length > 0)]);
        }
        catch (ToolException e) { throw new McpClientException(e.Message); }
    }

    /// <summary>Replaces <c>${NAME}</c> and <c>${NAME:-default}</c>; a name without a value and without a default is an error.</summary>
    public static string Expand(string text, Func<string, string?> variables)
    {
        if (!text.Contains("${", StringComparison.Ordinal)) return text;
        var sb = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            int close = text[i] == '$' && i + 1 < text.Length && text[i + 1] == '{' ? text.IndexOf('}', i) : -1;
            if (close < 0) { sb.Append(text[i]); continue; }
            var inner = text[(i + 2)..close];
            var split = inner.IndexOf(":-", StringComparison.Ordinal);
            var name = split < 0 ? inner : inner[..split];
            var value = variables(name) ?? (split < 0 ? null : inner[(split + 2)..])
                ?? throw new McpClientException($"The variable ${{{name}}} has no value.");
            sb.Append(value);
            i = close;
        }
        return sb.ToString();
    }

    private static (string Command, List<string> Args)? NodeShim(string command, List<string> args)
    {
        if (!OperatingSystem.IsWindows() || command.IndexOfAny(['/', '\\']) >= 0) return null;
        var name = Path.GetFileNameWithoutExtension(command);
        if (!name.Equals("npx", StringComparison.OrdinalIgnoreCase) && !name.Equals("npm", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var shim = Path.Combine(dir.Trim('"'), name + ".cmd");
                if (!File.Exists(shim)) continue;
                var node = Path.Combine(Path.GetDirectoryName(shim)!, "node.exe");
                var script = Path.Combine(Path.GetDirectoryName(shim)!, "node_modules", "npm", "bin", name.ToLowerInvariant() + "-cli.js");
                return File.Exists(node) && File.Exists(script) ? (node, [script, .. args]) : null;
            }
            catch (ArgumentException) { }
        }
        return null;
    }
}

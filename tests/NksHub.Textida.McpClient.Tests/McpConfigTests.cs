using NksHub.Textida.McpClient;

namespace NksHub.Textida.McpClient.Tests;

/// <summary>P6.4.2: one list of MCP servers from the files of four agents, read-only; a snippet for any of them; secrets masked. All files are fixtures in a temp folder.</summary>
public sealed class McpConfigTests : IDisposable
{
    private static readonly string[] Secrets = ["abc-secret-123", "hdr-secret-456", "urlsecret", "cur-secret-789", "codex-secret-1", "oc-secret-2", "oc-secret-3"];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "textida-mcpcfg-" + Guid.NewGuid().ToString("N"));
    private string Home => Path.Combine(_root, "home");
    private string Project => Path.Combine(_root, "proj");

    public McpConfigTests()
    {
        Write(Path.Combine(Project, ".mcp.json"), """
            { "mcpServers": {
              "files": { "command": "npx", "args": ["-y", "@scope/server-files", "/data"], "env": { "API_KEY": "abc-secret-123", "MODE": "${MODE_REF}" } },
              "remote": { "type": "http", "url": "https://example.com/mcp?key=urlsecret", "headers": { "Authorization": "Bearer hdr-secret-456" } }
            } }
            """);
        Write(Path.Combine(Project, ".cursor", "mcp.json"), """{ "mcpServers": { "cur": { "command": "uvx", "args": ["srv"], "env": { "TOKEN": "cur-secret-789" } } } }""");
        Write(Path.Combine(Project, "opencode.jsonc"), """
            {
              // comments and trailing commas are allowed in opencode.jsonc
              "mcp": {
                "oc": { "type": "local", "command": ["bunx", "pkg", "--flag"], "environment": { "K": "oc-secret-2" }, "enabled": false, },
                "oc-remote": { "type": "remote", "url": "https://example.net/mcp", "headers": { "X-Key": "oc-secret-3" } },
              },
            }
            """);
        Write(Path.Combine(Home, ".claude.json"), """
            { "numStartups": 3, "mcpServers": { "user-one": { "type": "stdio", "command": "node", "args": ["s.js"] } },
              "projects": { "C:/work/a": { "mcpServers": { "local-one": { "command": "py", "args": [] } } } } }
            """);
        Write(Path.Combine(Home, ".codex", "config.toml"), """
            model = "gpt"  # not a server
            notes = '''
            [not_a_table]
            '''

            [mcp_servers.docs]
            command = "npx"
            args = ["-y", "docs-server"]
            env = { DOCS_KEY = "codex-secret-1" }

            [mcp_servers.web]
            url = "https://example.org/mcp"
            bearer_token_env_var = "WEB_TOKEN"

            [mcp_servers.other.env]
            A = "b"

            [mcp_servers.other]
            command = "other"
            cwd = 'C:\tools'
            """);
    }

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private McpScan Scan() => McpConfig.Scan(Home, [Project]);

    [Fact]
    public void The_scan_makes_one_list_from_all_formats()
    {
        var scan = Scan();
        Assert.Empty(scan.Problems);
        Assert.Equal(
            ["cur", "docs", "files", "local-one", "oc", "oc-remote", "other", "remote", "user-one", "web"],
            scan.Servers.Select(s => s.Name).Order().ToArray());

        var files = scan.Servers.Single(s => s.Name == "files");
        Assert.Equal(McpAgent.ClaudeCode, files.Agent);
        Assert.Equal("npx", files.Command);
        Assert.Equal(["-y", "@scope/server-files", "/data"], files.Args);
        Assert.Equal("abc-secret-123", files.Env["API_KEY"]);
        Assert.Equal("project", files.Scope);
        Assert.EndsWith(".mcp.json", files.SourceFile);

        var remote = scan.Servers.Single(s => s.Name == "remote");
        Assert.Equal((McpTransport.Remote, "http", "Bearer hdr-secret-456"), (remote.Transport, remote.RemoteType, remote.Headers["Authorization"]));

        Assert.Equal("user", scan.Servers.Single(s => s.Name == "user-one").Scope);
        Assert.Equal("C:/work/a", scan.Servers.Single(s => s.Name == "local-one").Scope);
        Assert.Equal(McpAgent.Cursor, scan.Servers.Single(s => s.Name == "cur").Agent);

        var oc = scan.Servers.Single(s => s.Name == "oc");
        Assert.Equal((McpAgent.OpenCode, "bunx", false), (oc.Agent, oc.Command, oc.Enabled));
        Assert.Equal(["pkg", "--flag"], oc.Args);

        var docs = scan.Servers.Single(s => s.Name == "docs");
        Assert.Equal((McpAgent.Codex, "codex-secret-1"), (docs.Agent, docs.Env["DOCS_KEY"]));
        Assert.Equal("Bearer ${WEB_TOKEN}", scan.Servers.Single(s => s.Name == "web").Headers["Authorization"]);
        var other = scan.Servers.Single(s => s.Name == "other");
        Assert.Equal(("other", "b", @"C:\tools"), (other.Command, other.Env["A"], other.Cwd));
    }

    [Fact]
    public void Reading_never_changes_a_file_and_works_on_a_file_another_program_holds_open()
    {
        var files = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToList();
        var before = files.ToDictionary(f => f, f => (File.ReadAllBytes(f), File.GetLastWriteTimeUtc(f)));
        foreach (var f in files) File.SetAttributes(f, FileAttributes.ReadOnly); // a write would throw
        using var holder = new FileStream(Path.Combine(Home, ".claude.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Assert.Equal(10, Scan().Servers.Count);
        Assert.Equal(files.Order(), Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Order());
        foreach (var f in files) Assert.Equal(before[f], (File.ReadAllBytes(f), File.GetLastWriteTimeUtc(f)), new PairComparer());
    }

    private sealed class PairComparer : IEqualityComparer<(byte[], DateTime)>
    {
        public bool Equals((byte[], DateTime) x, (byte[], DateTime) y) => x.Item1.AsSpan().SequenceEqual(y.Item1) && x.Item2 == y.Item2;
        public int GetHashCode((byte[], DateTime) obj) => obj.Item2.GetHashCode();
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_a_problem_and_the_others_still_count()
    {
        Write(Path.Combine(Project, ".mcp.json"), "{ not json");
        Write(Path.Combine(Home, ".codex", "config.toml"), "[mcp_servers.x\ncommand = 1");
        var scan = Scan();
        Assert.Equal(2, scan.Problems.Count);
        Assert.Contains(scan.Problems, p => p.File.EndsWith("config.toml") && p.Message.Contains("TOML line"));
        Assert.Equal(["cur", "local-one", "oc", "oc-remote", "user-one"], scan.Servers.Select(s => s.Name).Order().ToArray());
    }

    [Fact]
    public void An_entry_without_command_and_address_is_reported_not_listed()
    {
        var read = McpConfig.Read(McpAgent.Cursor, """{ "mcpServers": { "empty": { "args": [] }, "ok": { "command": "x" } } }""");
        Assert.Equal(["ok"], read.Entries.Select(e => e.Name));
        Assert.Contains("'empty'", Assert.Single(read.Problems));
    }

    [Fact]
    public void Parent_folders_are_searched_nearest_first()
    {
        var dirs = McpConfig.WithParents(Path.Combine(Project, "a", "b")).Take(3).ToList();
        Assert.Equal([Path.Combine(Project, "a", "b"), Path.Combine(Project, "a"), Project], dirs);
    }

    // ---- masking ----

    [Fact]
    public void The_list_and_the_command_never_show_a_secret()
    {
        foreach (var entry in Scan().Servers)
        {
            Assert.DoesNotContain(Secrets, s => entry.Summary.Contains(s, StringComparison.Ordinal));
        }
        var files = Scan().Servers.Single(s => s.Name == "files");
        var launch = new McpLaunch("C:\\x\\npx.exe", ["--token", "abc-secret-123"], "C:\\x", files.Env, [.. files.Env.Values]);
        Assert.DoesNotContain("abc-secret-123", launch.CommandLine);
        Assert.Contains("***", launch.CommandLine);
        Assert.Equal(["API_KEY=***", "MODE=${MODE_REF}"], launch.MaskedEnvironment); // a plain reference holds no secret
        Assert.Equal("https://example.com/mcp?key=***", Scan().Servers.Single(s => s.Name == "remote").Summary);
        Assert.Equal("https://***@host/p?a=***&b", McpText.MaskUrl("https://user:pw@host/p?a=1&b"));
    }

    // ---- conversion ----

    private static void AssertSame(McpServerEntry a, McpServerEntry b, bool masked)
    {
        Assert.Equal(McpConfig.CodexName(a.Name) == b.Name ? b.Name : a.Name, b.Name);
        Assert.Equal((a.Transport, a.Command, masked && a.Url is not null ? McpText.MaskUrl(a.Url) : a.Url), (b.Transport, b.Command, b.Url));
        Assert.Equal(a.Args, b.Args);
        Assert.True(a.Env.Keys.Order().SequenceEqual(b.Env.Keys.Order()), $"{a.Name} ({a.Agent}) -> {b.Agent}: env {string.Join(',', a.Env.Keys)} vs {string.Join(',', b.Env.Keys)}");
        Assert.Equal(a.Headers.Keys.Order(), b.Headers.Keys.Order());
        foreach (var (k, v) in a.Headers) Assert.Equal(masked ? McpText.MaskValue(v) : v, b.Headers[k]);
        foreach (var (k, v) in a.Env) Assert.Equal(masked ? McpText.MaskValue(v) : v, b.Env[k]);
    }

    [Fact]
    public void Every_entry_converts_to_every_agent_and_passes_the_check_of_the_target_format()
    {
        var all = Scan().Servers;
        foreach (var target in Enum.GetValues<McpAgent>())
        {
            foreach (var entry in all)
            {
                foreach (var masked in new[] { false, true })
                {
                    var snippet = McpConfig.Render([entry], target, masked);
                    Assert.True(McpConfig.Check(target, snippet.Text).Count == 0, $"{entry.Name} -> {target}: {string.Join("; ", McpConfig.Check(target, snippet.Text))}\n{snippet.Text}");
                    var back = Assert.Single(McpConfig.Read(target, snippet.Text).Entries);
                    Assert.Equal(target, back.Agent);
                    AssertSame(entry, back, masked);
                    if (masked) Assert.DoesNotContain(Secrets, s => snippet.Text.Contains(s, StringComparison.Ordinal));
                }
            }
        }
    }

    [Fact]
    public void A_snippet_of_several_servers_keeps_them_all_and_what_the_target_cannot_hold_is_said()
    {
        var all = Scan().Servers;
        var codex = McpConfig.Render(all, McpAgent.Codex, maskValues: true);
        Assert.Equal(all.Count, McpConfig.Read(McpAgent.Codex, codex.Text).Entries.Count);
        Assert.Contains("bearer_token_env_var = \"WEB_TOKEN\"", codex.Text);
        Assert.Contains("DOCS_KEY = \"***\"", codex.Text);

        var cursor = McpConfig.Render(all, McpAgent.Cursor, maskValues: true);
        Assert.Contains(cursor.Notes, n => n.Contains("'other'") && n.Contains("working directory"));
        Assert.Contains(cursor.Notes, n => n.Contains("'oc'") && n.Contains("switched off"));
        Assert.Contains("\"type\": \"stdio\"", McpConfig.Render([all.Single(s => s.Name == "user-one")], McpAgent.ClaudeCode, true).Text);
    }

    [Fact]
    public void Codex_table_names_are_made_valid_and_the_check_rejects_a_bad_snippet()
    {
        var entry = new McpServerEntry("my server.v2", McpAgent.Cursor, McpTransport.Stdio, "x", [], new Dictionary<string, string>(), null, new Dictionary<string, string>());
        var codex = McpConfig.Render([entry], McpAgent.Codex, false);
        Assert.Contains("[mcp_servers.my_server_v2]", codex.Text);
        Assert.Contains(codex.Notes, n => n.Contains("my_server_v2"));
        Assert.Empty(McpConfig.Check(McpAgent.Codex, codex.Text));

        Assert.NotEmpty(McpConfig.Check(McpAgent.Codex, "[mcp_servers.\"bad name\"]\ncommand = \"x\"\n"));
        Assert.NotEmpty(McpConfig.Check(McpAgent.Cursor, "{ \"mcpServers\": {} }"));
        Assert.NotEmpty(McpConfig.Check(McpAgent.OpenCode, "not json"));
        Assert.NotEmpty(McpConfig.Check(McpAgent.ClaudeCode, """{ "mcpServers": { "x": { "args": [] } } }"""));
    }

    // ---- toml ----

    [Fact]
    public void Toml_reads_what_agent_files_use()
    {
        var t = MiniToml.Parse(""""
            a = 1
            b.c = "d\u00e9\n"
            e = [ 1, 2.5, true, "x", # c
              [3] ]
            f = { g = 'h', i = { j = false } }
            ml = """
            line1
            line2 \
               joined"""
            when = 2026-10-27T10:00:00Z
            [[items]]
            n = 1
            [[items]]
            n = 2
            [t.u]
            v = 'its'
            """");
        Assert.Equal(1L, t["a"]);
        Assert.Equal("dé\n", ((Dictionary<string, object>)t["b"])["c"]);
        var e = (List<object>)t["e"];
        Assert.Equal([1L, 2.5, true, "x"], e.Take(4));
        Assert.Equal("h", ((Dictionary<string, object>)t["f"])["g"]);
        Assert.Equal("line1\nline2 joined", t["ml"]);
        Assert.Equal(2, ((List<object>)t["items"]).Count);
        Assert.Equal("its", ((Dictionary<string, object>)((Dictionary<string, object>)t["t"])["u"])["v"]);
        Assert.Throws<FormatException>(() => MiniToml.Parse("a = 1\na = 2"));
        Assert.Throws<FormatException>(() => MiniToml.Parse("a = \"open"));
    }

    // ---- starting ----

    private static string DotNet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    private static McpServerEntry Stdio(string command, string[] args, Dictionary<string, string>? env = null, string? cwd = null) =>
        new("t", McpAgent.ClaudeCode, McpTransport.Stdio, command, args, env ?? [], null, new Dictionary<string, string>(), cwd);

    [Fact]
    public void A_launch_has_the_program_as_a_full_path_expanded_variables_and_the_secrets_to_hide()
    {
        var vars = new Dictionary<string, string> { ["HOME_X"] = "C:\\h", ["TOKEN"] = "tok-12345" };
        var entry = Stdio(DotNet, ["--a=${HOME_X}", "--b=${MISSING:-fallback}", "$5"], new() { ["T"] = "${TOKEN}", ["PLAIN"] = "p" }, Path.GetTempPath());
        var launch = McpConfig.BuildLaunch(entry, vars.GetValueOrDefault, _root);
        Assert.True(Path.IsPathRooted(launch.Executable) && File.Exists(launch.Executable));
        Assert.Equal(["--a=C:\\h", "--b=fallback", "$5"], launch.Args);
        Assert.Equal("tok-12345", launch.Env["T"]);
        Assert.Contains("tok-12345", launch.SecretValues);
        Assert.DoesNotContain("tok-12345", launch.CommandLine + string.Join(' ', launch.MaskedEnvironment));
        Assert.Equal(Path.GetFullPath(Path.GetTempPath()), launch.WorkingDirectory);
    }

    [Fact]
    public void A_launch_that_cannot_work_is_refused_with_a_reason_and_starts_nothing()
    {
        var remote = Scan().Servers.Single(s => s.Name == "remote");
        Assert.Contains("address", Assert.Throws<McpClientException>(() => McpConfig.BuildLaunch(remote, _ => null, _root)).Message);
        Assert.Contains("MISSING_VAR", Assert.Throws<McpClientException>(() => McpConfig.BuildLaunch(Stdio(DotNet, ["${MISSING_VAR}"]), _ => null, _root)).Message);
        Assert.Contains("not found", Assert.Throws<McpClientException>(() => McpConfig.BuildLaunch(Stdio("no-such-program-xyz", []), _ => null, _root)).Message);
        if (OperatingSystem.IsWindows())
        {
            var batch = Path.Combine(_root, "tool.cmd");
            File.WriteAllText(batch, "@echo off");
            Assert.Contains("batch file", Assert.Throws<McpClientException>(() => McpConfig.BuildLaunch(Stdio(batch, []), _ => null, _root)).Message);
        }
    }

    [Fact]
    public void The_npx_shim_of_node_is_replaced_by_node_and_its_script_when_node_is_installed()
    {
        var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(d => Path.Combine(d, "npx.cmd")).FirstOrDefault(File.Exists);
        if (!OperatingSystem.IsWindows() || node is null) return; // nothing to test without Node.js
        var launch = McpConfig.BuildLaunch(Stdio("npx", ["-y", "pkg"]), _ => null, _root);
        Assert.EndsWith("node.exe", launch.Executable);
        Assert.EndsWith("npx-cli.js", launch.Args[0]);
        Assert.Equal(["-y", "pkg"], launch.Args.Skip(1));
    }
}

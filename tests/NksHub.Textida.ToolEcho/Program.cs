// Stand-in for an external tool in tests: prints the arguments it received, one per line in brackets.
// --format FILE: stand-in for a formatter, rewrites the file in upper case. --format-fail FILE: leaves the file alone, writes "syntax error" to standard error and exits with 2.
// A file named like the program plus ".sample" next to it (a renamed copy standing in for a linter) is printed as it is, whatever the arguments; a first line "#exit N" sets the exit code, "#sleep" waits a minute instead,
// "#format" rewrites the last argument that is a file in upper case (a renamed copy standing in for a formatter), "#delay N" waits N milliseconds and then prints the rest as usual (it may come before "#exit"). Such a copy writes the arguments it got to ".args" next to it, one per line, and CARGO_NET_OFFLINE and GOPROXY as it saw them to ".env"; every start adds a line to ".runs". The one argument --version prints the file ".version" (nothing when there is none) and records nothing.
// --sleep: prints "started" and then waits a minute. --both: writes one line to each output stream. A trailing "--exit N" sets the exit code.
// --mcp modern|legacy|old: a small MCP server on standard input and output. modern answers server/discover, legacy only initialize (2025-11-25), old answers initialize with an
// older version. Tools: echo, slow (never answers; a cancel is reported on stderr), crash (exits with 7), env (value of a variable), secret (writes the variable named TEST_SECRET to stderr), page2 (second page of the list),
// big (not listed, but callable: a prefix plus a unit repeated length times). Resources: test://notes (text), test://blob (binary), test://big (5000 characters).
using System.Text;
using System.Text.Json;

Console.OutputEncoding = new UTF8Encoding(false);
if (Environment.ProcessPath is { } self && File.Exists(self + ".sample"))
{
    if (args is ["--version"]) // a version request of a linter: answers with the file ".version" when there is one, else stays silent; ".args" and ".runs" keep the record of the real runs
    {
        if (File.Exists(self + ".version")) Console.Write(File.ReadAllText(self + ".version"));
        return 0;
    }
    File.WriteAllLines(self + ".args", args);
    File.AppendAllText(self + ".runs", "run\n");
    File.WriteAllLines(self + ".env", new[] { "CARGO_NET_OFFLINE", "GOPROXY" }.Select(n => n + "=" + Environment.GetEnvironmentVariable(n)));
    var sample = File.ReadAllLines(self + ".sample");
    if (sample.Length > 0 && sample[0] == "#sleep")
    {
        Thread.Sleep(TimeSpan.FromMinutes(1));
        return 0;
    }
    if (sample.Length > 0 && sample[0] == "#format" && args.LastOrDefault(File.Exists) is { } target)
    {
        File.WriteAllText(target, File.ReadAllText(target).ToUpperInvariant(), new UTF8Encoding(false));
        return 0;
    }
    if (sample.Length > 0 && sample[0].StartsWith("#delay ", StringComparison.Ordinal) && int.TryParse(sample[0][7..], out int delay))
    {
        Thread.Sleep(delay);
        sample = sample[1..];
    }
    int sampleCode = 0;
    if (sample.Length > 0 && sample[0].StartsWith("#exit ", StringComparison.Ordinal)) int.TryParse(sample[0][6..], out sampleCode);
    foreach (var line in sample.Where(l => !l.StartsWith('#'))) Console.WriteLine(line);
    return sampleCode;
}
if (args is ["--mcp", var mode]) return RunMcp(mode);
if (args is ["--format", var formatted])
{
    File.WriteAllText(formatted, File.ReadAllText(formatted).ToUpperInvariant(), new UTF8Encoding(false));
    return 0;
}
if (args is ["--format-fail", _])
{
    Console.Error.WriteLine("syntax error");
    return 2;
}
if (args is ["--sleep", ..])
{
    Console.WriteLine("started");
    Thread.Sleep(TimeSpan.FromMinutes(1));
    return 0;
}
if (args is ["--both"])
{
    Console.WriteLine("to-out");
    Console.Error.WriteLine("to-err");
    return 0;
}
int code = 0;
if (args is [.., "--exit", var number] && int.TryParse(number, out code)) args = args[..^2];
foreach (var arg in args) Console.WriteLine("[" + arg + "]");
return code;

static int RunMcp(string mode)
{
    if (Environment.GetEnvironmentVariable("TEST_MARKER") is { Length: > 0 } marker) File.WriteAllText(marker, "started");
    var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
    var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
    var gate = new object();
    void Send(string json) { lock (gate) stdout.WriteLine(json); }
    static string Quote(string s) => JsonSerializer.Serialize(s);
    static string Result(JsonElement id, string result) => "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"result\":" + result + "}";
    static string Error(JsonElement id, int code, string message) => "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"error\":{\"code\":" + code + ",\"message\":" + Quote(message) + "}}";
    static string Tools(bool second) => second
        ? "{\"tools\":[{\"name\":\"crash\",\"inputSchema\":{\"type\":\"object\"}},{\"name\":\"env\",\"inputSchema\":{\"type\":\"object\"}},{\"name\":\"secret\",\"inputSchema\":{\"type\":\"object\"}}]}"
        : "{\"tools\":[{\"name\":\"echo\",\"title\":\"Echo\",\"description\":\"Returns text\",\"inputSchema\":{\"type\":\"object\"},\"annotations\":{\"readOnlyHint\":true}},{\"name\":\"slow\",\"inputSchema\":{\"type\":\"object\"}}],\"nextCursor\":\"page2\"}";
    static string Text(string text) => "{\"content\":[{\"type\":\"text\",\"text\":" + Quote(text) + "}],\"isError\":false}";

    while (stdin.ReadLine() is { } line)
    {
        if (line.Length == 0) continue;
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        var method = root.GetProperty("method").GetString()!;
        var p = root.TryGetProperty("params", out var pp) ? pp : default;
        if (!root.TryGetProperty("id", out var id))
        {
            if (method == "notifications/cancelled") Console.Error.WriteLine("cancelled:" + p.GetProperty("requestId").GetRawText());
            else Console.Error.WriteLine("notified:" + method);
            continue;
        }
        switch (method)
        {
            case "server/discover" when mode == "modern":
                Send(Result(id, "{\"resultType\":\"complete\",\"supportedVersions\":[\"2026-07-28\",\"2025-11-25\"],\"capabilities\":{\"tools\":{},\"resources\":{}},\"_meta\":{\"io.modelcontextprotocol/serverInfo\":{\"name\":\"fake\",\"version\":\"2\"}}}"));
                break;
            case "server/discover":
                Send(Error(id, -32601, "Method not found"));
                break;
            case "initialize":
                Send(Result(id, "{\"protocolVersion\":\"" + (mode == "old" ? "2024-11-05" : "2025-11-25") + "\",\"capabilities\":{\"tools\":{},\"resources\":{}},\"serverInfo\":{\"name\":\"fake\",\"version\":\"1\"},\"instructions\":\"fake instructions\"}"));
                break;
            case "tools/list":
                Console.Error.WriteLine("era:" + (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("_meta", out _) ? "modern" : "legacy"));
                Send(Result(id, Tools(p.ValueKind == JsonValueKind.Object && p.TryGetProperty("cursor", out _))));
                break;
            case "tools/call":
                var arguments = p.TryGetProperty("arguments", out var a) ? a : default;
                switch (p.GetProperty("name").GetString())
                {
                    case "echo": Send(Result(id, Text(arguments.GetProperty("text").GetString()!))); break;
                    case "slow": Console.Error.WriteLine("slow-started:" + id.GetRawText()); break;
                    case "crash": Console.Error.WriteLine("boom"); return 7;
                    case "env": Send(Result(id, Text(Environment.GetEnvironmentVariable(arguments.GetProperty("name").GetString()!) ?? "(unset)"))); break;
                    case "secret": Console.Error.WriteLine("token is " + Environment.GetEnvironmentVariable("TEST_SECRET")); Send(Result(id, Text("ok"))); break;
                    case "big":
                        var prefix = arguments.TryGetProperty("prefix", out var pf) ? pf.GetString()! : "";
                        Send(Result(id, Text(prefix + string.Concat(Enumerable.Repeat(arguments.TryGetProperty("unit", out var un) ? un.GetString()! : "x", arguments.GetProperty("length").GetInt32())))));
                        break;
                    default: Send(Error(id, -32602, "Unknown tool")); break;
                }
                break;
            case "resources/list":
                Send(Result(id, "{\"resources\":[{\"uri\":\"test://notes\",\"name\":\"notes\",\"title\":\"Notes\",\"mimeType\":\"text/plain\"},{\"uri\":\"test://blob\",\"name\":\"blob\",\"mimeType\":\"application/octet-stream\"},{\"uri\":\"test://big\",\"name\":\"big\"}]}"));
                break;
            case "resources/read":
                var uri = p.GetProperty("uri").GetString();
                Send(uri switch
                {
                    "test://notes" => Result(id, "{\"contents\":[{\"uri\":\"test://notes\",\"mimeType\":\"text/plain\",\"text\":\"first notes\"},{\"uri\":\"test://notes\",\"text\":\"second part\"}]}"),
                    "test://blob" => Result(id, "{\"contents\":[{\"uri\":\"test://blob\",\"mimeType\":\"application/octet-stream\",\"blob\":\"AAECAwQFBgcICQoL\"}]}"),
                    "test://big" => Result(id, "{\"contents\":[{\"uri\":\"test://big\",\"text\":" + Quote(new string('r', 5000)) + "}]}"),
                    _ => Error(id, -32002, "Resource not found"),
                });
                break;
            case "ping":
                Send(Result(id, "{}"));
                break;
            default:
                Send(Error(id, -32601, "Method not found"));
                break;
        }
    }
    return 0;
}

// Stand-in for a language server in tests: JSON-RPC over stdio with Content-Length framing. It keeps its own copy of every document it is told about
// and counts positions in UTF-16 units, as the protocol says, without sharing any code with the client it is tested against.
// Options: --full (ask for whole texts instead of ranges), --dump DIR (after every open and change write NAME.txt with its copy, NAME.version with the version
// and NAME.stats with "opens=N incremental=N full=N"; process-PID per started process holding "root=<rootUri>", "cwd=<working folder>", "configuration=yes|no" (whether the client announced workspace.configuration) and one "open <uri>" or "close <uri>" line per document event,
// and init-options.json with the initializationOptions it got), --external FILE (a file on disk that definitions may point into),
// --config SECTIONS (after "initialized" ask the client for workspace/configuration with these comma separated sections, "*" for an item without one, and write the answer to config-answer.json).
// Behaviour: a text containing CRASH ends the process with exit code 3, a text containing FALLONCE does so once (while the file "fell-once" is not in the dump folder); every ERROR is published as a diagnostic (with the version it was found in);
// a definition of a word is the first "def WORD" in the open documents, then in the external file; references are the whole-word occurrences in the document;
// completion of a prefix of "Console" offers it with an additional edit that inserts "using System;" at the top (a text containing SLOW answers a second late);
// rename of a word changes its whole-word occurrences in every open document (documentChanges with the version the server holds); with --fileops it also creates
// created.fake, deletes doomed.fake, renames moveme.fake to moved.fake (all next to the first open document) and edits the external file on disk.
// --load-ms N: like Roslyn, it knows the document of a request only until it is told to open a solution or projects (solution/open, project/open, noted in the process file); it says it loaded them with workspace/projectInitializationComplete N ms later,
// and renames in the document of the request only until then.
using System.Text;
using System.Text.Json;

bool full = args.Contains("--full");
bool fileOps = args.Contains("--fileops");
string? dump = args.SkipWhile(a => a != "--dump").Skip(1).FirstOrDefault();
string? external = args.SkipWhile(a => a != "--external").Skip(1).FirstOrDefault();
string? configSections = args.SkipWhile(a => a != "--config").Skip(1).FirstOrDefault();
string? processFile = null;
int loadMs = int.TryParse(args.SkipWhile(a => a != "--load-ms").Skip(1).FirstOrDefault(), out var loadValue) ? loadValue : -1;
var loading = new int[] { loadMs >= 0 ? 1 : 0 }; // until it is told to open a project, a server of this kind knows the document of the request only
if (dump is not null)
{
    Directory.CreateDirectory(dump);
    processFile = Path.Combine(dump, $"process-{Environment.ProcessId}");
    File.WriteAllText(processFile, "");
}

var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
var gate = new object();
var texts = new Dictionary<string, string>();
var stats = new Dictionary<string, (int Opens, int Incremental, int Full)>();
var versions = new Dictionary<string, long>();

void Send(string json)
{
    var body = Encoding.UTF8.GetBytes(json);
    lock (gate)
    {
        output.Write(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"));
        output.Write(body);
        output.Flush();
    }
}

string Quote(string s) => JsonSerializer.Serialize(s);

void Note(string line)
{
    if (processFile is not null) lock (gate) File.AppendAllText(processFile, line + "\n");
}

byte[]? ReadMessage()
{
    int length = -1;
    while (true)
    {
        var line = new StringBuilder();
        int b;
        while ((b = input.ReadByte()) >= 0 && b != '\n') if (b != '\r') line.Append((char)b);
        if (b < 0) return null;
        if (line.Length == 0) break;
        if (line.ToString().StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.ToString()[15..].Trim());
    }
    var body = new byte[length];
    for (int read = 0; read < length;)
    {
        int n = input.Read(body, read, length - read);
        if (n <= 0) return null;
        read += n;
    }
    return body;
}

// ---- positions: lines end with \n, \r\n or \r; a character is a UTF-16 unit ----

int Offset(string text, int line, int character)
{
    int at = 0;
    for (int l = 0; l < line && at < text.Length; l++)
    {
        while (at < text.Length && text[at] != '\n' && text[at] != '\r') at++;
        if (at < text.Length) at += text[at] == '\r' && at + 1 < text.Length && text[at + 1] == '\n' ? 2 : 1;
    }
    int end = at;
    while (end < text.Length && text[end] != '\n' && text[end] != '\r') end++;
    return Math.Min(at + character, end);
}

(int Line, int Character) Position(string text, int offset)
{
    int line = 0, start = 0;
    for (int i = 0; i < offset; i++)
    {
        if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
        else if (text[i] != '\r' && text[i] != '\n') continue;
        line++;
        start = i + 1;
    }
    return (line, offset - start);
}

string Range(string text, int start, int end)
{
    var (sl, sc) = Position(text, start);
    var (el, ec) = Position(text, end);
    return $"{{\"start\":{{\"line\":{sl},\"character\":{sc}}},\"end\":{{\"line\":{el},\"character\":{ec}}}}}";
}

static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

string WordAt(string text, int offset)
{
    int s = offset, e = offset;
    while (s > 0 && IsWord(text[s - 1])) s--;
    while (e < text.Length && IsWord(text[e])) e++;
    return text[s..e];
}

string Location(string uri, string text, int start, int end) => $"{{\"uri\":{Quote(uri)},\"range\":{Range(text, start, end)}}}";

// ---- documents ----

string DumpName(string uri) => string.Concat(Uri.UnescapeDataString(uri[(uri.LastIndexOf('/') + 1)..]).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

void Changed(string uri, long version, string text)
{
    texts[uri] = text;
    versions[uri] = version;
    if (dump is not null)
    {
        var name = DumpName(uri);
        File.WriteAllText(Path.Combine(dump, name + ".txt"), text, new UTF8Encoding(false));
        var (opens, incremental, fullCount) = stats.GetValueOrDefault(uri);
        File.WriteAllText(Path.Combine(dump, name + ".stats"), $"opens={opens} incremental={incremental} full={fullCount}");
        File.WriteAllText(Path.Combine(dump, name + ".version"), version.ToString());
    }
    if (text.Contains("CRASH")) Environment.Exit(3);
    if (text.Contains("FALLONCE") && dump is not null && !File.Exists(Path.Combine(dump, "fell-once")))
    {
        File.WriteAllText(Path.Combine(dump, "fell-once"), "");
        Environment.Exit(3);
    }
    var items = new List<string>();
    for (int at = text.IndexOf("ERROR", StringComparison.Ordinal); at >= 0; at = text.IndexOf("ERROR", at + 5, StringComparison.Ordinal))
        items.Add($"{{\"range\":{Range(text, at, at + 5)},\"severity\":1,\"code\":\"E1\",\"message\":\"found ERROR\"}}");
    Send($"{{\"jsonrpc\":\"2.0\",\"method\":\"textDocument/publishDiagnostics\",\"params\":{{\"uri\":{Quote(uri)},\"version\":{version},\"diagnostics\":[{string.Join(',', items)}]}}}}");
}

string? Definition(JsonElement p)
{
    var uri = p.GetProperty("textDocument").GetProperty("uri").GetString()!;
    var pos = p.GetProperty("position");
    var text = texts[uri];
    var word = WordAt(text, Offset(text, pos.GetProperty("line").GetInt32(), pos.GetProperty("character").GetInt32()));
    if (word.Length == 0) return null;
    var needle = "def " + word;
    foreach (var (otherUri, otherText) in texts)
        if (otherText.IndexOf(needle, StringComparison.Ordinal) is var at and >= 0)
            return Location(otherUri, otherText, at + 4, at + 4 + word.Length);
    if (external is not null && File.Exists(external))
    {
        var externalText = File.ReadAllText(external, new UTF8Encoding(false));
        if (externalText.IndexOf(needle, StringComparison.Ordinal) is var at and >= 0)
            return Location(new Uri(Path.GetFullPath(external)).AbsoluteUri, externalText, at + 4, at + 4 + word.Length);
    }
    return null;
}

string References(JsonElement p)
{
    var uri = p.GetProperty("textDocument").GetProperty("uri").GetString()!;
    var pos = p.GetProperty("position");
    var text = texts[uri];
    var word = WordAt(text, Offset(text, pos.GetProperty("line").GetInt32(), pos.GetProperty("character").GetInt32()));
    var found = new List<string>();
    if (word.Length > 0)
        for (int at = text.IndexOf(word, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.Ordinal))
            if ((at == 0 || !IsWord(text[at - 1])) && (at + word.Length == text.Length || !IsWord(text[at + word.Length])))
                found.Add(Location(uri, text, at, at + word.Length));
    return "[" + string.Join(',', found) + "]";
}

string Completion(JsonElement p)
{
    var uri = p.GetProperty("textDocument").GetProperty("uri").GetString()!;
    var pos = p.GetProperty("position");
    var text = texts[uri];
    int caret = Offset(text, pos.GetProperty("line").GetInt32(), pos.GetProperty("character").GetInt32());
    if (text.Contains("SLOW")) Thread.Sleep(1000);
    int start = caret;
    while (start > 0 && IsWord(text[start - 1])) start--;
    var prefix = text[start..caret];
    if (prefix.Length == 0 || !"Console".StartsWith(prefix, StringComparison.Ordinal)) return "{\"isIncomplete\":false,\"items\":[]}";
    return "{\"isIncomplete\":false,\"items\":[{\"label\":\"Console\",\"kind\":7,\"detail\":\"class System.Console\",\"textEdit\":{\"range\":" + Range(text, start, caret)
        + ",\"newText\":\"Console\"},\"additionalTextEdits\":[{\"range\":" + Range(text, 0, 0) + ",\"newText\":\"using System;\\n\"}]}]}";
}

string? Rename(JsonElement p)
{
    var uri = p.GetProperty("textDocument").GetProperty("uri").GetString()!;
    var pos = p.GetProperty("position");
    var newName = p.GetProperty("newName").GetString()!;
    var word = WordAt(texts[uri], Offset(texts[uri], pos.GetProperty("line").GetInt32(), pos.GetProperty("character").GetInt32()));
    if (word.Length == 0) return null;
    var changes = new List<string>();
    foreach (var (otherUri, text) in texts)
    {
        if (Volatile.Read(ref loading[0]) == 1 && otherUri != uri) continue;
        var edits = new List<string>();
        for (int at = text.IndexOf(word, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.Ordinal))
            if ((at == 0 || !IsWord(text[at - 1])) && (at + word.Length == text.Length || !IsWord(text[at + word.Length])))
                edits.Add($"{{\"range\":{Range(text, at, at + word.Length)},\"newText\":{Quote(newName)}}}");
        if (edits.Count > 0)
            changes.Add($"{{\"textDocument\":{{\"uri\":{Quote(otherUri)},\"version\":{versions[otherUri]}}},\"edits\":[{string.Join(',', edits)}]}}");
    }
    if (fileOps)
    {
        var folder = uri[..(uri.LastIndexOf('/') + 1)];
        changes.Add($"{{\"kind\":\"create\",\"uri\":{Quote(folder + "created.fake")}}}");
        changes.Add($"{{\"kind\":\"delete\",\"uri\":{Quote(folder + "doomed.fake")}}}");
        changes.Add($"{{\"kind\":\"rename\",\"oldUri\":{Quote(folder + "moveme.fake")},\"newUri\":{Quote(folder + "moved.fake")}}}");
        if (external is not null && File.Exists(external))
        {
            var disk = File.ReadAllText(external, new UTF8Encoding(false));
            var edits = new List<string>();
            for (int at = disk.IndexOf(word, StringComparison.Ordinal); at >= 0; at = disk.IndexOf(word, at + 1, StringComparison.Ordinal))
                edits.Add($"{{\"range\":{Range(disk, at, at + word.Length)},\"newText\":{Quote(newName)}}}");
            if (edits.Count > 0)
                changes.Add($"{{\"textDocument\":{{\"uri\":{Quote(new Uri(Path.GetFullPath(external)).AbsoluteUri)},\"version\":null}},\"edits\":[{string.Join(',', edits)}]}}");
        }
    }
    return $"{{\"documentChanges\":[{string.Join(',', changes)}]}}";
}

// ---- the loop ----

while (ReadMessage() is { } body)
{
    using var doc = JsonDocument.Parse(body);
    var root = doc.RootElement;
    if (!root.TryGetProperty("method", out var methodName))
    {
        if (dump is not null && root.TryGetProperty("id", out var answered) && answered.ValueKind == JsonValueKind.Number && answered.GetInt32() == 7001 && root.TryGetProperty("result", out var answer))
            File.WriteAllText(Path.Combine(dump, "config-answer.json"), answer.GetRawText());
        continue;
    }
    var method = methodName.GetString()!;
    var p = root.TryGetProperty("params", out var pp) ? pp : default;
    bool isRequest = root.TryGetProperty("id", out var id);
    string Reply(string result) => $"{{\"jsonrpc\":\"2.0\",\"id\":{id.GetRawText()},\"result\":{result}}}";
    switch (method)
    {
        case "initialize":
            if (dump is not null && p.TryGetProperty("initializationOptions", out var initOptions)) File.WriteAllText(Path.Combine(dump, "init-options.json"), initOptions.GetRawText());
            Note("root=" + (p.TryGetProperty("rootUri", out var rootUri) ? rootUri.GetString() : ""));
            Note("cwd=" + Environment.CurrentDirectory);
            Note("configuration=" + (p.TryGetProperty("capabilities", out var caps) && caps.TryGetProperty("workspace", out var ws) && ws.TryGetProperty("configuration", out var cfg) && cfg.ValueKind == JsonValueKind.True ? "yes" : "no"));
            Send(Reply($"{{\"capabilities\":{{\"textDocumentSync\":{{\"openClose\":true,\"change\":{(full ? 1 : 2)}}},\"definitionProvider\":true,\"referencesProvider\":true,\"completionProvider\":{{}},\"renameProvider\":true}},\"serverInfo\":{{\"name\":\"fake\"}}}}"));
            break;
        case "initialized":
            if (configSections is not null)
            {
                var items = string.Join(',', configSections.Split(',').Select(section => section == "*" ? "{}" : $"{{\"section\":{Quote(section)}}}"));
                Send($"{{\"jsonrpc\":\"2.0\",\"id\":7001,\"method\":\"workspace/configuration\",\"params\":{{\"items\":[{items}]}}}}");
            }
            break;
        case "solution/open":
        case "project/open":
            Note("workspace " + method + " " + p.GetRawText());
            if (loadMs >= 0)
            {
                Volatile.Write(ref loading[0], 1);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(loadMs);
                    Volatile.Write(ref loading[0], 0);
                    Send("{\"jsonrpc\":\"2.0\",\"method\":\"workspace/projectInitializationComplete\"}");
                });
            }
            break;
        case "shutdown":
            Send(Reply("null"));
            break;
        case "exit":
            return 0;
        case "textDocument/didOpen":
        {
            var item = p.GetProperty("textDocument");
            var uri = item.GetProperty("uri").GetString()!;
            Note("open " + uri);
            var (opens, incremental, fullCount) = stats.GetValueOrDefault(uri);
            stats[uri] = (opens + 1, incremental, fullCount);
            Changed(uri, item.GetProperty("version").GetInt64(), item.GetProperty("text").GetString()!);
            break;
        }
        case "textDocument/didChange":
        {
            var uri = p.GetProperty("textDocument").GetProperty("uri").GetString()!;
            var text = texts[uri];
            var (opens, incremental, fullCount) = stats.GetValueOrDefault(uri);
            foreach (var change in p.GetProperty("contentChanges").EnumerateArray())
            {
                if (change.TryGetProperty("range", out var range))
                {
                    int start = Offset(text, range.GetProperty("start").GetProperty("line").GetInt32(), range.GetProperty("start").GetProperty("character").GetInt32());
                    int end = Offset(text, range.GetProperty("end").GetProperty("line").GetInt32(), range.GetProperty("end").GetProperty("character").GetInt32());
                    text = text[..start] + change.GetProperty("text").GetString() + text[end..];
                    incremental++;
                }
                else
                {
                    text = change.GetProperty("text").GetString()!;
                    fullCount++;
                }
            }
            stats[uri] = (opens, incremental, fullCount);
            Changed(uri, p.GetProperty("textDocument").GetProperty("version").GetInt64(), text);
            break;
        }
        case "textDocument/didClose":
            Note("close " + p.GetProperty("textDocument").GetProperty("uri").GetString()!);
            texts.Remove(p.GetProperty("textDocument").GetProperty("uri").GetString()!);
            break;
        case "textDocument/definition":
            Send(Reply(Definition(p) ?? "null"));
            break;
        case "textDocument/references":
            Send(Reply(References(p)));
            break;
        case "textDocument/completion":
            Send(Reply(Completion(p)));
            break;
        case "textDocument/rename":
            Send(Reply(Rename(p) ?? "null"));
            break;
        default:
            if (isRequest) Send($"{{\"jsonrpc\":\"2.0\",\"id\":{id.GetRawText()},\"error\":{{\"code\":-32601,\"message\":\"unknown\"}}}}");
            break;
    }
}
return 0;

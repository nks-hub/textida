// Stand-in for an ACP agent in tests: JSON-RPC over stdio, one message per line, version 1, written from the protocol pages (agentclientprotocol.com/protocol)
// without sharing code with the client it is tested against. What a prompt does depends on its first word:
//   hello            streams "Hel" "lo " "wor" "ld", a tool call that completes, then ends the turn (end_turn)
//   read PATH        asks the client for fs/read_text_file and streams "READ:" + the content (or "READERR:code:message")
//   write PATH\nTEXT asks the client for fs/write_text_file and streams "WRITE-OK" (or "WRITE-ERR:code:message")
//   perm             asks session/request_permission (allow-once / reject-once) and streams "PERM:" + the outcome
//   permwait         the same, but the turn waits for the answer and ends with cancelled when the outcome is cancelled
//   permexec         the same for a tool of kind execute, with an extra "allow always" option
//   permdiff PATH\nTEXT, permedit PATH\nOLD\nNEW, permdiffslow PATH\nTEXT
//                    ask for permission with a diff (the new text, or a piece and its replacement), write the file itself after "allow once" and stream "PERMDIFF:written" or "PERMDIFF:not-written"
//   seen             streams "SEEN:" and the methods the client has sent so far, in order, separated by commas
//   caps             streams "CAPS:" and the clientCapabilities of the client's initialize request
//   outside          reports four tool calls that complete: two of kind execute, one delete, one read
//   slow             streams "tick" every 50 ms until session/cancel arrives, then ends the turn with cancelled
//   crash            writes a line to stderr and exits with code 3 in the middle of the turn
//   silent           never answers the prompt (and ignores session/cancel)
//   anything else    streams "echo:" + the text
// Options: --protocol N (answer initialize with version N), --hang-init (never answer initialize), --auth (session/new fails with "authentication required").
// Version probe (the program is started like a real agent program asked for its version): --version prints a line with 0.1.0 and leaves, --version-silent prints nothing,
// --version-fail writes to stderr and exits with code 4, --version-hang never answers.
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Contains("--version-hang")) await Task.Delay(Timeout.Infinite);
if (args.Contains("--version-fail"))
{
    Console.Error.WriteLine("boom");
    Environment.Exit(4);
}
if (args.Contains("--version-silent")) return;
if (args.Contains("--version"))
{
    Console.WriteLine("fake-acp 0.1.0 (build 2026.10)");
    return;
}

int protocol = int.Parse(args.SkipWhile(a => a != "--protocol").Skip(1).FirstOrDefault() ?? "1");
bool hangInit = args.Contains("--hang-init");
bool auth = args.Contains("--auth");

var output = Console.OpenStandardOutput();
var gate = new object();
var waiting = new Dictionary<long, TaskCompletionSource<JsonNode>>();
long nextId = 1000;
var cancelled = new TaskCompletionSource();
var seen = new List<string>();
var caps = "";

void Send(JsonObject message)
{
    message["jsonrpc"] = "2.0";
    var bytes = System.Text.Encoding.UTF8.GetBytes(message.ToJsonString() + "\n");
    lock (gate)
    {
        output.Write(bytes);
        output.Flush();
    }
}

void Update(JsonObject update) => Send(new JsonObject { ["method"] = "session/update", ["params"] = new JsonObject { ["sessionId"] = "sess_fake", ["update"] = update } });

void Chunk(string text) => Update(new JsonObject { ["sessionUpdate"] = "agent_message_chunk", ["content"] = new JsonObject { ["type"] = "text", ["text"] = text } });

Task<JsonNode> Ask(string method, JsonObject parameters)
{
    long id = Interlocked.Increment(ref nextId);
    var tcs = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
    lock (waiting) waiting[id] = tcs;
    Send(new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters });
    return tcs.Task;
}

string Failure(JsonNode answer) => $"{answer["error"]!["code"]}:{answer["error"]!["message"]}";

async Task RunPrompt(JsonNode id, string text)
{
    var word = text.Split([' ', '\n'], 2)[0];
    var rest = text.Length > word.Length ? text[(word.Length + 1)..] : "";
    var stop = "end_turn";
    switch (word)
    {
        case "hello":
            foreach (var part in new[] { "Hel", "lo ", "wor", "ld" })
            {
                Chunk(part);
                await Task.Delay(20);
            }
            Update(new JsonObject { ["sessionUpdate"] = "tool_call", ["toolCallId"] = "call_1", ["title"] = "Looking around", ["kind"] = "search", ["status"] = "pending" });
            Update(new JsonObject { ["sessionUpdate"] = "tool_call_update", ["toolCallId"] = "call_1", ["status"] = "completed" });
            break;
        case "read":
        {
            var answer = await Ask("fs/read_text_file", new JsonObject { ["sessionId"] = "sess_fake", ["path"] = rest });
            Chunk(answer["error"] is null ? "READ:" + answer["result"]!["content"] : "READERR:" + Failure(answer));
            break;
        }
        case "write":
        {
            int newline = rest.IndexOf('\n');
            var answer = await Ask("fs/write_text_file", new JsonObject { ["sessionId"] = "sess_fake", ["path"] = rest[..newline], ["content"] = rest[(newline + 1)..] });
            Chunk(answer["error"] is null ? "WRITE-OK" : "WRITE-ERR:" + Failure(answer));
            break;
        }
        case "perm" or "permwait" or "permexec":
        {
            bool exec = word == "permexec";
            var toolCall = new JsonObject { ["toolCallId"] = "call_2", ["title"] = exec ? "Run rm" : "Run the tests" };
            var options = new JsonArray
            {
                new JsonObject { ["optionId"] = "allow-once", ["name"] = "Allow once", ["kind"] = "allow_once" },
                new JsonObject { ["optionId"] = "reject-once", ["name"] = "Reject", ["kind"] = "reject_once" },
            };
            if (exec)
            {
                toolCall["kind"] = "execute";
                options.Add(new JsonObject { ["optionId"] = "allow-always", ["name"] = "Always allow", ["kind"] = "allow_always" });
            }
            var answer = await Ask("session/request_permission", new JsonObject { ["sessionId"] = "sess_fake", ["toolCall"] = toolCall, ["options"] = options });
            var outcome = answer["result"]!["outcome"]!;
            Chunk("PERM:" + outcome.ToJsonString());
            if (word == "permwait" && (string?)outcome["outcome"] == "cancelled") stop = "cancelled";
            break;
        }
        case "permdiff" or "permdiffslow" or "permedit":
        {
            // like the real adapters: the change comes with the question as a diff, and after an "allow" the agent writes the file itself and never calls fs/write_text_file
            //   permdiff PATH\nTEXT       the whole new text, no old text (the Write tool)
            //   permedit PATH\nOLD\nNEW   a piece of the file and its replacement (the Edit tool)
            //   permdiffslow              like permdiff, but the tool call completes half a second after the write
            var lines = rest.Split('\n');
            string path = lines[0];
            string current = File.Exists(path) ? File.ReadAllText(path) : "";
            string? sentOld = word == "permedit" ? lines[1] : null;
            string newText = word == "permedit" ? lines[2] : string.Join('\n', lines.Skip(1));
            string result = word == "permedit" ? current.Replace(lines[1], lines[2]) : newText;
            Update(new JsonObject { ["sessionUpdate"] = "tool_call", ["toolCallId"] = "call_3", ["title"] = "Edit the file", ["kind"] = "edit", ["status"] = "pending" });
            var answer = await Ask("session/request_permission", new JsonObject
            {
                ["sessionId"] = "sess_fake",
                ["toolCall"] = new JsonObject
                {
                    ["toolCallId"] = "call_3",
                    ["title"] = "Edit the file",
                    ["kind"] = "edit",
                    ["content"] = new JsonArray { new JsonObject { ["type"] = "diff", ["path"] = path, ["oldText"] = sentOld, ["newText"] = newText } },
                    ["locations"] = new JsonArray { new JsonObject { ["path"] = path } },
                },
                ["options"] = new JsonArray
                {
                    new JsonObject { ["optionId"] = "allow-once", ["name"] = "Allow once", ["kind"] = "allow_once" },
                    new JsonObject { ["optionId"] = "reject-once", ["name"] = "Reject", ["kind"] = "reject_once" },
                },
            });
            var outcome = answer["result"]!["outcome"]!;
            bool allowed = (string?)outcome["outcome"] == "selected" && (string?)outcome["optionId"] == "allow-once";
            if (allowed) File.WriteAllText(path, result);
            if (word == "permdiffslow") await Task.Delay(500);
            Update(new JsonObject { ["sessionUpdate"] = "tool_call_update", ["toolCallId"] = "call_3", ["status"] = allowed ? "completed" : "failed" });
            Chunk("PERMDIFF:" + (allowed ? "written" : "not-written"));
            break;
        }
        case "seen":
            lock (seen) Chunk("SEEN:" + string.Join(',', seen));
            break;
        case "caps":
            Chunk("CAPS:" + caps);
            break;
        case "outside":
            foreach (var (callId, title, kind) in new[] { ("call_s1", "Run the tests", "execute"), ("call_s2", "Run the build", "execute"), ("call_s3", "Delete the cache", "delete"), ("call_s4", "Look at a file", "read") })
            {
                Update(new JsonObject { ["sessionUpdate"] = "tool_call", ["toolCallId"] = callId, ["title"] = title, ["kind"] = kind, ["status"] = "pending" });
                Update(new JsonObject { ["sessionUpdate"] = "tool_call_update", ["toolCallId"] = callId, ["status"] = "completed" });
            }
            Chunk("OUTSIDE-DONE");
            break;
        case "slow":
            for (int i = 0; i < 400 && !cancelled.Task.IsCompleted; i++)
            {
                Chunk("tick");
                await Task.WhenAny(Task.Delay(50), cancelled.Task);
            }
            stop = "cancelled";
            break;
        case "crash":
            Chunk("about to crash");
            Console.Error.WriteLine("agent crashed on purpose");
            Environment.Exit(3);
            break;
        case "silent":
            await Task.Delay(Timeout.Infinite);
            break;
        default:
            Chunk("echo:" + text);
            break;
    }
    Send(new JsonObject { ["id"] = id.DeepClone(), ["result"] = new JsonObject { ["stopReason"] = stop } });
}

using var input = new StreamReader(Console.OpenStandardInput(), new System.Text.UTF8Encoding(false));
while (await input.ReadLineAsync() is { } line)
{
    var message = JsonNode.Parse(line)!;
    var method = (string?)message["method"];
    var id = message["id"];
    if (method is not null) lock (seen) seen.Add(method);
    if (method is null)
    {
        TaskCompletionSource<JsonNode>? tcs;
        lock (waiting) waiting.Remove((long)id!, out tcs);
        tcs?.TrySetResult(message);
        continue;
    }
    switch (method)
    {
        case "initialize":
            caps = message["params"]?["clientCapabilities"]?.ToJsonString() ?? "";
            if (hangInit) break;
            Send(new JsonObject
            {
                ["id"] = id!.DeepClone(),
                ["result"] = new JsonObject
                {
                    ["protocolVersion"] = protocol,
                    ["agentCapabilities"] = new JsonObject { ["loadSession"] = false },
                    ["agentInfo"] = new JsonObject { ["name"] = "fake-acp", ["title"] = "Fake ACP agent", ["version"] = "0.1" },
                    ["authMethods"] = new JsonArray(),
                },
            });
            break;
        case "session/new":
            Send(auth
                ? new JsonObject { ["id"] = id!.DeepClone(), ["error"] = new JsonObject { ["code"] = -32000, ["message"] = "Authentication required" } }
                : new JsonObject { ["id"] = id!.DeepClone(), ["result"] = new JsonObject { ["sessionId"] = "sess_fake" } });
            break;
        case "session/prompt":
        {
            var text = (string)message["params"]!["prompt"]![0]!["text"]!;
            _ = Task.Run(() => RunPrompt(id!, text));
            break;
        }
        case "session/cancel":
            if (!(message["params"]!["sessionId"]!.ToString() == "sess_fake")) break;
            cancelled.TrySetResult();
            break;
        default:
            if (id is not null) Send(new JsonObject { ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not found" } });
            break;
    }
}

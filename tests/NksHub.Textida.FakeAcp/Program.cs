// Stand-in for an ACP agent in tests: JSON-RPC over stdio, one message per line, version 1, written from the protocol pages (agentclientprotocol.com/protocol)
// without sharing code with the client it is tested against. What a prompt does depends on its first word:
//   hello            streams "Hel" "lo " "wor" "ld", a tool call that completes, then ends the turn (end_turn)
//   read PATH        asks the client for fs/read_text_file and streams "READ:" + the content (or "READERR:code:message")
//   write PATH\nTEXT asks the client for fs/write_text_file and streams "WRITE-OK" (or "WRITE-ERR:code:message")
//   perm             asks session/request_permission (allow-once / reject-once) and streams "PERM:" + the outcome
//   permwait         the same, but the turn waits for the answer and ends with cancelled when the outcome is cancelled
//   slow             streams "tick" every 50 ms until session/cancel arrives, then ends the turn with cancelled
//   crash            writes a line to stderr and exits with code 3 in the middle of the turn
//   silent           never answers the prompt (and ignores session/cancel)
//   anything else    streams "echo:" + the text
// Options: --protocol N (answer initialize with version N), --hang-init (never answer initialize), --auth (session/new fails with "authentication required").
using System.Text.Json;
using System.Text.Json.Nodes;

int protocol = int.Parse(args.SkipWhile(a => a != "--protocol").Skip(1).FirstOrDefault() ?? "1");
bool hangInit = args.Contains("--hang-init");
bool auth = args.Contains("--auth");

var output = Console.OpenStandardOutput();
var gate = new object();
var waiting = new Dictionary<long, TaskCompletionSource<JsonNode>>();
long nextId = 1000;
var cancelled = new TaskCompletionSource();

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
        case "perm" or "permwait":
        {
            var answer = await Ask("session/request_permission", new JsonObject
            {
                ["sessionId"] = "sess_fake",
                ["toolCall"] = new JsonObject { ["toolCallId"] = "call_2", ["title"] = "Run the tests" },
                ["options"] = new JsonArray
                {
                    new JsonObject { ["optionId"] = "allow-once", ["name"] = "Allow once", ["kind"] = "allow_once" },
                    new JsonObject { ["optionId"] = "reject-once", ["name"] = "Reject", ["kind"] = "reject_once" },
                },
            });
            var outcome = answer["result"]!["outcome"]!;
            Chunk("PERM:" + outcome.ToJsonString());
            if (word == "permwait" && (string?)outcome["outcome"] == "cancelled") stop = "cancelled";
            break;
        }
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

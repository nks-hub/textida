using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Agents.Transcripts;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P8b.3.4 and P8b.3.5 for the second format: the session files of Codex as turns, rows, files touched and hits of a search, and the index of both formats against a plain walk over the file. The lines are
/// handmade and follow the open-source protocol definitions of Codex (see <see cref="CodexTranscript"/>); the key in them is made up.
/// </summary>
[Collection("SecretPatterns")]
public class CodexTranscriptTests
{
    private const string Key = "hunter2hunter2";

    private static readonly TranscriptLimits Limits = new(
        WholeLineBytes: 1 << 16, PeekBytes: 512, IndexMaxBytes: 1 << 20, MaxLinesPerTurn: 200, MaxRowChars: 4000, MaxRawBytes: 1 << 16, MaxSearchHits: 100, PreviewChars: 60, MaxDepth: 64,
        MinSecretLength: 6, MaxPendingTools: 100);

    private static readonly string[] Session =
    [
        /* 0 */ """{"timestamp":"2026-01-01T10:00:00Z","type":"session_meta","payload":{"id":"s1","cwd":"/work/proj","originator":"test","cli_version":"0.0.0"}}""",
        /* 1 */ """{"timestamp":"2026-01-01T10:00:01Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"Please read the env file"}]}}""",
        /* 2 */ """{"timestamp":"2026-01-01T10:00:01Z","type":"event_msg","payload":{"type":"user_message","message":"Please read the env file"}}""",
        /* 3 */ """{"timestamp":"2026-01-01T10:00:02Z","type":"response_item","payload":{"type":"reasoning","summary":[{"type":"summary_text","text":"Plan to read it."}],"encrypted_content":null}}""",
        /* 4 */ """{"timestamp":"2026-01-01T10:00:03Z","type":"response_item","payload":{"type":"function_call","name":"shell","arguments":"{\"command\":[\"bash\",\"-lc\",\"cat .env\"],\"workdir\":\"/work/proj\"}","call_id":"c1"}}""",
        /* 5 */ """{"timestamp":"2026-01-01T10:00:04Z","type":"response_item","payload":{"type":"function_call_output","call_id":"c1","output":"API_KEY=hunter2hunter2\nMODE=dev"}}""",
        /* 6 */ """{"timestamp":"2026-01-01T10:00:05Z","type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"The key is hunter2hunter2 and the mode is dev."}]}}""",
        /* 7 */ """{"timestamp":"2026-01-01T10:01:00Z","type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"Now change the code"}]}}""",
        /* 8 */ """{"timestamp":"2026-01-01T10:01:01Z","type":"response_item","payload":{"type":"custom_tool_call","name":"apply_patch","call_id":"c2","input":"*** Begin Patch\n*** Update File: src/app.cs\n@@\n-int b = 2;\n+int b = 3;\n*** End Patch"}}""",
        /* 9 */ """{"timestamp":"2026-01-01T10:01:02Z","type":"response_item","payload":{"type":"custom_tool_call_output","call_id":"c2","output":"Done!"}}""",
        /* 10 */ """{"timestamp":"2026-01-01T10:01:03Z","type":"response_item","payload":{"type":"local_shell_call","call_id":"c3","status":"completed","action":{"type":"exec","command":["dotnet","test"]}}}""",
        /* 11 */ """{"timestamp":"2026-01-01T10:01:04Z","type":"response_item","payload":{"type":"function_call_output","call_id":"c3","output":[{"type":"input_text","text":"Passed!"}]}}""",
        /* 12 */ """{"timestamp":"2026-01-01T10:01:05Z","type":"response_item","payload":{"type":"web_search_call","status":"completed","action":{"type":"search","query":"q"}}}""",
        /* 13 */ """{"timestamp":"2026-01-01T10:01:06Z","type":"turn_context","payload":{"cwd":"/work/proj"}}""",
    ];

    private static TempFile Write(IEnumerable<string> lines, string eol = "\n") => new(Encoding.UTF8.GetBytes(string.Join(eol, lines) + eol));

    private static TranscriptIndex Open(TempFile file, TranscriptMasker? masker = null, TranscriptLimits? limits = null)
    {
        var index = TranscriptIndex.Open(file.Path, limits ?? Limits, masker ?? new TranscriptMasker((limits ?? Limits).MinSecretLength));
        index.Build(CancellationToken.None);
        return index;
    }

    private static string AllText(TurnContent content) => string.Join("\n", content.Rows.Select(r => r.Text));

    // ---- the index ----

    [Fact]
    public void The_index_finds_the_kind_of_every_line_and_a_turn_starts_at_a_user_message()
    {
        using var file = Write(Session);
        using var index = Open(file);
        Assert.Equal(Session.Length, index.LineCount);
        Assert.Equal(
            [TranscriptKind.Other, TranscriptKind.UserPrompt, TranscriptKind.Other, TranscriptKind.Assistant, TranscriptKind.Assistant, TranscriptKind.ToolResult, TranscriptKind.Assistant,
             TranscriptKind.UserPrompt, TranscriptKind.Assistant, TranscriptKind.ToolResult, TranscriptKind.Assistant, TranscriptKind.ToolResult, TranscriptKind.Other, TranscriptKind.Other],
            Enumerable.Range(0, Session.Length).Select(i => index.KindOf(i)));
        Assert.Equal(3, index.TurnCount);
        Assert.Equal((0, 0L, 1L), (index.Turn(0).Number, index.Turn(0).FirstLine, index.Turn(0).LineCount));
        Assert.Equal((1, 1L, 6L, "Please read the env file"), (index.Turn(1).Number, index.Turn(1).FirstLine, index.Turn(1).LineCount, index.Turn(1).Preview));
        Assert.Equal((2, 7L, 7L, "Now change the code"), (index.Turn(2).Number, index.Turn(2).FirstLine, index.Turn(2).LineCount, index.Turn(2).Preview));
    }

    [Fact]
    public void A_turn_shows_messages_reasoning_calls_and_outputs_with_the_files_a_patch_names()
    {
        using var file = Write(Session);
        using var index = Open(file);
        var second = index.ReadTurn(2);
        Assert.Equal(
            [TranscriptRowKind.User, TranscriptRowKind.ToolCall, TranscriptRowKind.ToolResult, TranscriptRowKind.ToolCall, TranscriptRowKind.ToolResult, TranscriptRowKind.Raw, TranscriptRowKind.Raw],
            second.Rows.Select(r => r.Kind));
        Assert.Equal("Now change the code", second.Rows[0].Text);
        Assert.Equal("apply_patch", second.Rows[1].Tool);
        Assert.Contains("-int b = 2;", second.Rows[1].Text);
        Assert.Equal("Done!", second.Rows[2].Text);
        Assert.Equal(("local_shell", "dotnet test"), (second.Rows[3].Tool, second.Rows[3].Text));
        Assert.Equal("Passed!", second.Rows[4].Text);
        Assert.Contains("web_search_call", second.Rows[5].Text);
        Assert.Contains("turn_context", second.Rows[6].Text);
        Assert.Equal([7L, 8L, 9L, 10L, 11L, 12L, 13L], second.Rows.Select(r => r.Line));
        Assert.Equal([("src/app.cs", "apply_patch", 8L)], second.Files.Select(f => (f.Path, f.Tool, f.Line)));

        var first = index.ReadTurn(1);
        Assert.Equal([TranscriptRowKind.User, TranscriptRowKind.Raw, TranscriptRowKind.Thinking, TranscriptRowKind.ToolCall, TranscriptRowKind.ToolResult, TranscriptRowKind.Assistant], first.Rows.Select(r => r.Kind));
        Assert.Contains("event_msg", first.Rows[1].Text); // an event is not interpreted: it is shown as the JSON it is
        Assert.Equal("Plan to read it.", first.Rows[2].Text);
        Assert.Equal("shell", first.Rows[3].Tool);
    }

    [Fact]
    public void An_unknown_item_and_an_unknown_block_are_shown_as_raw_JSON()
    {
        string odd = """{"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"refusal","refusal":"no"},{"type":"output_text","text":"After."}]}}""";
        string future = """{"type":"response_item","payload":{"type":"brand_new_item","x":1}}""";
        string developer = """{"type":"response_item","payload":{"type":"message","role":"developer","content":[{"type":"input_text","text":"rules"}]}}""";
        string bare = """{"type":"response_item","payload":"not an object"}""";
        using var file = Write([Session[1], odd, future, developer, bare]);
        using var index = Open(file);
        var rows = index.ReadTurn(0).Rows;
        Assert.Equal([TranscriptRowKind.User, TranscriptRowKind.Raw, TranscriptRowKind.Assistant, TranscriptRowKind.Raw, TranscriptRowKind.Raw, TranscriptRowKind.Raw], rows.Select(r => r.Kind));
        Assert.Equal("""{"type":"refusal","refusal":"no"}""", rows[1].Text);
        Assert.Equal(future, rows[3].Text);
        Assert.Equal(developer, rows[4].Text);
        Assert.Equal(bare, rows[5].Text);
    }

    [Fact]
    public void A_line_too_long_to_parse_is_classified_from_the_start_of_its_payload()
    {
        string Item(string payload) => $$"""{"timestamp":"2026-01-01T10:00:00Z","type":"response_item","payload":{{payload}}}""";
        string big = new('x', 3000);
        string user = Item($$"""{"type":"message","role":"user","content":[{"type":"input_text","text":"{{big}}"}]}""");
        string reply = Item($$"""{"type":"message","id":"m1","role":"assistant","content":[{"type":"output_text","text":"{{big}}"}]}""");
        string output = Item($$"""{"type":"function_call_output","call_id":"c","output":"{{big}}"}""");
        string call = Item($$"""{"type":"function_call","name":"shell","arguments":"{{big}}","call_id":"c"}""");
        string developer = Item($$"""{"type":"message","role":"developer","content":[{"type":"input_text","text":"{{big}}"}]}""");
        string unknown = Item($$"""{"type":"brand_new_item","text":"{{big}}"}""");
        string event_ = "{\"timestamp\":\"t\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"text\":\"" + big + "\"}}";
        using var file = Write([user, reply, output, call, developer, unknown, event_]);
        using var index = Open(file, limits: Limits with { WholeLineBytes = 1000, PeekBytes = 200 });
        Assert.Equal(
            [TranscriptKind.UserPrompt, TranscriptKind.Assistant, TranscriptKind.ToolResult, TranscriptKind.Assistant, TranscriptKind.Other, TranscriptKind.Other, TranscriptKind.Other],
            Enumerable.Range(0, 7).Select(i => index.KindOf(i)));
        Assert.Equal(1, index.TurnCount);
        var content = index.ReadTurn(0);
        Assert.Equal(7, content.OversizedLines);
        Assert.All(content.Rows, r => Assert.Equal(TranscriptRowKind.Raw, r.Kind));
    }

    [Fact]
    public void A_prefix_of_a_codex_line_that_is_cut_anywhere_does_not_throw()
    {
        string line = Session[4];
        for (int length = 0; length <= line.Length; length++)
            TranscriptParser.ClassifyPrefix(Encoding.UTF8.GetBytes(line[..length]), 60, out _);
        Assert.Equal(TranscriptKind.Assistant, TranscriptParser.ClassifyPrefix(Encoding.UTF8.GetBytes(line[..130]), 60, out _));
        Assert.Equal(TranscriptKind.Other, TranscriptParser.ClassifyPrefix("""{"timestamp":"t","type":"response_item","payload":{"type":"mess"""u8, 60, out _));
    }

    // ---- the index against a plain walk over the file ----

    private static readonly string[] ClaudeSession =
    [
        """{"type":"summary","summary":"Fixture"}""",
        """{"type":"user","message":{"role":"user","content":"Pozdrav, příliš žluťoučký kůň"},"uuid":"u1"}""",
        """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"Ahoj"},{"type":"tool_use","id":"t1","name":"Read","input":{"file_path":"/p/a.cs"}}]}}""",
        """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":"x"}]}}""",
        """{"type":"user","message":{"role":"user","content":[{"type":"text","text":"Další prompt"}]}}""",
        """{"type":"queue-operation","operation":"enqueue"}""",
    ];

    /// <summary>What a plain walk over the bytes says: where the lines start, and the first line of every turn (a prompt, or the first line of the file when it comes before one).</summary>
    private static (List<long> Starts, List<long> TurnFirstLines) Reference(byte[] bytes, Func<JsonElement, bool> isPrompt)
    {
        var starts = new List<long>();
        var turns = new List<long>();
        long position = 0;
        int line = 0;
        foreach (var text in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            long length = Encoding.UTF8.GetByteCount(text) + 1;
            if (position >= bytes.Length) break;
            starts.Add(position);
            string json = text.TrimEnd('\r');
            bool prompt = false;
            if (json.Trim().Length > 0)
            {
                try { using var doc = JsonDocument.Parse(json); prompt = isPrompt(doc.RootElement); }
                catch (JsonException) { /* not a record */ }
                if (prompt || turns.Count == 0) turns.Add(line);
            }
            position += length;
            line++;
        }
        return (starts, turns);
    }

    private static bool ClaudePrompt(JsonElement e) =>
        e.TryGetProperty("message", out var m) && m.TryGetProperty("role", out var r) && r.GetString() == "user" && m.TryGetProperty("content", out var c)
        && (c.ValueKind == JsonValueKind.String || !c.EnumerateArray().Any(b => b.TryGetProperty("type", out var t) && t.GetString() == "tool_result"));

    private static bool CodexPrompt(JsonElement e) =>
        e.TryGetProperty("type", out var t) && t.GetString() == "response_item" && e.TryGetProperty("payload", out var p) && p.TryGetProperty("type", out var pt) && pt.GetString() == "message"
        && p.TryGetProperty("role", out var role) && role.GetString() == "user";

    [Theory]
    [InlineData("claude", "\n")]
    [InlineData("claude", "\r\n")]
    [InlineData("codex", "\n")]
    [InlineData("codex", "\r\n")]
    public void The_index_of_a_transcript_gives_the_lines_offsets_and_turns_of_a_plain_walk_over_the_file(string format, string eol)
    {
        var lines = new List<string>();
        for (int round = 0; round < 40; round++)
            lines.AddRange(format == "claude" ? ClaudeSession.Skip(round % 3 == 0 ? 0 : 1) : Session.Skip(round % 3 == 0 ? 0 : 1));
        lines.Insert(7, ""); // a blank line is a line of its own and belongs to the turn before it
        using var file = Write(lines, eol);
        using var index = Open(file);

        var (starts, turns) = Reference(File.ReadAllBytes(file.Path), format == "claude" ? ClaudePrompt : CodexPrompt);
        Assert.Equal(starts.Count, index.LineCount);
        Assert.Equal(starts, Enumerable.Range(0, (int)index.LineCount).Select(i => index.StartOf(i)));
        Assert.Equal(turns.Count, index.TurnCount);
        Assert.Equal(turns, Enumerable.Range(0, index.TurnCount).Select(i => index.Turn(i).FirstLine));
        for (int i = 0; i < index.TurnCount; i++)
        {
            long end = i + 1 < index.TurnCount ? index.Turn(i + 1).FirstLine : index.LineCount;
            Assert.Equal(end - index.Turn(i).FirstLine, index.Turn(i).LineCount);
        }
        // the bytes behind an offset are the line
        for (int i = 0; i < index.LineCount; i += 17)
            Assert.Equal(lines[i], Encoding.UTF8.GetString(index.ReadLineBytes(i, 1 << 16, out _)));
    }

    // ---- the secrets ----

    [Fact]
    public void The_value_of_a_secrets_file_the_agent_read_is_hidden_in_rows_raw_lines_titles_and_hits()
    {
        using var file = Write(Session);
        using var index = Open(file);
        string all = AllText(index.ReadTurn(1));
        Assert.DoesNotContain(Key, all);
        Assert.Contains("API_KEY=" + SecretValues.Placeholder, all);
        Assert.Contains("MODE=" + SecretValues.Placeholder, all); // by position, so a short value of the file is hidden where the file is shown
        Assert.Contains(SecretValues.Placeholder + " and the mode is dev.", all);

        Assert.DoesNotContain(Key, index.ReadRaw(5));
        Assert.DoesNotContain(Key, index.ReadRaw(6));
        Assert.Contains("function_call_output", index.ReadRaw(5));

        var hits = new List<TranscriptHit>();
        index.Search(Key, hits.Add, CancellationToken.None);
        Assert.Empty(hits);
        index.Search("API_KEY", hits.Add, CancellationToken.None);
        var hit = Assert.Single(hits);
        Assert.Equal((5L, 1), (hit.Line, hit.Turn));
        Assert.DoesNotContain(Key, hit.Snippet);
    }

    [Fact]
    public void A_value_in_a_patch_that_writes_a_secrets_file_is_hidden_wherever_it_is_shown()
    {
        string patch = """{"type":"response_item","payload":{"type":"custom_tool_call","name":"apply_patch","call_id":"w","input":"*** Begin Patch\n*** Add File: config/prod.env\n+DB_PASSWORD=correct-horse-battery\n+DB_HOST=db.example.test\n*** End Patch"}}""";
        string echo = """{"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"Saved with correct-horse-battery."}]}}""";
        using var file = Write([Session[1], patch, echo]);
        using var index = Open(file);
        var content = index.ReadTurn(0);
        string all = AllText(content);
        Assert.DoesNotContain("correct-horse-battery", all);
        Assert.Contains("+DB_PASSWORD=" + SecretValues.Placeholder, all);
        Assert.Contains("Saved with " + SecretValues.Placeholder + ".", all);
        Assert.DoesNotContain("correct-horse-battery", index.ReadRaw(1));
        Assert.Equal([("config/prod.env", "apply_patch", 1L)], content.Files.Select(f => (f.Path, f.Tool, f.Line)));
    }

    [Fact]
    public void A_value_with_characters_that_JSON_escapes_is_hidden_in_the_raw_line_also_inside_the_arguments_of_a_call()
    {
        string read = """{"type":"response_item","payload":{"type":"function_call","name":"shell","arguments":"{\"command\":[\"cat\",\"C:\\\\proj\\\\.env\"]}","call_id":"r"}}""";
        string result = """{"type":"response_item","payload":{"type":"function_call_output","call_id":"r","output":"PASS='p@ss\"word-123'"}}""";
        string later = """{"type":"response_item","payload":{"type":"function_call","name":"shell","arguments":"{\"command\":[\"curl\",\"-u\",\"me:p@ss\\\"word-123\"]}","call_id":"n"}}""";
        using var file = Write([Session[1], read, result, later]);
        using var index = Open(file);
        Assert.DoesNotContain("word-123", AllText(index.ReadTurn(0)));
        Assert.DoesNotContain("word-123", index.ReadRaw(2)); // the quote is written \" in the line
        Assert.DoesNotContain("word-123", index.ReadRaw(3)); // and \\\" inside the arguments, which are JSON in a string
        Assert.Contains(SecretValues.Placeholder, index.ReadRaw(3));
    }

    [Fact]
    public void An_output_that_is_JSON_text_is_masked_by_every_text_value_it_holds_and_a_command_names_the_file()
    {
        string call = """{"type":"response_item","payload":{"type":"local_shell_call","call_id":"j","status":"completed","action":{"type":"exec","command":["bash","-lc","cat config/.env.local"]}}}""";
        string output = """{"type":"response_item","payload":{"type":"function_call_output","call_id":"j","output":"{\"output\":\"TOKEN=abcdef123456\\nREGION=eu\",\"metadata\":{\"exit_code\":0}}"}}""";
        string echo = """{"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"Token abcdef123456 found."}]}}""";
        using var file = Write([Session[1], call, output, echo]);
        using var index = Open(file);
        string all = AllText(index.ReadTurn(0));
        Assert.DoesNotContain("abcdef123456", all);
        Assert.Contains("Token " + SecretValues.Placeholder + " found.", all);
        Assert.DoesNotContain("abcdef123456", index.ReadRaw(2));
    }

    [Fact]
    public void An_output_of_a_call_that_names_no_secrets_file_is_shown_whole_and_the_calls_wait_only_up_to_the_limit()
    {
        string call = """{"type":"response_item","payload":{"type":"function_call","name":"shell","arguments":"{\"command\":[\"ls\"]}","call_id":"p"}}""";
        string output = """{"type":"response_item","payload":{"type":"function_call_output","call_id":"p","output":"MODE=dev"}}""";
        using var file = Write([Session[1], call, output]);
        using var index = Open(file);
        Assert.Contains("MODE=dev", AllText(index.ReadTurn(0)));

        var lines = new List<string> { Session[1] };
        for (int i = 0; i < 10; i++)
            lines.Add("{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"shell\",\"arguments\":\"{\\\"command\\\":[\\\"cat\\\",\\\".env\\\"]}\",\"call_id\":\"k" + i + "\"}}");
        using var many = Write(lines);
        using var small = Open(many, limits: Limits with { MaxPendingTools = 3 });
        Assert.Equal(11, small.LineCount); // the calls beyond the limit are forgotten together; the index goes on
    }

    [Fact]
    public void Reading_a_codex_file_never_changes_it()
    {
        using var file = Write(Session);
        var before = (File.ReadAllBytes(file.Path), File.GetLastWriteTimeUtc(file.Path));
        using (var index = Open(file))
        {
            index.ReadTurn(1);
            index.ReadRaw(4);
            index.Search("the", _ => { }, CancellationToken.None);
        }
        Assert.Equal(before.Item1, File.ReadAllBytes(file.Path));
        Assert.Equal(before.Item2, File.GetLastWriteTimeUtc(file.Path));
    }
}

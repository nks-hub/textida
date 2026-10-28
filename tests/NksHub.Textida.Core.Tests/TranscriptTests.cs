using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Core.Agents;
using NksHub.Textida.Core.Agents.Transcripts;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P8b.3.3 to P8b.3.5: the transcript of an agent session as turns, rows, diffs, files touched and hits of a search, read lazily from a handmade file; the values of a secrets file never reach any of them.
/// The key in the fixture is made up. The reader is run on the records the public documentation defines; everything else stays raw JSON.
/// </summary>
[Collection("SecretPatterns")]
public class TranscriptTests
{
    private const string Key = "hunter2hunter2";

    private static readonly TranscriptLimits Limits = new(
        WholeLineBytes: 1 << 16, PeekBytes: 512, IndexMaxBytes: 1 << 20, MaxLinesPerTurn: 200, MaxRowChars: 4000, MaxRawBytes: 1 << 16, MaxSearchHits: 100, PreviewChars: 60, MaxDepth: 64,
        MinSecretLength: 6, MaxPendingTools: 100);

    private static readonly string[] Session =
    [
        """{"type":"summary","summary":"Fixture session"}""",
        """{"type":"user","message":{"role":"user","content":"Please read the env file"},"uuid":"u1","timestamp":"2026-01-01T10:00:00Z"}""",
        """{"type":"assistant","message":{"id":"m1","role":"assistant","content":[{"type":"text","text":"Reading it."},{"type":"tool_use","id":"t1","name":"Read","input":{"file_path":"/work/proj/.env"}}]},"uuid":"a1"}""",
        """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":"     1→API_KEY=hunter2hunter2\n     2→MODE=dev"}]}}""",
        """{"type":"assistant","message":{"id":"m2","role":"assistant","content":[{"type":"text","text":"The key is hunter2hunter2 and the mode is dev."}]}}""",
        """{"type":"user","message":{"role":"user","content":"Now change the code"}}""",
        """{"type":"assistant","message":{"id":"m3","role":"assistant","content":[{"type":"thinking","thinking":"Plan the edit.","signature":"x"},{"type":"tool_use","id":"t2","name":"Edit","input":{"file_path":"/work/proj/src/app.cs","old_string":"int a = 1;\nint b = 2;","new_string":"int a = 1;\nint b = 3;"}}]}}""",
        """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t2","content":[{"type":"text","text":"The file was updated."}]}]}}""",
        """{"type":"assistant","message":{"id":"m4","role":"assistant","content":[{"type":"tool_use","id":"t3","name":"Write","input":{"file_path":"/work/proj/new.txt","content":"hello\nworld"}},{"type":"tool_use","id":"t4","name":"Bash","input":{"command":"dotnet test"}}]}}""",
        """{"type":"queue-operation","operation":"enqueue"}""",
    ];

    private static TempFile Write(IEnumerable<string> lines, string eol = "\n", bool bom = false)
    {
        var bytes = new List<byte>();
        if (bom) bytes.AddRange([0xEF, 0xBB, 0xBF]);
        bytes.AddRange(Encoding.UTF8.GetBytes(string.Join(eol, lines) + eol));
        return new TempFile([.. bytes]);
    }

    private static TranscriptIndex Open(TempFile file, TranscriptMasker? masker = null, TranscriptLimits? limits = null, bool build = true)
    {
        var index = TranscriptIndex.Open(file.Path, limits ?? Limits, masker ?? new TranscriptMasker((limits ?? Limits).MinSecretLength));
        if (build) index.Build(CancellationToken.None);
        return index;
    }

    private static string AllText(TurnContent content) => string.Join("\n", content.Rows.Select(r => r.Text));

    // ---- the index ----

    [Fact]
    public void The_index_finds_the_kind_of_every_line_and_the_turns()
    {
        using var file = Write(Session);
        using var index = Open(file);
        Assert.True(index.IsComplete);
        Assert.False(index.IsTruncated);
        Assert.Equal(Session.Length, index.LineCount);
        Assert.Equal([TranscriptKind.Other, TranscriptKind.UserPrompt, TranscriptKind.Assistant, TranscriptKind.ToolResult, TranscriptKind.Assistant, TranscriptKind.UserPrompt, TranscriptKind.Assistant, TranscriptKind.ToolResult, TranscriptKind.Assistant, TranscriptKind.Other],
            Enumerable.Range(0, Session.Length).Select(i => index.KindOf(i)));
        Assert.Equal(3, index.TurnCount);
        Assert.Equal((0, 0L, 1L), (index.Turn(0).Number, index.Turn(0).FirstLine, index.Turn(0).LineCount));
        Assert.Equal((1, 1L, 4L, "Please read the env file"), (index.Turn(1).Number, index.Turn(1).FirstLine, index.Turn(1).LineCount, index.Turn(1).Preview));
        Assert.Equal((2, 5L, 5L, "Now change the code"), (index.Turn(2).Number, index.Turn(2).FirstLine, index.Turn(2).LineCount, index.Turn(2).Preview));
        Assert.Equal([0, 1, 1, 1, 1, 2, 2, 2, 2, 2], Enumerable.Range(0, Session.Length).Select(i => index.TurnOf(i)));
    }

    [Fact]
    public void The_index_reads_a_file_with_CRLF_line_breaks_a_byte_order_mark_and_no_final_line_break()
    {
        using var file = Write(Session, "\r\n", bom: true);
        using var index = Open(file);
        Assert.Equal(Session.Length, index.LineCount);
        Assert.Equal(TranscriptKind.Other, index.KindOf(0));
        Assert.Equal(Session[0], Encoding.UTF8.GetString(index.ReadLineBytes(0, 1 << 16, out bool cut)));
        Assert.False(cut);
        Assert.Equal(Session[5], Encoding.UTF8.GetString(index.ReadLineBytes(5, 1 << 16, out _)));
        Assert.Equal(3, index.TurnCount);

        using var bare = new TempFile(Encoding.UTF8.GetBytes(string.Join("\n", Session.Take(2))));
        using var two = Open(bare);
        Assert.Equal(2, two.LineCount);
        Assert.Equal(Session[1], Encoding.UTF8.GetString(two.ReadLineBytes(1, 1 << 16, out _)));
    }

    [Fact]
    public void A_line_that_is_not_JSON_and_a_blank_line_do_not_break_the_turns()
    {
        using var file = Write(["not json at all", "", Session[1], "{\"type\":\"user\"", Session[2]]);
        using var index = Open(file);
        Assert.Equal(5, index.LineCount);
        Assert.Equal(TranscriptKind.Invalid, index.KindOf(0));
        Assert.Equal(TranscriptKind.Invalid, index.KindOf(3));
        var content = index.ReadTurn(0);
        Assert.Equal("not json at all", Assert.Single(content.Rows).Text);
        Assert.Equal(TranscriptRowKind.Raw, content.Rows[0].Kind);
    }

    [Fact]
    public void A_turn_longer_than_the_limit_goes_on_in_the_next_one()
    {
        var lines = new List<string> { Session[1] };
        for (int i = 0; i < 6; i++) lines.Add(Session[4]);
        using var file = Write(lines);
        using var index = Open(file, limits: Limits with { MaxLinesPerTurn = 3 });
        Assert.Equal(3, index.TurnCount);
        Assert.Equal([false, true, true], Enumerable.Range(0, 3).Select(i => index.Turn(i).Continued));
        Assert.Equal([1, 1, 1], Enumerable.Range(0, 3).Select(i => index.Turn(i).Number));
        Assert.Equal([3L, 3L, 1L], Enumerable.Range(0, 3).Select(i => index.Turn(i).LineCount));
        Assert.Equal(7, Enumerable.Range(0, 3).Sum(i => index.ReadTurn(i).Rows.Count));
    }

    [Fact]
    public void The_index_keeps_to_its_memory_limit_and_says_it_stopped()
    {
        using var file = Write(Enumerable.Repeat(Session[1], 500));
        using var index = Open(file, limits: Limits with { IndexMaxBytes = 2000 });
        Assert.True(index.IsComplete);
        Assert.True(index.IsTruncated);
        Assert.InRange(index.LineCount, 1, 499);
        Assert.InRange(index.IndexBytes, 1, 2200);
        Assert.True(index.TurnCount > 0);
    }

    [Fact]
    public void A_line_too_long_to_parse_is_classified_from_its_start_and_read_only_when_asked_for()
    {
        string big = "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"" + new string('x', 3000) + "\"}}";
        string bigAnswer = "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"tool_use_id\":\"t9\",\"type\":\"tool_result\",\"content\":\"" + new string('y', 3000) + "\"}]}}";
        string bigReply = "{\"message\":{\"id\":\"m9\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"" + new string('z', 3000) + "\"}]},\"type\":\"assistant\"}";
        using var file = Write([Session[1], big, bigReply, bigAnswer, Session[5]]);
        using var index = Open(file, limits: Limits with { WholeLineBytes = 1000, PeekBytes = 200 });
        Assert.Equal([TranscriptKind.UserPrompt, TranscriptKind.UserPrompt, TranscriptKind.Assistant, TranscriptKind.ToolResult, TranscriptKind.UserPrompt], Enumerable.Range(0, 5).Select(i => index.KindOf(i)));
        Assert.Equal(3, index.TurnCount);

        var content = index.ReadTurn(1);
        Assert.Equal(3, content.OversizedLines);
        Assert.All(content.Rows, r => Assert.Equal(TranscriptRowKind.Raw, r.Kind));
        Assert.All(content.Rows, r => Assert.True(r.Text.Length <= 1000 + 1));

        var hits = new List<TranscriptHit>();
        var summary = index.Search("please", hits.Add, CancellationToken.None);
        Assert.Equal(3, summary.Skipped);
        Assert.Equal(1, summary.Hits);
        Assert.Equal(0, hits[0].Line);
    }

    // ---- the turns ----

    [Fact]
    public void A_turn_shows_prompts_answers_tool_calls_and_results_with_the_files_it_touched()
    {
        using var file = Write(Session);
        using var index = Open(file);
        var second = index.ReadTurn(2);
        Assert.Equal(
            [TranscriptRowKind.User, TranscriptRowKind.Thinking, TranscriptRowKind.ToolCall, TranscriptRowKind.DiffRemoved, TranscriptRowKind.DiffAdded, TranscriptRowKind.ToolResult, TranscriptRowKind.ToolCall, TranscriptRowKind.DiffAdded, TranscriptRowKind.ToolCall, TranscriptRowKind.Raw],
            second.Rows.Select(r => r.Kind));
        Assert.Equal("Now change the code", second.Rows[0].Text);
        Assert.Equal("Plan the edit.", second.Rows[1].Text);
        Assert.Equal(("Edit", "/work/proj/src/app.cs"), (second.Rows[2].Tool, second.Rows[2].Path));
        Assert.Equal("The file was updated.", second.Rows[5].Text);
        Assert.Equal(("Bash", "dotnet test"), (second.Rows[8].Tool, second.Rows[8].Text));
        Assert.Contains("queue-operation", second.Rows[9].Text);
        Assert.Equal([5L, 6L, 6L, 6L, 6L, 7L, 8L, 8L, 8L, 9L], second.Rows.Select(r => r.Line));
        Assert.Equal([("/work/proj/src/app.cs", "Edit", 6L), ("/work/proj/new.txt", "Write", 8L)], second.Files.Select(f => (f.Path, f.Tool, f.Line)));
    }

    [Fact]
    public void An_edit_is_shown_as_the_lines_that_differ_and_a_write_as_the_lines_it_adds()
    {
        using var file = Write(Session);
        using var index = Open(file);
        var rows = index.ReadTurn(2).Rows;
        Assert.Equal("-int b = 2;", rows.Single(r => r.Kind == TranscriptRowKind.DiffRemoved).Text);
        Assert.Equal(["+int b = 3;", "+hello\n+world"], rows.Where(r => r.Kind == TranscriptRowKind.DiffAdded).Select(r => r.Text));
    }

    [Fact]
    public void An_edit_that_only_adds_or_only_removes_shows_one_side()
    {
        string add = """{"message":{"role":"assistant","content":[{"type":"tool_use","id":"a","name":"Edit","input":{"file_path":"/p/x.cs","old_string":"","new_string":"new line"}}]}}""";
        string remove = """{"message":{"role":"assistant","content":[{"type":"tool_use","id":"b","name":"Edit","input":{"file_path":"/p/x.cs","old_string":"gone\nstays","new_string":"stays"}}]}}""";
        using var file = Write([Session[1], add, remove]);
        using var index = Open(file);
        var rows = index.ReadTurn(0).Rows;
        Assert.Equal([TranscriptRowKind.User, TranscriptRowKind.ToolCall, TranscriptRowKind.DiffAdded, TranscriptRowKind.ToolCall, TranscriptRowKind.DiffRemoved], rows.Select(r => r.Kind));
        Assert.Equal(["+new line", "-gone"], rows.Where(r => r.Kind is TranscriptRowKind.DiffAdded or TranscriptRowKind.DiffRemoved).Select(r => r.Text));
    }

    [Fact]
    public void An_unknown_block_and_an_unknown_record_are_shown_as_raw_JSON()
    {
        string odd = """{"type":"assistant","message":{"role":"assistant","content":[{"type":"server_tool_use","id":"s1","name":"web_search","input":{"query":"q"}},{"type":"text","text":"After."}]}}""";
        using var file = Write([Session[1], odd, """{"type":"file-history-snapshot","messageId":"x"}"""]);
        using var index = Open(file);
        var rows = index.ReadTurn(0).Rows;
        Assert.Equal([TranscriptRowKind.User, TranscriptRowKind.Raw, TranscriptRowKind.Assistant, TranscriptRowKind.Raw], rows.Select(r => r.Kind));
        Assert.Equal("""{"type":"server_tool_use","id":"s1","name":"web_search","input":{"query":"q"}}""", rows[1].Text);
        Assert.Equal("""{"type":"file-history-snapshot","messageId":"x"}""", rows[3].Text);
    }

    [Fact]
    public void An_error_result_is_marked_and_a_long_row_is_cut()
    {
        string failed = """{"message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"q","is_error":true,"content":"boom"}]}}""";
        string longer = "{\"message\":{\"role\":\"assistant\",\"content\":\"" + new string('w', 100) + "\"}}";
        using var file = Write([Session[1], failed, longer]);
        using var index = Open(file, limits: Limits with { MaxRowChars = 40 });
        var rows = index.ReadTurn(0).Rows;
        Assert.Equal(TranscriptRowKind.ToolError, rows[1].Kind);
        Assert.Equal("boom", rows[1].Text);
        Assert.Equal(41, rows[2].Text.Length);
        Assert.EndsWith("…", rows[2].Text);
    }

    // ---- the secrets ----

    [Fact]
    public void The_value_of_a_secrets_file_the_agent_read_is_hidden_in_every_row_of_the_turn()
    {
        using var file = Write(Session);
        using var index = Open(file);
        var first = index.ReadTurn(1);
        string all = AllText(first);
        Assert.DoesNotContain(Key, all);
        Assert.Contains(SecretValues.Placeholder + " and the mode is dev.", all);
        Assert.Contains("MODE=" + SecretValues.Placeholder, all); // every value of the file is hidden where the file is shown, also a short one
        Assert.Equal([TranscriptRowKind.User, TranscriptRowKind.Assistant, TranscriptRowKind.ToolCall, TranscriptRowKind.ToolResult, TranscriptRowKind.Assistant], first.Rows.Select(r => r.Kind));
        Assert.Contains("API_KEY=" + SecretValues.Placeholder, first.Rows[3].Text);
        Assert.Equal(("Read", "/work/proj/.env"), (first.Rows[2].Tool, first.Rows[2].Path));
    }

    [Fact]
    public void The_raw_line_the_title_and_the_results_of_a_search_never_show_the_value()
    {
        using var file = Write(Session);
        using var index = Open(file);
        Assert.DoesNotContain(Key, index.ReadRaw(3));
        Assert.DoesNotContain(Key, index.ReadRaw(4));
        Assert.Contains(SecretValues.Placeholder, index.ReadRaw(4));
        Assert.Contains("tool_result", index.ReadRaw(3));

        var hits = new List<TranscriptHit>();
        index.Search(Key, hits.Add, CancellationToken.None);
        Assert.Empty(hits);
        index.Search("API_KEY", hits.Add, CancellationToken.None);
        var hit = Assert.Single(hits);
        Assert.Equal((3L, 1), (hit.Line, hit.Turn));
        Assert.DoesNotContain(Key, hit.Snippet);
        Assert.Contains("API_KEY=" + SecretValues.Placeholder, hit.Snippet);

        hits.Clear();
        index.Search("MODE IS DEV", hits.Add, CancellationToken.None);
        Assert.Equal([4L], hits.Select(h => h.Line));
    }

    [Fact]
    public void A_value_written_to_a_secrets_file_is_hidden_in_the_diff()
    {
        string write = """{"message":{"role":"assistant","content":[{"type":"tool_use","id":"w","name":"Write","input":{"file_path":"/p/prod.env","content":"DB_PASSWORD=correct-horse-battery\nDB_HOST=db.example.test"}}]}}""";
        string echo = """{"message":{"role":"assistant","content":[{"type":"text","text":"Saved with correct-horse-battery."}]}}""";
        using var file = Write([Session[1], write, echo]);
        using var index = Open(file);
        string all = AllText(index.ReadTurn(0));
        Assert.DoesNotContain("correct-horse-battery", all);
        Assert.Contains("+DB_PASSWORD=" + SecretValues.Placeholder, all);
        Assert.Contains("DB_HOST=" + SecretValues.Placeholder, all);
        Assert.Contains("Saved with " + SecretValues.Placeholder + ".", all);
    }

    [Fact]
    public void A_command_that_reads_a_secrets_file_makes_its_output_secret()
    {
        string call = """{"message":{"role":"assistant","content":[{"type":"tool_use","id":"c","name":"Bash","input":{"command":"cat config/.env.local"}}]}}""";
        string output = """{"message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"c","content":"TOKEN=abcdef123456\n"}]}}""";
        using var file = Write([Session[1], call, output]);
        using var index = Open(file);
        string all = AllText(index.ReadTurn(0));
        Assert.DoesNotContain("abcdef123456", all);
        Assert.Contains("TOKEN=" + SecretValues.Placeholder, all);
    }

    [Fact]
    public void Values_the_caller_knows_from_the_files_of_the_workspace_are_hidden_before_any_read()
    {
        string prompt = """{"message":{"role":"user","content":"my token is abcdef123456, keep it"}}""";
        using var file = Write([prompt]);
        var masker = new TranscriptMasker(Limits.MinSecretLength);
        masker.LearnFile("# workspace\nTOKEN=\"abcdef123456\"\nexport SHORT=ab\n");
        Assert.Equal(1, masker.KnownValues); // a value shorter than the limit would hide ordinary text
        using var index = Open(file, masker);
        Assert.Equal("my token is " + SecretValues.Placeholder + ", keep it", index.Turn(0).Preview);
        Assert.DoesNotContain("abcdef123456", index.ReadRaw(0));
        Assert.DoesNotContain("abcdef123456", AllText(index.ReadTurn(0)));
    }

    [Fact]
    public void A_value_with_characters_that_JSON_escapes_is_hidden_in_the_raw_line_too()
    {
        string read = """{"message":{"role":"assistant","content":[{"type":"tool_use","id":"r","name":"Read","input":{"file_path":"C:\\proj\\.env"}}]}}""";
        string result = """{"message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"r","content":"PASS='p@ss\"word-123'"}]}}""";
        string echo = """{"message":{"role":"assistant","content":[{"type":"text","text":"It is p@ss\"word-123 indeed"}]}}""";
        using var file = Write([Session[1], read, result, echo]);
        using var index = Open(file);
        Assert.DoesNotContain("word-123", AllText(index.ReadTurn(0)));
        Assert.DoesNotContain("word-123", index.ReadRaw(2)); // the quote is written \" inside the JSON line
        Assert.Contains("PASS=", index.ReadRaw(2));
        Assert.Contains(SecretValues.Placeholder, index.ReadRaw(2));
        Assert.DoesNotContain("word-123", index.ReadRaw(3));
    }

    [Fact]
    public void The_masker_only_hides_what_it_learned_and_follows_the_names_of_secret_files()
    {
        var masker = new TranscriptMasker(6);
        Assert.Equal("nothing to hide", masker.Mask("nothing to hide"));
        masker.Learn("tiny");
        Assert.Equal(0, masker.KnownValues);
        masker.Learn("sekret-value");
        masker.Learn("sekret-value");
        Assert.Equal(1, masker.KnownValues);
        Assert.Equal($"a {SecretValues.Placeholder} b {SecretValues.Placeholder}", masker.Mask("a sekret-value b sekret-value"));
        Assert.Equal($"K={SecretValues.Placeholder}\nZ={SecretValues.Placeholder}", masker.MaskSecretFile("K=other-secret\nZ=1")); // by position, every value of the file
        Assert.DoesNotContain("other-secret", masker.Mask("later: other-secret"));
        Assert.True(TranscriptMasker.TargetsSecretFile("/a/b/.env"));
        Assert.True(TranscriptMasker.TargetsSecretFile("cat .env.production | head"));
        Assert.True(TranscriptMasker.TargetsSecretFile("source \"prod.env\""));
        Assert.False(TranscriptMasker.TargetsSecretFile("dotnet test environment.txt"));
        Assert.False(TranscriptMasker.TargetsSecretFile(null));
    }

    // ---- reading is read only and lazy ----

    [Fact]
    public void Opening_indexing_reading_and_searching_never_change_the_file()
    {
        using var file = Write(Session);
        var before = (SHA256.HashData(File.ReadAllBytes(file.Path)), File.GetLastWriteTimeUtc(file.Path), new FileInfo(file.Path).Length);
        using (var index = Open(file))
        {
            index.ReadTurn(1);
            index.ReadRaw(2);
            index.Search("the", _ => { }, CancellationToken.None);
        }
        Assert.Equal(before.Item1, SHA256.HashData(File.ReadAllBytes(file.Path)));
        Assert.Equal(before.Item2, File.GetLastWriteTimeUtc(file.Path));
        Assert.Equal(before.Item3, new FileInfo(file.Path).Length);
    }

    [Fact]
    public void The_file_can_be_written_by_the_agent_while_the_index_is_open()
    {
        using var file = Write(Session);
        using var index = Open(file);
        File.AppendAllText(file.Path, Session[1] + "\n"); // a read handle that allows writers: no exception
        Assert.Equal(Session.Length, index.LineCount);
    }

    [Fact]
    public async Task The_index_can_be_built_on_another_thread_and_cancelled()
    {
        using var file = Write(Enumerable.Repeat(Session[1], 2000));
        using var index = Open(file, build: false);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => index.Build(cts.Token));
        Assert.True(index.IsComplete); // nothing more will be added

        using var other = Open(file, build: false);
        await other.BuildAsync(CancellationToken.None);
        Assert.Equal(2000, other.LineCount);
        Assert.Equal(2000, other.TurnCount);
    }

    [Fact]
    public async Task A_search_follows_a_line_index_that_is_still_growing_and_can_be_stopped()
    {
        using var file = Write(Enumerable.Repeat(Session[1], 3000));
        using var index = Open(file, limits: Limits with { MaxSearchHits = 10_000 }, build: false);
        var hits = new List<TranscriptHit>();
        var build = index.BuildAsync(CancellationToken.None);
        var summary = index.Search("env file", hits.Add, CancellationToken.None);
        await build;
        Assert.Equal(3000, summary.Hits);
        Assert.Equal(3000, hits.Count);

        using var limited = Open(file, limits: Limits with { MaxSearchHits = 5 });
        var few = new List<TranscriptHit>();
        var cut = limited.Search("env file", few.Add, CancellationToken.None);
        Assert.True(cut.Limited);
        Assert.Equal(5, few.Count);

        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => limited.Search("env", _ => { }, cancel.Token));
    }

    [Fact]
    public void A_prefix_of_a_line_that_cannot_be_parsed_does_not_throw()
    {
        foreach (var text in new[] { "", "{", "[1,2", "{\"message\":", "{\"message\":{\"role\":\"user\",\"content\":", "{\"a\":\"unterminated", "\u0001\u0002" })
            TranscriptParser.ClassifyPrefix(Encoding.UTF8.GetBytes(text), 60, out _);
        Assert.Equal(TranscriptKind.UserPrompt, TranscriptParser.ClassifyPrefix("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"abc"u8, 60, out _));
        Assert.Equal(TranscriptKind.ToolResult, TranscriptParser.ClassifyPrefix("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"tool_use_id\":\"a\",\"type\":\"tool_re"u8, 60, out _));
        Assert.Equal(TranscriptKind.Assistant, TranscriptParser.ClassifyPrefix("{\"message\":{\"id\":\"m\",\"role\":\"assistant\",\"content\":[{\"type\":\"te"u8, 60, out _));
    }

    // ---- the estimate of tokens ----

    [Fact]
    public void Tokens_are_estimated_from_the_published_rules_and_rounded_up()
    {
        Assert.Equal(0, TokenEstimator.Estimate(0, TokenEstimator.Generic));
        Assert.Equal(1, TokenEstimator.Estimate(1, TokenEstimator.Generic));
        Assert.Equal(2, TokenEstimator.Estimate(5, TokenEstimator.Generic));
        Assert.Equal(1000, TokenEstimator.Estimate(4000, TokenEstimator.OpenAi));
        Assert.Equal(1000, TokenEstimator.Estimate(4000, TokenEstimator.Gemini));
        Assert.Equal(100, TokenEstimator.Estimate(350, TokenEstimator.Claude));
        Assert.Equal(2, TokenEstimator.Estimate(7, TokenEstimator.Claude));
        Assert.Equal(130, TokenEstimator.Estimate(350, TokenEstimator.ClaudeNewTokenizer));
        Assert.Equal(TokenEstimator.Estimate(12, TokenEstimator.Generic), TokenEstimator.Estimate("twelve chars".AsSpan(), TokenEstimator.Generic));
        Assert.Equal(0, TokenEstimator.Estimate(-5, TokenEstimator.Generic));
    }

    [Fact]
    public void Every_preset_names_its_public_source_and_an_unknown_id_is_the_generic_estimate()
    {
        Assert.Equal(["generic", "openai", "gemini", "claude", "claude47"], TokenEstimator.Presets.Select(p => p.Id));
        Assert.All(TokenEstimator.Presets, p => Assert.False(string.IsNullOrWhiteSpace(p.Source)));
        Assert.Same(TokenEstimator.Generic, TokenEstimator.ById("something else"));
        Assert.Same(TokenEstimator.Generic, TokenEstimator.ById(null));
        Assert.Same(TokenEstimator.Claude, TokenEstimator.ById("claude"));
    }
}

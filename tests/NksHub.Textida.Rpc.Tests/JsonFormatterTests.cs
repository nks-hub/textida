using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Plugins.Json;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>The JSON formatter plugin: a real plugin process against the real host, document API and UI API.</summary>
public class JsonFormatterTests
{
    internal sealed class Owner : IDocumentOwnerThread
    {
        readonly object _gate = new();

        public Task<T> InvokeAsync<T>(Func<T> work)
        {
            try { lock (_gate) return Task.FromResult(work()); }
            catch (Exception e) { return Task.FromException<T>(e); }
        }
    }

    internal sealed class Proposals : IEditProposals
    {
        public readonly List<EditProposal> Items = [];

        public ValueTask<ProposalDecision> SubmitAsync(EditProposal proposal, CancellationToken cancellation)
        {
            lock (Items) { Items.Add(proposal); return ValueTask.FromResult(new ProposalDecision("p" + Items.Count, ProposalOutcome.Applied, proposal.Edits.Count, proposal.Edits.Count)); }
        }
    }

    internal sealed class Rig : IAsyncDisposable
    {
        public required PluginHost.PluginHost Host { get; init; }
        public required DocumentApi Docs { get; init; }
        public required Document Doc { get; init; }
        public required UiApiTests.RecordingUi Ui { get; init; }
        public required Proposals Proposals { get; init; }

        /// <param name="fileName">The document is a file of this name, so the plugin can tell JSONC from JSON; without it the document has no path.</param>
        /// <param name="program">A published (NativeAOT) program to run instead of the plugin DLL.</param>
        /// <param name="indexLines">The line index of the document is built on the thread pool; the command asks for line and column of the selection, so the test waits for the index
        /// unless it is switched off to see what the plugin says where the editor cannot place the selection.</param>
        public static async Task<Rig> CreateAsync(byte[] content, TextCodec codec, IReadOnlyList<(long Anchor, long Caret)>? selections, long maxBytes = 1 << 16,
            string? fileName = null, string? program = null, int maxDepth = 64, bool indexLines = true)
        {
            var proposals = new Proposals();
            var ui = new UiApiTests.RecordingUi();
            PluginHost.PluginHost? host = null;
            PluginEventSender send = (plugin, name, payload) => host!.SendEventAsync(plugin, name, payload);
            var permissions = new PluginPermissions();
            foreach (var permission in new[] { DocumentPermissions.Read, DocumentPermissions.EditPropose, DocumentPermissions.EditDirect })
                permissions.Grant("json", permission, GrantScope.All);
            var docs = new DocumentApi(new DocumentApiOptions
            {
                MaxReadBytes = 1 << 16, MaxFindMatches = 10, MaxSnapshotsPerPlugin = 2, SnapshotLifetime = TimeSpan.FromSeconds(10),
                EventQueueCapacity = 64, MaxRememberedBatches = 16, Permissions = permissions, CallLog = new PluginCallLog(100), Proposals = proposals,
                Tools = new DocumentApiTests.RecordingTools(), MaxSymbolsPerDocument = 10, MaxSymbolTextLength = 50, MaxSnippetLength = 100, MaxSnippetCarets = 2, TabTimeout = TimeSpan.FromSeconds(1), MaxSecretScanBytes = 1 << 20, MaxClipboardChars = 1000, MaxScanBytes = 1 << 22, MaxVirtualDocumentsPerPlugin = 4, MaxVirtualBytes = 1 << 20, MaxDiagnosticsPerDocument = 10, MaxCompletionItems = 10, MaxCompletionEdits = 4, CompletionTimeout = TimeSpan.FromSeconds(2),
            }, new Owner(), send, (plugin, method, payload, timeout, ct) => host!.CallAsync(plugin, method, payload, timeout, ct));
            var uiApi = new UiApi(new UiApiOptions { MaxPanelsPerPlugin = 2, MaxItemsPerPanel = 10, MaxTreeDepth = 3, MaxTasksPerPlugin = 2, MaxTextLength = 2000, MaxPickItems = 10, TaskCancelGrace = TimeSpan.FromSeconds(30) }, ui, send);
            host = new PluginHost.PluginHost(PluginHostTests.Options(), PluginApiRouter.Create(docs.HandleAsync, ("ui/", uiApi.HandleAsync)), docs);
            string[] options = ["--max-bytes", maxBytes.ToString(), "--indent", "2", "--max-depth", maxDepth.ToString()];
            if (program is not null) host.Register(new PluginSpec("json", "JSON", program, options, ["onCommand:json"]));
            else
            {
                var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Plugins.Json.dll");
                Assert.True(File.Exists(dll), dll);
                host.Register(new PluginSpec("json", "JSON", T.DotnetHost, [dll, .. options], ["onCommand:json"]));
            }
            await host.ActivateAsync("onCommand:json");
            string? path = null;
            if (fileName is not null)
            {
                path = Path.Combine(T.TempDir(), fileName);
                File.WriteAllBytes(path, content);
            }
            var options2 = new DocumentOptions { Format = codec };
            var factory = indexLines ? PagedBackendFactory.Instance : new PagedBackendFactory(startIndexing: false);
            var doc = path is null ? Document.FromBytes(content, factory, options2) : Document.Open(path, factory, options2);
            if (indexLines) await T.Until(() => doc.LineIndexState.Complete);
            Func<SelectionSet?>? selector = selections is null ? null
                : () => SelectionSet.Create([.. selections.Select(s => new TextSelection(s.Anchor, s.Caret))], 0);
            docs.Expose(doc, selector);
            return new Rig { Host = host, Docs = docs, Doc = doc, Ui = ui, Proposals = proposals };
        }

        public async Task<string> RunAsync(string command, long? revision = null, string language = "en", string extra = "")
        {
            var json = $"{{\"command\":\"{command}\",\"document\":\"{Doc.Id}\",\"revision\":{revision ?? Doc.Revision},\"language\":\"{language}\"{extra}}}";
            return (await Host.CallAsync("json", "command/execute", Encoding.UTF8.GetBytes(json))).GetProperty("status").GetString()!;
        }

        public byte[] Bytes() => Doc.GetSnapshot().ReadBytes(new ByteRange(0, Doc.ByteLength));

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await Docs.DisposeAsync();
            Doc.Dispose();
        }
    }

    static byte[] Cp1250Bytes(string s) => TextCodec.Cp1250.Encode(s);

    // head line, CRLF | line with JSON, no EOL inside the selection | LF | second JSON | CR | tail
    const string Head = "hlava ěš\r\n";
    const string Json1 = "{\"jméno\":\"žlutý\",\"seznam\":[1,2.50,{\"x\":null}],\"prázdný\":{}}";
    const string Middle = "\r\nstřed\n";
    const string Json2 = "[true,\"č\\u010d\"]";
    const string Tail = "\rocas ř\n";

    static (byte[] All, ByteRange First, ByteRange Second) MixedFile()
    {
        var head = Cp1250Bytes(Head);
        var j1 = Cp1250Bytes(Json1);
        var mid = Cp1250Bytes(Middle);
        var j2 = Cp1250Bytes(Json2);
        var tail = Cp1250Bytes(Tail);
        long a = head.Length, b = a + j1.Length, c = b + mid.Length, d = c + j2.Length;
        return ([.. head, .. j1, .. mid, .. j2, .. tail], new ByteRange(a, b), new ByteRange(c, d));
    }

    [Fact]
    public Task Two_selections_in_a_CP1250_file_with_mixed_line_endings_change_only_the_selected_ranges_in_one_undo_step() => TwoSelectionsInCp1250Async(null);

    /// <summary>The scenario of the test above; the NativeAOT test runs it against the published program.</summary>
    internal static async Task TwoSelectionsInCp1250Async(string? program)
    {
        var (all, first, second) = MixedFile();
        await using var rig = await Rig.CreateAsync(all, TextCodec.Cp1250, [(first.Start, first.End), (second.End, second.Start)], program: program); // the second one is selected backwards
        var undoBefore = rig.Doc.UndoDepth;

        Assert.Equal("applied", await rig.RunAsync(JsonPlugin.Format));

        var after = rig.Bytes();
        // neither selection holds a line break, so the lines the formatter breaks follow the file: CRLF is its most frequent ending (two against two LF and one CR, and CRLF wins a tie)
        var expected = Cp1250Bytes(Head + Format(Json1, "\r\n") + Middle + Format(Json2, "\r\n") + Tail);
        Assert.Equal(expected, after);
        // everything outside the two ranges is byte for byte what it was, including the three kinds of line ending
        Assert.Equal(all[..(int)first.Start], after[..(int)first.Start]);
        Assert.Equal(all[(int)second.End..], after[^(all.Length - (int)second.End)..]);
        Assert.Equal(undoBefore + 1, rig.Doc.UndoDepth);
        Assert.Contains((NoticeLevel.Info, "2 ranges formatted."), rig.Ui.Notices);

        rig.Doc.Undo();
        Assert.Equal(all, rig.Bytes());
    }

    static readonly JsonStyle Strict = new("\n", 2, Jsonc: false, MaxDepth: 64);
    static readonly JsonStyle Jsonc = Strict with { Jsonc = true };

    static string Format(string json, string newLine = "\n")
    {
        Assert.True(JsonFormatter.TryFormat(json, Strict with { NewLine = newLine }, out var formatted, out _));
        return formatted;
    }

    [Fact]
    public async Task Formatting_keeps_strings_and_numbers_as_written_and_uses_the_line_ending_of_the_range()
    {
        var content = Encoding.UTF8.GetBytes("  {\"a\":1.50,\r\n\"b\":\"\\u00e9 é\"}  \n");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null);
        Assert.Equal("applied", await rig.RunAsync(JsonPlugin.Format));
        Assert.Equal("  {\r\n  \"a\": 1.50,\r\n  \"b\": \"\\u00e9 é\"\r\n}  \n", Encoding.UTF8.GetString(rig.Bytes()));
    }

    [Fact]
    public async Task Without_a_selection_the_whole_document_is_formatted_after_its_BOM()
    {
        var content = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("[1,{\"a\":2}]")).ToArray();
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8Bom, null);
        Assert.Equal("applied", await rig.RunAsync(JsonPlugin.Format));
        var after = rig.Bytes();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, after[..3]);
        Assert.Equal("JSON formatted.", rig.Ui.Notices[^1].Message);
        Assert.Equal("[\n  1,\n  {\n    \"a\": 2\n  }\n]", Encoding.UTF8.GetString(after[3..]));
    }

    [Fact]
    public async Task A_syntax_error_is_reported_with_its_place_and_nothing_changes_even_when_another_range_is_valid()
    {
        var content = Encoding.UTF8.GetBytes("[1,2]\n{\"a\":1,}\n");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, [(0, 5), (6, 14)]);
        var revision = rig.Doc.Revision;

        Assert.Equal("syntaxError", await rig.RunAsync(JsonPlugin.Format));

        Assert.Equal(revision, rig.Doc.Revision);
        Assert.Equal(content, rig.Bytes());
        var (level, message) = Assert.Single(rig.Ui.Notices);
        Assert.Equal(NoticeLevel.Error, level);
        // the second range starts on the second line of the document, so the place is counted there and not from the start of the selection
        Assert.StartsWith("Range 2 of 2: JSON syntax error at line 2, column 8:", message);
    }

    [Fact]
    public async Task Preview_proposes_the_edits_and_leaves_the_document_alone()
    {
        var content = Encoding.UTF8.GetBytes("{\"a\":[1,2]}");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null);
        var revision = rig.Doc.Revision;

        Assert.Equal("proposed", await rig.RunAsync(JsonPlugin.Preview));

        Assert.Equal(content, rig.Bytes());
        var proposal = Assert.Single(rig.Proposals.Items);
        Assert.Equal(("json", revision, "Format JSON"), (proposal.PluginId, proposal.Revision, proposal.Title));
        var edit = Assert.Single(proposal.Edits);
        Assert.Equal(new ByteRange(0, content.Length), edit.Range);
        Assert.Equal("{\n  \"a\": [\n    1,\n    2\n  ]\n}", Encoding.UTF8.GetString(edit.Insert.Span));
    }

    [Fact]
    public async Task A_command_started_on_an_old_revision_is_rejected_and_the_user_is_told_to_run_it_again()
    {
        var content = Encoding.UTF8.GetBytes("{\"a\":1}");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null);
        var old = rig.Doc.Revision;
        rig.Doc.Apply([new ByteEdit(new ByteRange(7, 7), Encoding.UTF8.GetBytes(" "))], new EditOptions { BaseRevision = old, Source = "test" });
        var changed = rig.Bytes();

        Assert.Equal("stale", await rig.RunAsync(JsonPlugin.Format, old));
        Assert.Equal("stale", await rig.RunAsync(JsonPlugin.Preview, old));

        Assert.Equal(changed, rig.Bytes());
        Assert.Empty(rig.Proposals.Items);
        Assert.All(rig.Ui.Notices, n => Assert.Equal((NoticeLevel.Warning, "The document changed in the meantime. Run the command again."), n));
        Assert.Equal(2, rig.Ui.Notices.Count);
    }

    [Fact]
    public async Task Input_over_the_limit_is_refused_with_a_message_that_names_both_sizes()
    {
        var json = "{\"key\":\"" + new string('x', 40) + "\"}";
        var content = Encoding.UTF8.GetBytes(json);
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null, maxBytes: 30);

        Assert.Equal("tooLarge", await rig.RunAsync(JsonPlugin.Format));

        Assert.Equal(content, rig.Bytes());
        var (level, message) = Assert.Single(rig.Ui.Notices);
        Assert.Equal(NoticeLevel.Error, level);
        Assert.Equal($"The text is {content.Length} bytes; the limit is 30 bytes. Select a smaller part.", message);
    }

    [Fact]
    public async Task Text_that_is_already_formatted_is_reported_and_not_edited()
    {
        var content = Encoding.UTF8.GetBytes("{\n  \"a\": 1\n}");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null);
        var undo = rig.Doc.UndoDepth;
        Assert.Equal("unchanged", await rig.RunAsync(JsonPlugin.Format));
        Assert.Equal(undo, rig.Doc.UndoDepth);
        Assert.Equal("The JSON is already formatted.", rig.Ui.Notices[^1].Message);
    }

    [Theory]
    [InlineData("{}", "{}")]
    [InlineData("[ ]", "[]")]
    [InlineData("[[],{}]", "[\n  [],\n  {}\n]")]
    [InlineData("\n\t{\"a\":true}\r\n", "\n\t{\n  \"a\": true\n}\r\n")]
    public void The_pretty_printer_handles_empty_containers_and_keeps_outer_white_space(string input, string expected)
    {
        Assert.True(JsonFormatter.TryFormat(input, Strict, out var formatted, out _));
        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData("", 1, 1)]
    [InlineData("[1,]", 1, 4)]
    [InlineData("{\"a\":1}\n{", 2, 1)]
    [InlineData("// c\n[]", 1, 1)]
    [InlineData("{\"č\":x}", 1, 6)]
    public void Errors_carry_a_line_and_a_column_in_characters(string input, int line, int column)
    {
        Assert.False(JsonFormatter.TryFormat(input, Strict, out _, out var error));
        Assert.Equal((line, column), (error.Line, error.Column));
    }

    [Theory]
    [InlineData("[1,\n2,\n3 4]", 3, 3)]
    [InlineData("[1,\r\n2,\r\n3 4]", 3, 3)]
    [InlineData("[1,\r2,\r3 4]", 3, 3)]
    [InlineData("[1,\r2,\n3,\r\n4 5]", 4, 3)]
    public void A_line_ending_of_any_kind_ends_a_line_in_the_place_of_an_error_as_it_does_in_the_editor(string input, int line, int column)
    {
        Assert.False(JsonFormatter.TryFormat(input, Strict, out _, out var error));
        Assert.Equal((line, column), (error.Line, error.Column));
    }

    // ---- minify, sort keys, trailing commas ----

    [Fact]
    public async Task Minify_writes_formatted_text_of_a_CP1250_file_on_one_line_in_one_undo_step()
    {
        var head = Cp1250Bytes(Head);
        var body = Cp1250Bytes(Format(Json1));
        byte[] all = [.. head, .. body, .. Cp1250Bytes(Middle + Tail)];
        await using var rig = await Rig.CreateAsync(all, TextCodec.Cp1250, [(head.Length, head.Length + body.Length)]);
        var undoBefore = rig.Doc.UndoDepth;

        Assert.Equal("applied", await rig.RunAsync(JsonPlugin.Minify));

        Assert.Equal(Cp1250Bytes(Head + Json1 + Middle + Tail), rig.Bytes());
        Assert.Equal(undoBefore + 1, rig.Doc.UndoDepth);
        Assert.Equal((NoticeLevel.Info, "JSON minified."), rig.Ui.Notices[^1]);
        rig.Doc.Undo();
        Assert.Equal(all, rig.Bytes());
    }

    [Fact]
    public async Task Sort_keys_orders_the_members_in_a_CP1250_file_and_writes_them_with_the_line_ending_of_the_document()
    {
        const string unsorted = "{\"žaba\":1,\"abeceda\":[{\"b\":2,\"a\":1}],\"čáp\":3}";
        var before = Cp1250Bytes("řádek\r\n");
        var range = Cp1250Bytes(unsorted);
        byte[] all = [.. before, .. range, .. Cp1250Bytes("\r\nkonec\n")];
        await using var rig = await Rig.CreateAsync(all, TextCodec.Cp1250, [(before.Length, before.Length + range.Length)]);

        Assert.Equal("applied", await rig.RunAsync(JsonPlugin.SortKeys));

        // the selection holds no line break, so the lines it breaks follow the document (two CRLF, one LF)
        var expected = "řádek\r\n{\r\n  \"abeceda\": [\r\n    {\r\n      \"a\": 1,\r\n      \"b\": 2\r\n    }\r\n  ],\r\n  \"čáp\": 3,\r\n  \"žaba\": 1\r\n}\r\nkonec\n";
        Assert.Equal(Cp1250Bytes(expected), rig.Bytes());
        Assert.Equal((NoticeLevel.Info, "Keys sorted."), rig.Ui.Notices[^1]);
    }

    [Fact]
    public async Task Sorting_two_selections_is_one_undo_step()
    {
        var content = Encoding.UTF8.GetBytes("{\"b\":1,\"a\":2}\n{\"d\":1,\"c\":2}\n");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, [(0, 13), (14, 27)]);
        var undoBefore = rig.Doc.UndoDepth;

        Assert.Equal("applied", await rig.RunAsync(JsonPlugin.SortKeys));

        Assert.Equal("{\n  \"a\": 2,\n  \"b\": 1\n}\n{\n  \"c\": 2,\n  \"d\": 1\n}\n", Encoding.UTF8.GetString(rig.Bytes()));
        Assert.Equal(undoBefore + 1, rig.Doc.UndoDepth);
        Assert.Equal((NoticeLevel.Info, "2 ranges sorted."), rig.Ui.Notices[^1]);
    }

    [Fact]
    public async Task Removing_trailing_commas_repairs_a_JSON_document_and_changes_nothing_else()
    {
        var content = Encoding.UTF8.GetBytes("{\n  \"a\": [1, 2,],\n  \"b\": {\"c\": 1,},\n}\n");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null);

        Assert.Equal("applied", await rig.RunAsync(JsonPlugin.FixCommas));

        Assert.Equal("{\n  \"a\": [1, 2],\n  \"b\": {\"c\": 1}\n}\n", Encoding.UTF8.GetString(rig.Bytes()));
        Assert.Equal((NoticeLevel.Info, "Trailing commas removed."), rig.Ui.Notices[^1]);
        Assert.Equal("unchanged", await rig.RunAsync(JsonPlugin.FixCommas));
        Assert.Equal((NoticeLevel.Info, "No trailing comma was found."), rig.Ui.Notices[^1]);
    }

    [Fact]
    public async Task Sorting_with_the_preview_flag_proposes_the_edit_and_leaves_the_document_alone()
    {
        var content = Encoding.UTF8.GetBytes("{\"b\":1,\"a\":2}");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null);

        Assert.Equal("proposed", await rig.RunAsync(JsonPlugin.SortKeys, extra: ",\"preview\":true"));

        Assert.Equal(content, rig.Bytes());
        var proposal = Assert.Single(rig.Proposals.Items);
        Assert.Equal("Sort JSON Keys", proposal.Title);
        Assert.Equal("{\n  \"a\": 2,\n  \"b\": 1\n}", Encoding.UTF8.GetString(Assert.Single(proposal.Edits).Insert.Span));
    }

    [Fact]
    public async Task Minify_sort_and_comma_removal_obey_the_size_limit_of_the_formatter()
    {
        var content = Encoding.UTF8.GetBytes("{\"key\":\"" + new string('x', 40) + "\"}");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null, maxBytes: 30);

        foreach (var command in new[] { JsonPlugin.Minify, JsonPlugin.SortKeys, JsonPlugin.FixCommas })
            Assert.Equal("tooLarge", await rig.RunAsync(command));

        Assert.Equal(content, rig.Bytes());
        Assert.All(rig.Ui.Notices, n => Assert.Equal((NoticeLevel.Error, $"The text is {content.Length} bytes; the limit is 30 bytes. Select a smaller part."), n));
    }

    // ---- JSONC, line endings, error places ----

    [Fact]
    public async Task A_JSONC_file_keeps_its_comments_and_trailing_commas_when_formatted()
    {
        var content = Encoding.UTF8.GetBytes("// settings\n{\"a\":1, // one\n\"b\":[1,2,],}\n");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null, fileName: "settings.jsonc");

        Assert.Equal("applied", await rig.RunAsync(JsonPlugin.Format));

        Assert.Equal("// settings\n{\n  \"a\": 1, // one\n  \"b\": [\n    1,\n    2,\n  ],\n}\n", Encoding.UTF8.GetString(rig.Bytes()));
    }

    [Fact]
    public async Task A_file_named_like_a_JSONC_file_is_read_as_JSONC()
    {
        var content = Encoding.UTF8.GetBytes("{\"compilerOptions\":{/* c */\"strict\":true}}");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null, fileName: "tsconfig.json");
        Assert.Equal("applied", await rig.RunAsync(JsonPlugin.Format));
    }

    [Fact]
    public async Task The_same_comment_in_a_plain_JSON_file_is_a_syntax_error_that_says_what_to_do_and_nothing_changes()
    {
        var content = Encoding.UTF8.GetBytes("{\n  // note\n  \"a\": 1\n}");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null, fileName: "settings.json");

        Assert.Equal("syntaxError", await rig.RunAsync(JsonPlugin.Format));

        Assert.Equal(content, rig.Bytes());
        var (level, message) = Assert.Single(rig.Ui.Notices);
        Assert.Equal(NoticeLevel.Error, level);
        Assert.Equal("JSON syntax error at line 2, column 3: Comments are not allowed in JSON. Use a .jsonc file or remove them.", message);
    }

    [Fact]
    public async Task A_selection_without_a_line_break_takes_the_line_ending_of_the_document()
    {
        var content = Encoding.UTF8.GetBytes("a\r\n{\"k\":[1]}\r\nb\r\n");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, [(3, 12)]);

        Assert.Equal("applied", await rig.RunAsync(JsonPlugin.Format));

        Assert.Equal("a\r\n{\r\n  \"k\": [\r\n    1\r\n  ]\r\n}\r\nb\r\n", Encoding.UTF8.GetString(rig.Bytes()));
    }

    [Theory]
    [InlineData("en", "JSON syntax error at line 3, column 11: A trailing comma is not allowed in JSON. Run \"Remove JSON Trailing Commas\" first.")]
    [InlineData("cs", "Chyba syntaxe JSON na řádku 3, sloupci 11: V JSON není čárka před zavírací závorkou povolená.")]
    [InlineData("sk", "Chyba syntaxe JSON na riadku 3, stĺpci 11: V JSON nie je čiarka pred zatváracou zátvorkou povolená.")]
    [InlineData("de", "JSON-Syntaxfehler in Zeile 3, Spalte 11: Ein abschließendes Komma ist in JSON nicht erlaubt.")]
    public async Task An_error_in_a_selection_is_placed_in_the_document_and_worded_in_the_language_of_the_editor(string language, string start)
    {
        // the selection starts on the third line after three spaces, so the error of the range (line 1, column 8) is at line 3, column 11 of the document
        var content = Encoding.UTF8.GetBytes("one\ntwo\n   {\"a\":1,}\nend\n");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, [(11, 19)]);

        Assert.Equal("syntaxError", await rig.RunAsync(JsonPlugin.Format, language: language));

        Assert.StartsWith(start, Assert.Single(rig.Ui.Notices).Message);
        Assert.Equal(content, rig.Bytes());
    }

    [Fact]
    public async Task Where_the_editor_cannot_place_the_selection_the_error_is_counted_from_the_selection_and_the_message_says_so()
    {
        var content = Encoding.UTF8.GetBytes("one\ntwo\n   {\"a\":1,}\nend\n");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, [(11, 19)], indexLines: false);

        Assert.Equal("syntaxError", await rig.RunAsync(JsonPlugin.Format));

        Assert.StartsWith("JSON syntax error at line 1, column 8 of the selected text:", Assert.Single(rig.Ui.Notices).Message);
        Assert.Equal(content, rig.Bytes());
    }

    [Fact]
    public async Task Minify_of_a_JSONC_file_drops_its_comments_and_trailing_commas_as_the_help_says_and_leaves_strict_JSON()
    {
        var content = Encoding.UTF8.GetBytes("// settings\n{\"a\":1, // one\n\"b\":[1,2,],}\n");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null, fileName: "settings.jsonc");
        var undoBefore = rig.Doc.UndoDepth;

        Assert.Equal("applied", await rig.RunAsync(JsonPlugin.Minify));

        var minified = Encoding.UTF8.GetString(rig.Bytes());
        Assert.Equal("{\"a\":1,\"b\":[1,2]}\n", minified);
        using (JsonDocument.Parse(minified)) { } // strict parser: no comment, no trailing comma
        Assert.Equal(undoBefore + 1, rig.Doc.UndoDepth);
        rig.Doc.Undo();
        Assert.Equal(content, rig.Bytes());
    }

    [Fact]
    public async Task Nesting_deeper_than_the_limit_is_refused_with_the_limit_in_the_message()
    {
        var content = Encoding.UTF8.GetBytes("[[[[1]]]]");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null, maxDepth: 3);

        Assert.Equal("syntaxError", await rig.RunAsync(JsonPlugin.Format));

        Assert.Equal("JSON syntax error at line 1, column 4: The nesting is deeper than 3 levels.", Assert.Single(rig.Ui.Notices).Message);
    }

    // ---- the formatter without a process ----

    const string Commented = "// top\n{ // after open\n  \"b\": 1, // about b\n  // own line\n  \"a\": [1, /* inline */ 2,],\n  /* block\n  two lines */\n  \"c\": /* val */ \"x\" // last\n  // dangling\n}\n// end";

    [Fact]
    public void Format_keeps_every_comment_with_its_member_and_every_trailing_comma_of_a_JSONC_text()
    {
        Assert.True(JsonFormatter.TryFormat(Commented, Jsonc, out var formatted, out _));
        Assert.Equal("// top\n{ // after open\n  \"b\": 1, // about b\n  // own line\n  \"a\": [\n    1,\n    /* inline */\n    2,\n  ],\n  /* block\n  two lines */\n  /* val */\n  \"c\": \"x\" // last\n  // dangling\n}\n// end", formatted);

        Assert.True(JsonFormatter.TryFormat(formatted, Jsonc, out var again, out _));
        Assert.Equal(formatted, again);
    }

    [Fact]
    public void Sort_keys_moves_a_comment_with_its_member_and_leaves_the_comments_of_the_object_in_place()
    {
        Assert.True(JsonFormatter.TrySortKeys(Commented, Jsonc, out var sorted, out _));
        Assert.Equal("// top\n{ // after open\n  // own line\n  \"a\": [\n    1,\n    /* inline */\n    2,\n  ],\n  \"b\": 1, // about b\n  /* block\n  two lines */\n  /* val */\n  \"c\": \"x\" // last\n  // dangling\n}\n// end", sorted);
    }

    [Fact]
    public void Minify_drops_comments_and_trailing_commas_so_the_result_is_strict_JSON()
    {
        Assert.True(JsonFormatter.TryMinify(Commented, Jsonc, out var minified, out _));
        Assert.Equal("{\"b\":1,\"a\":[1,2],\"c\":\"x\"}", minified);
        Assert.True(JsonFormatter.TryMinify("\n  [ 1 , { \"k\" : \"v w\" } ]  \n", Strict, out var outer, out _));
        Assert.Equal("\n  [1,{\"k\":\"v w\"}]  \n", outer);
    }

    /// <summary>The check every result passes. No input makes the printers go wrong today, so it is tested with the results a bug could produce.</summary>
    [Theory]
    [InlineData("{\"a\":1}", "{\"a\":1", false)]            // no longer valid
    [InlineData("{\"a\":1}", "{\"a\":2}", false)]           // a value changed
    [InlineData("{\"a\":1}", "{\"a\":\"1\"}", false)]       // a number became a string
    [InlineData("{\"a\":1}", "{\"a\":1,\"b\":2}", false)]   // a member appeared
    [InlineData("[1,2]", "[2,1]", false)]                   // order changed where it must not
    [InlineData("{\"a\":1}", "{ \"a\" : 1 }", true)]        // white space only
    public void A_result_that_is_not_valid_or_not_the_same_tokens_is_not_taken(string before, string after, bool taken)
    {
        Assert.Equal(taken, JsonFormatter.Accept(before, after, Strict, sorted: false, comments: true, out var result, out var error));

        Assert.Equal(taken ? after : before, result);
        Assert.Equal(taken ? JsonErrorKind.Syntax : JsonErrorKind.SelfCheck, error.Kind);
    }

    [Fact]
    public void The_check_lets_sorting_reorder_tokens_and_a_comment_move_but_not_vanish()
    {
        Assert.False(JsonFormatter.Accept("{\"b\":1,\"a\":2}", "{\"a\":2,\"b\":1}", Strict, sorted: false, comments: true, out _, out _));
        Assert.True(JsonFormatter.Accept("{\"b\":1,\"a\":2}", "{\"a\":2,\"b\":1}", Strict, sorted: true, comments: true, out _, out _));

        Assert.False(JsonFormatter.Accept("[1, /* c */ 2]", "[1,2]", Jsonc, sorted: false, comments: true, out _, out var lost));
        Assert.Equal(JsonErrorKind.SelfCheck, lost.Kind);
        Assert.True(JsonFormatter.Accept("[1, /* c */ 2]", "[1,\n/* c */ 2]", Jsonc, sorted: false, comments: true, out _, out _));
        Assert.True(JsonFormatter.Accept("[1, /* c */ 2]", "[1,2]", Strict, sorted: false, comments: false, out _, out _)); // minify drops comments on purpose
    }

    [Fact]
    public void Removing_trailing_commas_touches_only_those_commas_also_next_to_a_comment()
    {
        Assert.True(JsonFormatter.TryFixTrailingCommas(Commented, 64, out var fixedText, out _));
        Assert.Equal(Commented.Replace("2,]", "2]"), fixedText);
        Assert.True(JsonFormatter.TryFixTrailingCommas("[1, // c\n]", 64, out var withComment, out _));
        Assert.Equal("[1 // c\n]", withComment);
        Assert.True(JsonFormatter.TryFixTrailingCommas("{\"a\":\"x,]\",}", 64, out var inString, out _));
        Assert.Equal("{\"a\":\"x,]\"}", inString);
    }

    [Fact]
    public void Equal_names_keep_their_order_and_a_name_is_compared_as_the_characters_it_stands_for()
    {
        Assert.True(JsonFormatter.TrySortKeys("{\"b\":1,\"\\u0061\":2,\"a\":3}", Strict, out var sorted, out _));
        Assert.Equal("{\n  \"\\u0061\": 2,\n  \"a\": 3,\n  \"b\": 1\n}", sorted);
    }

    [Theory]
    [InlineData("// c\n[]", JsonErrorKind.Comment)]
    [InlineData("[1,]", JsonErrorKind.TrailingComma)]
    [InlineData("{\"a\":1,\n}", JsonErrorKind.TrailingComma)]
    [InlineData("[1 2]", JsonErrorKind.Syntax)]
    [InlineData("[[[[1]]]]", JsonErrorKind.Depth)]
    public void A_strict_text_names_what_is_wrong_with_it(string input, JsonErrorKind kind)
    {
        Assert.False(JsonFormatter.TryFormat(input, Strict with { MaxDepth = 3 }, out _, out var error));
        Assert.Equal(kind, error.Kind);
    }

    [Fact]
    public void The_depth_limit_is_the_one_that_was_asked_for_and_never_deeper_than_the_stack_allows()
    {
        var style = Strict with { MaxDepth = 3 };
        Assert.True(JsonFormatter.TryFormat("[[[1]]]", style, out _, out _));
        Assert.False(JsonFormatter.TryFormat("[[[[1]]]]", style, out _, out _));
        var deep = new string('[', JsonFormatter.StackSafeDepth + 1) + new string(']', JsonFormatter.StackSafeDepth + 1);
        Assert.False(JsonFormatter.TryFormat(deep, Strict with { MaxDepth = int.MaxValue }, out _, out var error));
        Assert.Equal(JsonErrorKind.Depth, error.Kind);
    }

    [Theory]
    [InlineData("C:\\work\\settings.jsonc", true)]
    [InlineData("C:\\work\\tsconfig.json", true)]
    [InlineData("C:\\work\\settings.json", false)]
    [InlineData(null, false)]
    public void A_file_counts_as_JSONC_by_its_name(string? path, bool jsonc) => Assert.Equal(jsonc, JsonPlugin.IsJsonc(path));
}

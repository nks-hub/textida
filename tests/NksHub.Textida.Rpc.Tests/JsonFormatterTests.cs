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
    sealed class Owner : IDocumentOwnerThread
    {
        readonly object _gate = new();

        public Task<T> InvokeAsync<T>(Func<T> work)
        {
            try { lock (_gate) return Task.FromResult(work()); }
            catch (Exception e) { return Task.FromException<T>(e); }
        }
    }

    sealed class Proposals : IEditProposals
    {
        public readonly List<EditProposal> Items = [];

        public ValueTask<ProposalDecision> SubmitAsync(EditProposal proposal, CancellationToken cancellation)
        {
            lock (Items) { Items.Add(proposal); return ValueTask.FromResult(new ProposalDecision("p" + Items.Count, ProposalOutcome.Applied, proposal.Edits.Count, proposal.Edits.Count)); }
        }
    }

    sealed class Rig : IAsyncDisposable
    {
        public required PluginHost.PluginHost Host { get; init; }
        public required DocumentApi Docs { get; init; }
        public required Document Doc { get; init; }
        public required UiApiTests.RecordingUi Ui { get; init; }
        public required Proposals Proposals { get; init; }

        public static async Task<Rig> CreateAsync(byte[] content, TextCodec codec, IReadOnlyList<(long Anchor, long Caret)>? selections, long maxBytes = 1 << 16)
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
                Tools = new DocumentApiTests.RecordingTools(), MaxSymbolsPerDocument = 10, MaxSymbolTextLength = 50, MaxSnippetLength = 100, MaxSnippetCarets = 2, TabTimeout = TimeSpan.FromSeconds(1), MaxSecretScanBytes = 1 << 20, MaxClipboardChars = 1000,
            }, new Owner(), send, (plugin, method, payload, timeout, ct) => host!.CallAsync(plugin, method, payload, timeout, ct));
            var uiApi = new UiApi(new UiApiOptions { MaxPanelsPerPlugin = 2, MaxItemsPerPanel = 10, MaxTreeDepth = 3, MaxTasksPerPlugin = 2, MaxTextLength = 2000, MaxPickItems = 10 }, ui, send);
            host = new PluginHost.PluginHost(PluginHostTests.Options(), PluginApiRouter.Create(docs.HandleAsync, ("ui/", uiApi.HandleAsync)), docs);
            var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Plugins.Json.dll");
            Assert.True(File.Exists(dll), dll);
            host.Register(new PluginSpec("json", "JSON", T.DotnetHost, [dll, "--max-bytes", maxBytes.ToString(), "--indent", "2"], ["onCommand:json"]));
            await host.ActivateAsync("onCommand:json");
            var doc = Document.FromBytes(content, PagedBackendFactory.Instance, new DocumentOptions { Format = codec });
            Func<SelectionSet?>? selector = selections is null ? null
                : () => SelectionSet.Create([.. selections.Select(s => new TextSelection(s.Anchor, s.Caret))], 0);
            docs.Expose(doc, selector);
            return new Rig { Host = host, Docs = docs, Doc = doc, Ui = ui, Proposals = proposals };
        }

        public async Task<string> RunAsync(string command, long? revision = null)
        {
            var json = $"{{\"command\":\"{command}\",\"document\":\"{Doc.Id}\",\"revision\":{revision ?? Doc.Revision},\"language\":\"en\"}}";
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
    public async Task Two_selections_in_a_CP1250_file_with_mixed_line_endings_change_only_the_selected_ranges_in_one_undo_step()
    {
        var (all, first, second) = MixedFile();
        await using var rig = await Rig.CreateAsync(all, TextCodec.Cp1250, [(first.Start, first.End), (second.End, second.Start)]); // the second one is selected backwards
        var undoBefore = rig.Doc.UndoDepth;

        Assert.Equal("applied", await rig.RunAsync(FormatCommand.Format));

        var after = rig.Bytes();
        var expected = Cp1250Bytes(Head + Format(Json1) + Middle + Format(Json2) + Tail);
        Assert.Equal(expected, after);
        // everything outside the two ranges is byte for byte what it was, including the three kinds of line ending
        Assert.Equal(all[..(int)first.Start], after[..(int)first.Start]);
        Assert.Equal(all[(int)second.End..], after[^(all.Length - (int)second.End)..]);
        Assert.Equal(undoBefore + 1, rig.Doc.UndoDepth);
        Assert.Contains((NoticeLevel.Info, "2 ranges formatted."), rig.Ui.Notices);

        rig.Doc.Undo();
        Assert.Equal(all, rig.Bytes());
    }

    static string Format(string json)
    {
        Assert.True(JsonFormatter.TryFormat(json, "\n", 2, out var formatted, out _));
        return formatted;
    }

    [Fact]
    public async Task Formatting_keeps_strings_and_numbers_as_written_and_uses_the_line_ending_of_the_range()
    {
        var content = Encoding.UTF8.GetBytes("  {\"a\":1.50,\r\n\"b\":\"\\u00e9 é\"}  \n");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null);
        Assert.Equal("applied", await rig.RunAsync(FormatCommand.Format));
        Assert.Equal("  {\r\n  \"a\": 1.50,\r\n  \"b\": \"\\u00e9 é\"\r\n}  \n", Encoding.UTF8.GetString(rig.Bytes()));
    }

    [Fact]
    public async Task Without_a_selection_the_whole_document_is_formatted_after_its_BOM()
    {
        var content = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("[1,{\"a\":2}]")).ToArray();
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8Bom, null);
        Assert.Equal("applied", await rig.RunAsync(FormatCommand.Format));
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

        Assert.Equal("syntaxError", await rig.RunAsync(FormatCommand.Format));

        Assert.Equal(revision, rig.Doc.Revision);
        Assert.Equal(content, rig.Bytes());
        var (level, message) = Assert.Single(rig.Ui.Notices);
        Assert.Equal(NoticeLevel.Error, level);
        Assert.StartsWith("Range 2 of 2: JSON syntax error at line 1, column 8:", message);
    }

    [Fact]
    public async Task Preview_proposes_the_edits_and_leaves_the_document_alone()
    {
        var content = Encoding.UTF8.GetBytes("{\"a\":[1,2]}");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null);
        var revision = rig.Doc.Revision;

        Assert.Equal("proposed", await rig.RunAsync(FormatCommand.Preview));

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

        Assert.Equal("stale", await rig.RunAsync(FormatCommand.Format, old));
        Assert.Equal("stale", await rig.RunAsync(FormatCommand.Preview, old));

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

        Assert.Equal("tooLarge", await rig.RunAsync(FormatCommand.Format));

        Assert.Equal(content, rig.Bytes());
        var (level, message) = Assert.Single(rig.Ui.Notices);
        Assert.Equal(NoticeLevel.Error, level);
        Assert.Equal($"The text to format is {content.Length} bytes; the limit is 30 bytes. Select a smaller part.", message);
    }

    [Fact]
    public async Task Text_that_is_already_formatted_is_reported_and_not_edited()
    {
        var content = Encoding.UTF8.GetBytes("{\n  \"a\": 1\n}");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, null);
        var undo = rig.Doc.UndoDepth;
        Assert.Equal("unchanged", await rig.RunAsync(FormatCommand.Format));
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
        Assert.True(JsonFormatter.TryFormat(input, "\n", 2, out var formatted, out _));
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
        Assert.False(JsonFormatter.TryFormat(input, "\n", 2, out _, out var error));
        Assert.Equal((line, column), (error.Line, error.Column));
    }
}

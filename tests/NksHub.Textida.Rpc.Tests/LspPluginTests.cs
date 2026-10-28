using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Plugins.Lsp;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>
/// P8.5.4 and P8.5.5: the LSP plugin as a real process against the real host and document API, talking to a small fake language server (a second real process)
/// that keeps its own copy of every text. Roslyn and csharp-ls are not needed and not started.
/// </summary>
public class LspPluginTests
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

    sealed class NoProposals : IEditProposals
    {
        public ValueTask<ProposalDecision> SubmitAsync(EditProposal proposal, CancellationToken cancellation) => throw new NotSupportedException();
    }

    /// <summary>The change review of the editor, reduced to what the tests look at: it keeps what it was shown and applies all of it, as the user accepting every change does.</summary>
    sealed class Review(Owner owner) : IWorkspaceEdits
    {
        public readonly List<Document> Open = [];
        public readonly List<WorkspaceEditRequest> Shown = [];

        public async ValueTask<ProposalDecision> SubmitAsync(WorkspaceEditRequest request, CancellationToken cancellation)
        {
            lock (Shown) Shown.Add(request);
            var report = await owner.InvokeAsync(() => new ChangeApplier(id => Open.FirstOrDefault(d => d.Id == id)).Apply(request.Proposal));
            int applied = report.Files.Count(f => f.Status == ChangeStatus.Applied);
            return new ProposalDecision("review" + Shown.Count, applied == report.Files.Count ? ProposalOutcome.Applied : ProposalOutcome.Failed, applied, report.Files.Count);
        }
    }

    sealed class Rig : IAsyncDisposable
    {
        public required Review Review { get; init; }
        public required PluginHost.PluginHost Host { get; init; }
        public required DocumentApi Docs { get; init; }
        public required string Folder { get; init; }
        public required string Dump { get; init; }
        public required Owner Owner { get; init; }
        public required UiApiTests.RecordingUi Ui { get; init; }
        public required DocumentApiTests.RecordingTools Tools { get; init; }
        public List<Document> Documents => Review.Open;

        /// <summary>The launch description of the server a plugin manifest declares; sent with every command, as the editor does. Null: the server comes from the catalog.</summary>
        public string? Launch { get; init; }

        /// <param name="declared">The server is not in the catalog (it is empty) but sent with each command, as the editor does for a server of a plugin manifest.</param>
        public static async Task<Rig> CreateAsync(string[]? serverArgs = null, long maxBytes = 1 << 16, string? externalName = null, string? externalContent = null, string? program = null,
            bool declared = false, int idleStopMs = 0, string? initializationOptions = null)
        {
            var folder = Path.Combine(Path.GetTempPath(), "textida-lsp-" + Guid.NewGuid().ToString("N"));
            var dump = Path.Combine(folder, "dump");
            Directory.CreateDirectory(dump);
            var fake = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.FakeLsp.dll");
            Assert.True(File.Exists(fake), fake);
            var arguments = new List<string> { fake, "--dump", dump };
            if (externalName is not null)
            {
                File.WriteAllBytes(Path.Combine(folder, externalName), new UTF8Encoding(false).GetBytes(externalContent!));
                arguments.AddRange(["--external", Path.Combine(folder, externalName)]);
            }
            arguments.AddRange(serverArgs ?? []);
            var catalog = Path.Combine(folder, "catalog.json");
            var entry = new { language = "fake", program = program ?? T.DotnetFullPath, arguments };
            File.WriteAllText(catalog, declared ? "[]" : JsonSerializer.Serialize(new[] { entry }));
            string? launch = null;
            if (declared)
            {
                launch = JsonSerializer.Serialize(entry);
                if (initializationOptions is not null) launch = launch[..^1] + ",\"initializationOptions\":" + initializationOptions + "}";
            }

            var ui = new UiApiTests.RecordingUi();
            var tools = new DocumentApiTests.RecordingTools();
            var owner = new Owner();
            var review = new Review(owner);
            PluginHost.PluginHost? host = null;
            PluginEventSender send = (plugin, name, payload) => host!.SendEventAsync(plugin, name, payload);
            var permissions = new PluginPermissions();
            foreach (var permission in new[] { DocumentPermissions.Read, DocumentPermissions.EditPropose, DocumentPermissions.WorkspaceFiles }) permissions.Grant("lsp", permission, GrantScope.All);
            var docs = new DocumentApi(new DocumentApiOptions
            {
                MaxReadBytes = 1 << 16, MaxFindMatches = 10, MaxSnapshotsPerPlugin = 2, SnapshotLifetime = TimeSpan.FromSeconds(10),
                EventQueueCapacity = 64, MaxRememberedBatches = 16, Permissions = permissions, CallLog = new PluginCallLog(100), Proposals = new NoProposals(),
                Tools = tools, MaxSymbolsPerDocument = 10, MaxSymbolTextLength = 400, MaxSnippetLength = 100, MaxSnippetCarets = 2, TabTimeout = TimeSpan.FromSeconds(1),
                MaxSecretScanBytes = 1 << 20, MaxClipboardChars = 1000, MaxScanBytes = 1 << 22, MaxVirtualDocumentsPerPlugin = 4, MaxVirtualBytes = 1 << 20,
                MaxDiagnosticsPerDocument = 100, MaxCompletionItems = 100, MaxCompletionEdits = 4, CompletionTimeout = TimeSpan.FromSeconds(5),
                Workspace = new WorkspaceEditOptions(review, () => [folder], path => review.Open.Any(d => d.FilePath is { } p && PathUri.SamePath(p, path)), 20, 1 << 20),
            }, owner, send, (plugin, method, payload, timeout, ct) => host!.CallAsync(plugin, method, payload, timeout, ct));
            var uiApi = new UiApi(new UiApiOptions { MaxPanelsPerPlugin = 2, MaxItemsPerPanel = 100, MaxTreeDepth = 3, MaxTextLength = 2000, MaxTasksPerPlugin = 2, MaxPickItems = 10, TaskCancelGrace = TimeSpan.FromSeconds(30) }, ui, send);
            host = new PluginHost.PluginHost(PluginHostTests.Options(), PluginApiRouter.Create(docs.HandleAsync, ("ui/", uiApi.HandleAsync)), docs);
            var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Plugins.Lsp.dll");
            Assert.True(File.Exists(dll), dll);
            host.Register(new PluginSpec("lsp", "LSP", T.DotnetHost,
                [dll, "--max-bytes", maxBytes.ToString(), "--start-timeout-ms", "20000", "--request-timeout-ms", "10000", "--stop-timeout-ms", "3000", "--max-locations", "50",
                 "--stderr-lines", "5", "--max-message-chars", "100", "--max-diagnostics", "50", "--catalog", catalog, "--idle-stop-ms", idleStopMs.ToString(), "--root-search-levels", "8"], ["onCommand:lsp"]));
            await host.ActivateAsync("onCommand:lsp");
            return new Rig { Review = review, Host = host, Docs = docs, Folder = folder, Dump = dump, Owner = owner, Ui = ui, Tools = tools, Launch = launch };
        }

        /// <summary>Opens a file with this content and shows it to plugins.</summary>
        public Document Add(string name, string content, TextCodec? codec = null)
        {
            codec ??= TextCodec.Utf8;
            var path = Path.Combine(Folder, name);
            File.WriteAllBytes(path, codec.WithBom(false).Encode(content));
            var doc = Document.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = codec });
            Docs.Expose(doc, null);
            Documents.Add(doc);
            return doc;
        }

        /// <param name="launch">The launch description to send instead of <see cref="Launch"/>, as the editor does when the server a document is served by changes.</param>
        public async Task<JsonElement> RunAsync(Document doc, string command, long? offset = null, string? launch = null)
        {
            launch ??= Launch;
            var json = $"{{\"command\":\"{command}\",\"document\":\"{doc.Id}\",\"revision\":{doc.Revision},\"language\":\"en\",\"lspLanguage\":\"fake\"{(offset is { } o ? $",\"offset\":{o}" : "")}{(launch is null ? "" : ",\"lspServer\":" + launch)}}}";
            return (await Host.CallAsync("lsp", "command/execute", Encoding.UTF8.GetBytes(json))).Clone();
        }

        public Task Edit(Document doc, long start, long end, string text) => Owner.InvokeAsync(() => doc.Replace(new ByteRange(start, end), Encoding.UTF8.GetBytes(text)).Applied);

        public static string TextOf(Document doc, TextCodec? codec = null) => (codec ?? TextCodec.Utf8).WithBom(false).Decode(doc.GetSnapshot().ReadBytes(new ByteRange(doc.BomLength, doc.ByteLength))).Text;

        string DumpFile(Document doc, string extension) => Path.Combine(Dump, Path.GetFileName(doc.FilePath!) + extension);

        /// <summary>Waits until the fake server has handled the revision the document has now, and returns the text it holds.</summary>
        public async Task<string> ServerTextAsync(Document doc)
        {
            await T.Until(() =>
            {
                try { return File.Exists(DumpFile(doc, ".version")) && long.Parse(File.ReadAllText(DumpFile(doc, ".version"))) == doc.Revision; }
                catch (IOException) { return false; }
            });
            return File.ReadAllText(DumpFile(doc, ".txt"), new UTF8Encoding(false));
        }

        public string Stats(Document doc) => File.ReadAllText(DumpFile(doc, ".stats"));

        public IReadOnlyList<PlacedDiagnostic> DiagnosticsOf(Document doc) => Owner.InvokeAsync(() => Docs.DiagnosticsOf(doc.Id)).Result;

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await Docs.DisposeAsync();
            foreach (var doc in Documents) doc.Dispose();
            try { Directory.Delete(Folder, true); }
            catch (IOException) { }
        }
    }

    static long Bytes(string text) => Encoding.UTF8.GetByteCount(text);

    [Fact]
    public async Task After_1000_edits_the_server_holds_the_text_of_the_editor_and_got_it_as_ranges()
    {
        await using var rig = await Rig.CreateAsync();
        var model = "é€😀 first line\nsecond line\n";
        var doc = rig.Add("many.fake", model);
        Assert.Equal("ok", (await rig.RunAsync(doc, LspPlugin.StartCommand)).GetProperty("status").GetString());

        var random = new Random(7);
        string[] pieces = ["a", "é", "€", "😀", "\n", "foo ", "ERROR", "\n\n", "x y"];
        int Safe(int i) => i > 0 && i < model.Length && char.IsLowSurrogate(model[i]) ? i + 1 : i;
        for (int n = 0; n < 1000; n++)
        {
            int start = Safe(random.Next(model.Length + 1)), length = random.Next(3) == 0 && start < model.Length ? Math.Min(random.Next(1, 6), model.Length - start) : 0;
            int end = Safe(start + length);
            var piece = random.Next(4) == 0 && end > start ? "" : pieces[random.Next(pieces.Length)];
            await rig.Edit(doc, Bytes(model[..start]), Bytes(model[..end]), piece);
            model = model[..start] + piece + model[end..];
        }

        await rig.Edit(doc, Bytes(model), Bytes(model), "\nEND"); // the last edit surely changes the text, so the server's version moves to this revision
        model += "\nEND";
        Assert.Equal(model, Rig.TextOf(doc));
        Assert.Equal(model, await rig.ServerTextAsync(doc));
        var stats = rig.Stats(doc);
        Assert.StartsWith("opens=1 ", stats);
        Assert.EndsWith("full=0", stats);
        Assert.DoesNotContain("incremental=0 ", stats);

        // what the server found wrong is where the editor has the text: one finding per ERROR, on its bytes
        int errors = model.Split("ERROR").Length - 1;
        Assert.True(errors > 0);
        await T.Until(() => rig.DiagnosticsOf(doc).Count == errors);
        var snapshot = doc.GetSnapshot();
        Assert.All(rig.DiagnosticsOf(doc), d => Assert.Equal("ERROR", Encoding.UTF8.GetString(snapshot.ReadBytes(d.Range))));
    }

    [Fact]
    public async Task A_server_that_wants_whole_texts_gets_them()
    {
        await using var rig = await Rig.CreateAsync(["--full"]);
        var doc = rig.Add("whole.fake", "é one\ntwo\n");
        await rig.RunAsync(doc, LspPlugin.StartCommand);
        await rig.Edit(doc, Bytes("é one\n"), Bytes("é one\n"), "😀 inserted ");
        await rig.Edit(doc, 0, 0, "first\n");

        Assert.Equal(Rig.TextOf(doc), await rig.ServerTextAsync(doc));
        Assert.Contains("incremental=0", rig.Stats(doc));
        Assert.DoesNotContain("full=0", rig.Stats(doc));
    }

    [Fact]
    public async Task Diagnostics_stand_on_the_bytes_of_the_finding_and_go_when_it_is_fixed_in_a_new_revision()
    {
        await using var rig = await Rig.CreateAsync();
        var text = "é€😀 ok ERROR end\n";
        var doc = rig.Add("diag.fake", text);
        await rig.RunAsync(doc, LspPlugin.StartCommand);

        await T.Until(() => rig.DiagnosticsOf(doc).Count == 1);
        var diagnostic = Assert.Single(rig.DiagnosticsOf(doc));
        Assert.Equal(("lsp", "lsp", DiagnosticSeverity.Error, "found ERROR", "E1"), (diagnostic.PluginId, diagnostic.Source, diagnostic.Severity, diagnostic.Message, diagnostic.Code));
        Assert.Equal(new ByteRange(Bytes("é€😀 ok "), Bytes("é€😀 ok ERROR")), diagnostic.Range);

        var fixedAt = doc.Revision;
        await rig.Edit(doc, diagnostic.Range.Start, diagnostic.Range.End, "FINE!");
        Assert.True(doc.Revision > fixedAt);
        await rig.ServerTextAsync(doc);
        await T.Until(() => rig.DiagnosticsOf(doc).Count == 0);
    }

    [Theory]
    [InlineData("UTF-8")]
    [InlineData("UTF-16 LE")]
    public async Task Definition_lands_on_the_bytes_the_server_returned_in_a_file_with_multibyte_characters(string codecName)
    {
        var codec = TextCodec.ByName(codecName)!;
        await using var rig = await Rig.CreateAsync();
        var text = "é€😀 use foo here\nint 😀é def foo = 1;\n";
        var doc = rig.Add("def.fake", text, codec);
        long B(string prefix) => codec.WithBom(false).Encode(prefix).Length + doc.BomLength;
        int use = text.IndexOf("foo", StringComparison.Ordinal), def = text.IndexOf("def foo", StringComparison.Ordinal) + 4;

        var r = await rig.RunAsync(doc, LspPlugin.DefinitionCommand, B(text[..(use + 1)]));

        Assert.Equal("ok", r.GetProperty("status").GetString());
        var place = Assert.Single(r.GetProperty("locations").EnumerateArray());
        Assert.Equal((B(text[..def]), B(text[..(def + 3)])), (place.GetProperty("start").GetInt64(), place.GetProperty("end").GetInt64()));
        Assert.Equal(1, place.GetProperty("line").GetInt32());
        Assert.Equal(12, place.GetProperty("character").GetInt32()); // UTF-16 units: "int " 4, the emoji 2, "é" 1, " def " 5
        Assert.True(place.GetProperty("inDocument").GetBoolean());
        Assert.True(r.GetProperty("revealed").GetBoolean());
        var revealed = Assert.Single(rig.Tools.Revealed);
        Assert.Equal(new ByteRange(B(text[..def]), B(text[..(def + 3)])), revealed);
        Assert.Equal("foo", codec.WithBom(false).Decode(doc.GetSnapshot().ReadBytes(revealed)).Text);
    }

    [Fact]
    public async Task A_definition_in_another_file_is_named_with_its_bytes_and_a_caret_in_a_stale_revision_is_refused()
    {
        await using var rig = await Rig.CreateAsync(externalName: "other.fake", externalContent: "é😀 def bar = 2\n");
        var doc = rig.Add("use.fake", "call bar\n");

        var r = await rig.RunAsync(doc, LspPlugin.DefinitionCommand, Bytes("call b"));

        var place = Assert.Single(r.GetProperty("locations").EnumerateArray());
        Assert.False(place.GetProperty("inDocument").GetBoolean());
        Assert.False(r.GetProperty("revealed").GetBoolean());
        Assert.Equal("other.fake", Path.GetFileName(place.GetProperty("path").GetString()));
        Assert.Equal((Bytes("é😀 def "), Bytes("é😀 def bar")), (place.GetProperty("start").GetInt64(), place.GetProperty("end").GetInt64()));

        var stale = doc.Revision;
        await rig.Edit(doc, 0, 0, "x");
        var json = $"{{\"command\":\"lsp.definition\",\"document\":\"{doc.Id}\",\"revision\":{stale},\"language\":\"en\",\"lspLanguage\":\"fake\",\"offset\":5}}";
        Assert.Equal("stale", (await rig.Host.CallAsync("lsp", "command/execute", Encoding.UTF8.GetBytes(json))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task References_are_listed_in_a_panel_and_a_word_without_a_definition_says_so()
    {
        await using var rig = await Rig.CreateAsync();
        var text = "foo é foo\n😀 foo(foo)\nfoobar\n";
        var doc = rig.Add("refs.fake", text);

        var r = await rig.RunAsync(doc, LspPlugin.ReferencesCommand, Bytes("fo"));

        Assert.Equal(4, r.GetProperty("count").GetInt32());
        var panel = Assert.Single(rig.Ui.Panels);
        Assert.Equal(LspPlugin.LocationsPanel, panel.Id);
        Assert.Equal(4, panel.Items.Count);
        var starts = r.GetProperty("locations").EnumerateArray().Select(l => l.GetProperty("start").GetInt64()).ToList();
        Assert.Equal(new long[] { 0, Bytes("foo é "), Bytes("foo é foo\n😀 "), Bytes("foo é foo\n😀 foo(") }, starts);

        var none = await rig.RunAsync(doc, LspPlugin.DefinitionCommand, Bytes("foo é foo\n😀 foo(foo)\nfoo"));
        Assert.Equal("none", none.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_crash_is_reported_and_the_next_start_hands_every_open_document_to_the_new_server_again()
    {
        await using var rig = await Rig.CreateAsync();
        var first = rig.Add("one.fake", "é one ERROR\n");
        var second = rig.Add("two.fake", "two\n");
        await rig.RunAsync(first, LspPlugin.StartCommand);
        await rig.RunAsync(second, LspPlugin.StartCommand);
        await T.Until(() => rig.DiagnosticsOf(first).Count == 1);

        await rig.Edit(first, 0, 0, "CRASH ");
        await T.Until(() => rig.Ui.Notices.Any(n => n.Message.Contains("stopped unexpectedly") && n.Message.Contains("exit code 3")));
        await T.Until(() => rig.DiagnosticsOf(first).Count == 0); // what the dead server said is no longer true

        // while the server is away both documents change; nobody is told
        await rig.Edit(first, 0, Bytes("CRASH "), "");
        await rig.Edit(second, Bytes("two"), Bytes("two"), " 😀 changed");
        File.Delete(Path.Combine(rig.Dump, "one.fake.txt"));
        File.Delete(Path.Combine(rig.Dump, "two.fake.txt"));

        var r = await rig.RunAsync(first, LspPlugin.RestartCommand);

        Assert.Equal("restarted", r.GetProperty("status").GetString());
        Assert.Equal(Rig.TextOf(first), await rig.ServerTextAsync(first));
        Assert.Equal(Rig.TextOf(second), await rig.ServerTextAsync(second));
        Assert.StartsWith("opens=1 ", rig.Stats(second)); // a new process: it was opened, not changed
        await T.Until(() => rig.DiagnosticsOf(first).Count == 1);
    }

    [Fact]
    public async Task A_stopped_server_leaves_no_diagnostics_and_a_command_starts_it_again()
    {
        await using var rig = await Rig.CreateAsync();
        var doc = rig.Add("stop.fake", "ERROR\n");
        await rig.RunAsync(doc, LspPlugin.StartCommand);
        await T.Until(() => rig.DiagnosticsOf(doc).Count == 1);

        Assert.Equal("stopped", (await rig.RunAsync(doc, LspPlugin.StopCommand)).GetProperty("status").GetString());
        Assert.Empty(rig.DiagnosticsOf(doc));

        Assert.Equal("ok", (await rig.RunAsync(doc, LspPlugin.StartCommand)).GetProperty("status").GetString());
        await T.Until(() => rig.DiagnosticsOf(doc).Count == 1);
    }

    [Fact]
    public async Task A_file_over_the_limit_never_starts_a_server_and_a_language_without_one_says_so()
    {
        await using var rig = await Rig.CreateAsync(maxBytes: 100);
        var doc = rig.Add("big.fake", new string('x', 200));

        Assert.Equal("tooLarge", (await rig.RunAsync(doc, LspPlugin.StartCommand)).GetProperty("status").GetString());
        Assert.Empty(Directory.GetFiles(rig.Dump)); // the fake server never ran

        var small = rig.Add("small.fake", "x");
        var json = $"{{\"command\":\"lsp.start\",\"document\":\"{small.Id}\",\"revision\":{small.Revision},\"language\":\"en\",\"lspLanguage\":\"nothing\"}}";
        Assert.Equal("noLanguage", (await rig.Host.CallAsync("lsp", "command/execute", Encoding.UTF8.GetBytes(json))).GetProperty("status").GetString());
    }

    // ---- P8.5.1: completion, rename and the status bar ----

    static async Task<CompletionAnswer> CompleteAsync(Rig rig, Document doc, long offset)
    {
        await T.Until(() => rig.Docs.HasCompletionProviders);
        return await rig.Docs.RequestCompletionAsync(doc.Id, offset);
    }

    [Fact]
    public async Task A_completion_with_an_additional_edit_is_written_as_one_undo_step()
    {
        await using var rig = await Rig.CreateAsync();
        var text = "é€😀 Con\n";
        var doc = rig.Add("complete.fake", text);
        await rig.RunAsync(doc, LspPlugin.StartCommand);
        var depth = doc.UndoDepth;

        var answer = await CompleteAsync(rig, doc, Bytes("é€😀 Con"));

        Assert.False(answer.Stale);
        var item = Assert.Single(answer.Items);
        Assert.Equal(("Console", "class", "class System.Console"), (item.Label, item.Kind, item.Detail));
        Assert.Equal(new ByteRange(Bytes("é€😀 "), Bytes("é€😀 Con")), item.Range);
        var extra = Assert.Single(item.AdditionalEdits);
        Assert.Equal((new ByteRange(0, 0), "using System;\n"), (extra.Range, extra.Text));

        await rig.Owner.InvokeAsync(() => rig.Docs.ApplyCompletion(item));

        Assert.Equal("using System;\né€😀 Console\n", Rig.TextOf(doc));
        Assert.Equal(depth + 1, doc.UndoDepth);
        await rig.Owner.InvokeAsync(() => doc.Undo());
        Assert.Equal(text, Rig.TextOf(doc));
        Assert.Equal(depth, doc.UndoDepth);
    }

    [Fact]
    public async Task A_completion_answered_for_a_text_the_document_has_left_is_dropped()
    {
        await using var rig = await Rig.CreateAsync();
        var doc = rig.Add("slow.fake", "SLOW Con\n");
        await rig.RunAsync(doc, LspPlugin.StartCommand);
        await T.Until(() => rig.Docs.HasCompletionProviders);

        var pending = rig.Docs.RequestCompletionAsync(doc.Id, Bytes("SLOW Con")); // the fake server needs a second for this one
        await Task.Delay(300);
        await rig.Edit(doc, 0, 0, "x");
        var answer = await pending;

        Assert.Empty(answer.Items);
        var fresh = await CompleteAsync(rig, doc, Bytes("xSLOW Con"));
        Assert.Single(fresh.Items);
    }

    [Fact]
    public async Task A_document_the_plugin_does_not_follow_gets_no_completion_and_no_server_is_started_for_it()
    {
        await using var rig = await Rig.CreateAsync();
        var followed = rig.Add("followed.fake", "x\n");
        await rig.RunAsync(followed, LspPlugin.StartCommand);
        var other = rig.Add("other.fake", "Con\n");

        var answer = await CompleteAsync(rig, other, Bytes("Con"));

        Assert.Empty(answer.Items);
        Assert.False(File.Exists(Path.Combine(rig.Dump, "other.fake.txt")));
    }

    [Fact]
    public async Task A_rename_across_two_files_shows_both_in_one_review_and_applies_them_together()
    {
        await using var rig = await Rig.CreateAsync();
        var first = rig.Add("one.fake", "é foo = foo + 1\n");
        var second = rig.Add("two.fake", "😀 call foo(foo)\nfoobar\n");
        await rig.RunAsync(first, LspPlugin.StartCommand);
        await rig.RunAsync(second, LspPlugin.StartCommand);
        rig.Ui.Input = request => request.Value == "foo" ? "baz" : null;

        var r = await rig.RunAsync(first, LspPlugin.RenameCommand, Bytes("é f"));

        Assert.Equal(("ok", "applied", 2, 2), (r.GetProperty("status").GetString(), r.GetProperty("decision").GetString(), r.GetProperty("accepted").GetInt32(), r.GetProperty("total").GetInt32()));
        var shown = Assert.Single(rig.Review.Shown);
        Assert.True(shown.Proposal.Atomic);
        Assert.Equal("Rename foo to baz", shown.Proposal.Title);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), shown.Proposal.Files.Select(f => f.DocumentId!.Value).Order());
        Assert.Equal([2, 2], shown.Proposal.Files.Select(f => f.Hunks.Count));
        Assert.Equal("é baz = baz + 1\n", Rig.TextOf(first));
        Assert.Equal("😀 call baz(baz)\nfoobar\n", Rig.TextOf(second));
        Assert.Equal(1, first.UndoDepth);
        Assert.Equal(1, second.UndoDepth);
    }

    [Fact]
    public async Task A_rename_that_creates_renames_and_deletes_files_and_edits_one_on_disk_goes_through_the_same_review()
    {
        await using var rig = await Rig.CreateAsync(["--fileops"], externalName: "disk.fake", externalContent: "é foo on disk\n");
        var doc = rig.Add("main.fake", "foo\n");
        File.WriteAllText(Path.Combine(rig.Folder, "doomed.fake"), "bye");
        File.WriteAllText(Path.Combine(rig.Folder, "moveme.fake"), "move");
        await rig.RunAsync(doc, LspPlugin.StartCommand);
        rig.Ui.Input = _ => "bar";

        var r = await rig.RunAsync(doc, LspPlugin.RenameCommand, 1);

        Assert.Equal(("ok", "applied"), (r.GetProperty("status").GetString(), r.GetProperty("decision").GetString()));
        var files = Assert.Single(rig.Review.Shown).Proposal.Files;
        Assert.Equal(5, files.Count);
        Assert.Equal([FileOperationKind.Create, FileOperationKind.Delete, FileOperationKind.Rename], files.Where(f => f.Operation is not null).Select(f => f.Operation!.Kind));
        Assert.Equal("bar\n", Rig.TextOf(doc));
        Assert.Equal("é bar on disk\n", File.ReadAllText(Path.Combine(rig.Folder, "disk.fake"), new UTF8Encoding(false)));
        Assert.True(File.Exists(Path.Combine(rig.Folder, "created.fake")));
        Assert.False(File.Exists(Path.Combine(rig.Folder, "doomed.fake")));
        Assert.False(File.Exists(Path.Combine(rig.Folder, "moveme.fake")));
        Assert.Equal("move", File.ReadAllText(Path.Combine(rig.Folder, "moved.fake")));
    }

    [Fact]
    public async Task A_rename_the_user_does_not_confirm_changes_nothing()
    {
        await using var rig = await Rig.CreateAsync();
        var doc = rig.Add("keep.fake", "foo foo\n");
        await rig.RunAsync(doc, LspPlugin.StartCommand);

        var r = await rig.RunAsync(doc, LspPlugin.RenameCommand, 1); // the input answers nothing

        Assert.Equal("cancelled", r.GetProperty("status").GetString());
        Assert.Empty(rig.Review.Shown);
        Assert.Equal("foo foo\n", Rig.TextOf(doc));
    }

    [Fact]
    public async Task The_status_bar_says_when_the_server_runs_when_it_was_not_found_and_when_it_is_off()
    {
        string? Last(Rig rig) { lock (rig.Ui.Statuses) return rig.Ui.Statuses.LastOrDefault()?.Text; }

        await using (var rig = await Rig.CreateAsync())
        {
            var doc = rig.Add("status.fake", "x\n");
            await rig.RunAsync(doc, LspPlugin.StartCommand);
            await T.Until(() => Last(rig) == "LSP: running");
            await rig.RunAsync(doc, LspPlugin.StopCommand);
            await T.Until(() => Last(rig) == "LSP: off");
        }
        await using (var missing = await Rig.CreateAsync(program: "no-such-language-server-program"))
        {
            var doc = missing.Add("lost.fake", "x\n");
            Assert.Equal("noServer", (await missing.RunAsync(doc, LspPlugin.StartCommand)).GetProperty("status").GetString());
            await T.Until(() => Last(missing) == "LSP: not found");
        }
    }

    // ---- P8.5.2: a server a plugin manifest declares ----

    static int Processes(Rig rig) => Directory.GetFiles(rig.Dump, "process-*").Length;

    [Fact]
    public async Task A_declared_server_gets_the_options_and_serves_completion_and_rename_with_no_catalog_entry()
    {
        await using var rig = await Rig.CreateAsync(declared: true, initializationOptions: "{\"mode\":\"strict\"}");
        var first = rig.Add("one.fake", "é foo = foo + 1\nCon\n");
        var second = rig.Add("two.fake", "call foo(foo)\n");
        Assert.Equal("ok", (await rig.RunAsync(first, LspPlugin.StartCommand)).GetProperty("status").GetString());
        await rig.RunAsync(second, LspPlugin.StartCommand);

        Assert.Equal("{\"mode\":\"strict\"}", File.ReadAllText(Path.Combine(rig.Dump, "init-options.json")));

        var answer = await CompleteAsync(rig, first, Bytes("é foo = foo + 1\nCon"));
        Assert.Equal("Console", Assert.Single(answer.Items).Label);

        rig.Ui.Input = request => request.Value == "foo" ? "baz" : null;
        var r = await rig.RunAsync(first, LspPlugin.RenameCommand, Bytes("é f"));
        Assert.Equal(("ok", "applied"), (r.GetProperty("status").GetString(), r.GetProperty("decision").GetString()));
        Assert.Equal("é baz = baz + 1\nCon\n", Rig.TextOf(first));
        Assert.Equal("call baz(baz)\n", Rig.TextOf(second));
    }

    [Fact]
    public async Task Two_documents_of_one_language_share_one_server_process()
    {
        await using var rig = await Rig.CreateAsync(declared: true);
        var first = rig.Add("a.fake", "a\n");
        var second = rig.Add("b.fake", "b\n");

        await rig.RunAsync(first, LspPlugin.StartCommand);
        await rig.RunAsync(second, LspPlugin.StartCommand);
        await rig.RunAsync(first, LspPlugin.StartCommand);

        Assert.Equal(1, Processes(rig));
        Assert.Equal("b\n", await rig.ServerTextAsync(second));
        Assert.Equal("a\n", await rig.ServerTextAsync(first));
    }

    [Fact]
    public async Task A_declared_server_that_is_missing_gives_one_message_and_the_plugin_goes_on()
    {
        await using var rig = await Rig.CreateAsync(declared: true, program: "no-such-language-server-program");
        var doc = rig.Add("lost.fake", "x\n");

        var r = await rig.RunAsync(doc, LspPlugin.StartCommand);

        Assert.Equal("noServer", r.GetProperty("status").GetString());
        var notice = Assert.Single(rig.Ui.Notices, n => n.Message.Contains("no-such-language-server-program"));
        Assert.Contains("No language server for fake was found", notice.Message);
        Assert.Equal("noServer", (await rig.RunAsync(doc, LspPlugin.RenameCommand, 0)).GetProperty("status").GetString()); // still answers, nothing broke
    }

    [Fact]
    public async Task A_declared_server_that_cannot_start_is_reported_without_a_crash()
    {
        var notExecutable = Path.Combine(Path.GetTempPath(), "textida-not-a-server-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(notExecutable, "this is not a program");
        try
        {
            await using var rig = await Rig.CreateAsync(declared: true, program: notExecutable);
            var doc = rig.Add("bad.fake", "x\n");

            var r = await rig.RunAsync(doc, LspPlugin.StartCommand);

            Assert.Equal("startFailed", r.GetProperty("status").GetString());
            Assert.Contains(rig.Ui.Notices, n => n.Message.Contains("could not be started"));
            Assert.Equal("x\n", Rig.TextOf(doc));
        }
        finally { File.Delete(notExecutable); }
    }

    [Fact]
    public async Task A_server_with_no_open_document_is_stopped_after_the_idle_time_and_started_again_by_the_next_command()
    {
        await using var rig = await Rig.CreateAsync(declared: true, idleStopMs: 300);
        var first = rig.Add("idle.fake", "x\n");
        await rig.RunAsync(first, LspPlugin.StartCommand);
        string? Last() { lock (rig.Ui.Statuses) return rig.Ui.Statuses.LastOrDefault()?.Text; }
        await T.Until(() => Last() == "LSP: running");

        await rig.Owner.InvokeAsync(() => { rig.Docs.Withdraw(first); return 0; });
        await T.Until(() => Last() == "LSP: off");

        var second = rig.Add("idle2.fake", "y\n");
        Assert.Equal("ok", (await rig.RunAsync(second, LspPlugin.StartCommand)).GetProperty("status").GetString());
        Assert.Equal(2, Processes(rig));
    }

    // ---- P8.5.1: one server per project ----

    /// <summary>A project folder below the rig folder (a <c>.git</c> folder marks it) with a file in its <c>src</c> folder.</summary>
    static Document AddProject(Rig rig, string project, string file, string content)
    {
        Directory.CreateDirectory(Path.Combine(rig.Folder, project, ".git"));
        Directory.CreateDirectory(Path.Combine(rig.Folder, project, "src"));
        return rig.Add(Path.Combine(project, "src", file), content);
    }

    sealed record FakeServer(int Pid, string Root, string Cwd, List<string> Events);

    static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    /// <summary>What each fake server process wrote about itself: the root it was given, its working folder and the <c>open</c> and <c>close</c> of documents, in order.</summary>
    static List<FakeServer> Servers(Rig rig)
    {
        var result = new List<FakeServer>();
        foreach (var file in Directory.GetFiles(rig.Dump, "process-*"))
        {
            var lines = ReadShared(file).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            result.Add(new FakeServer(int.Parse(Path.GetFileName(file)["process-".Length..]), lines.FirstOrDefault(l => l.StartsWith("root=", StringComparison.Ordinal))?[5..] ?? "",
                lines.FirstOrDefault(l => l.StartsWith("cwd=", StringComparison.Ordinal))?[4..] ?? "",
                [.. lines.Where(l => l.StartsWith("open ", StringComparison.Ordinal) || l.StartsWith("close ", StringComparison.Ordinal))]));
        }
        return result;
    }

    static string RootUri(Rig rig, string project) => PathUri.FromPath(Path.Combine(rig.Folder, project));

    /// <summary>The path with every symbolic link resolved, as a child process reports its working folder (the temp folder of macOS is under the link /var).</summary>
    static string RealPath(string path)
    {
        var current = Path.GetPathRoot(path)!;
        foreach (var part in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (new DirectoryInfo(current).ResolveLinkTarget(true) is { } target) current = target.FullName;
        }
        return current;
    }

    static bool Running(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return false; }
    }

    [Fact]
    public async Task Two_projects_of_one_language_get_two_server_processes_each_with_its_own_root_and_only_its_own_documents()
    {
        await using var rig = await Rig.CreateAsync(declared: true);
        var a = AddProject(rig, "projA", "a.fake", "def foo = 1\n");
        var b = AddProject(rig, "projB", "b.fake", "use foo\n");

        Assert.Equal("ok", (await rig.RunAsync(a, LspPlugin.StartCommand)).GetProperty("status").GetString());
        Assert.Equal("ok", (await rig.RunAsync(b, LspPlugin.StartCommand)).GetProperty("status").GetString());

        await T.Until(() => Servers(rig).Count(s => s.Events.Count == 1) == 2);
        var servers = Servers(rig);
        var forA = Assert.Single(servers, s => s.Root == RootUri(rig, "projA"));
        var forB = Assert.Single(servers, s => s.Root == RootUri(rig, "projB"));
        Assert.Equal([$"open {PathUri.FromPath(a.FilePath!)}"], forA.Events);
        Assert.Equal([$"open {PathUri.FromPath(b.FilePath!)}"], forB.Events);
        Assert.True(PathUri.SamePath(RealPath(Path.Combine(rig.Folder, "projA")), forA.Cwd));
        Assert.True(PathUri.SamePath(RealPath(Path.Combine(rig.Folder, "projB")), forB.Cwd));

        // the server of projB never saw the file of projA, so the definition that lives there is not found
        var none = await rig.RunAsync(b, LspPlugin.DefinitionCommand, Bytes("use f"));
        Assert.Equal("none", none.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Switching_between_two_projects_keeps_each_document_on_its_own_server()
    {
        await using var rig = await Rig.CreateAsync(declared: true);
        var a = AddProject(rig, "projA", "a.fake", "a\n");
        var b = AddProject(rig, "projB", "b.fake", "b\n");

        foreach (var doc in new[] { a, b, a, b, a })
            Assert.Equal("ok", (await rig.RunAsync(doc, LspPlugin.StartCommand)).GetProperty("status").GetString());
        await rig.Edit(b, 1, 1, "B");
        Assert.Equal("bB\n", await rig.ServerTextAsync(b));
        await rig.Edit(a, 0, 0, "A");
        Assert.Equal("Aa\n", await rig.ServerTextAsync(a));

        Assert.Equal(2, Processes(rig));
        Assert.All(Servers(rig), s => Assert.Single(s.Events));
    }

    [Fact]
    public async Task Two_files_of_one_project_in_different_folders_share_one_server_process_rooted_at_the_project()
    {
        await using var rig = await Rig.CreateAsync(declared: true);
        var first = AddProject(rig, "projA", "a.fake", "a\n");
        Directory.CreateDirectory(Path.Combine(rig.Folder, "projA", "lib", "deep"));
        var second = rig.Add(Path.Combine("projA", "lib", "deep", "b.fake"), "b\n");

        await rig.RunAsync(first, LspPlugin.StartCommand);
        await rig.RunAsync(second, LspPlugin.StartCommand);

        await T.Until(() => Servers(rig).Sum(s => s.Events.Count) == 2);
        var server = Assert.Single(Servers(rig));
        Assert.Equal(RootUri(rig, "projA"), server.Root);
    }

    [Fact]
    public async Task A_restart_replaces_only_the_server_of_the_project_it_was_run_for()
    {
        await using var rig = await Rig.CreateAsync(declared: true);
        var a = AddProject(rig, "projA", "a.fake", "a\n");
        var b = AddProject(rig, "projB", "b.fake", "b\n");
        await rig.RunAsync(a, LspPlugin.StartCommand);
        await rig.RunAsync(b, LspPlugin.StartCommand);
        await T.Until(() => Servers(rig).Count(s => s.Events.Count == 1) == 2);
        var before = Servers(rig);
        var oldForA = before.Single(s => s.Root == RootUri(rig, "projA"));
        var forB = before.Single(s => s.Root == RootUri(rig, "projB"));

        Assert.Equal("restarted", (await rig.RunAsync(a, LspPlugin.RestartCommand)).GetProperty("status").GetString());

        await T.Until(() => Servers(rig).Count(s => s.Root == RootUri(rig, "projA") && s.Events.Count == 1) == 2);
        await T.Until(() => !Running(oldForA.Pid));
        Assert.True(Running(forB.Pid));
        var after = Servers(rig);
        Assert.Equal(3, after.Count);
        Assert.Equal(forB.Events, after.Single(s => s.Pid == forB.Pid).Events); // projB neither closed nor reopened anything
        Assert.Equal("ok", (await rig.RunAsync(b, LspPlugin.StartCommand)).GetProperty("status").GetString());
        Assert.Equal(3, Processes(rig));
    }

    [Fact]
    public async Task The_idle_stop_of_one_project_leaves_the_server_of_the_other_running()
    {
        await using var rig = await Rig.CreateAsync(declared: true, idleStopMs: 300);
        var a = AddProject(rig, "projA", "a.fake", "a\n");
        var b = AddProject(rig, "projB", "b.fake", "b\n");
        await rig.RunAsync(a, LspPlugin.StartCommand);
        await rig.RunAsync(b, LspPlugin.StartCommand);
        await T.Until(() => Servers(rig).Count(s => s.Events.Count == 1) == 2);
        var forA = Servers(rig).Single(s => s.Root == RootUri(rig, "projA"));
        var forB = Servers(rig).Single(s => s.Root == RootUri(rig, "projB"));

        await rig.Owner.InvokeAsync(() => { rig.Docs.Withdraw(a); return 0; });

        await T.Until(() => !Running(forA.Pid));
        Assert.True(Running(forB.Pid));
        Assert.Equal("ok", (await rig.RunAsync(b, LspPlugin.StartCommand)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_document_that_comes_with_another_server_configuration_moves_to_a_server_of_that_configuration()
    {
        await using var rig = await Rig.CreateAsync(declared: true, initializationOptions: "{\"mode\":\"one\"}");
        var doc = AddProject(rig, "projA", "m.fake", "m\n");
        await rig.RunAsync(doc, LspPlugin.StartCommand);
        var uri = PathUri.FromPath(doc.FilePath!);
        await T.Until(() => Servers(rig).Any(s => s.Events.SequenceEqual([$"open {uri}"])));

        var other = rig.Launch!.Replace("\"mode\":\"one\"", "\"mode\":\"two\"");
        Assert.Equal("ok", (await rig.RunAsync(doc, LspPlugin.StartCommand, launch: other)).GetProperty("status").GetString());

        await T.Until(() => Servers(rig).Count(s => s.Events.SequenceEqual([$"open {uri}", $"close {uri}"])) == 1 && Servers(rig).Count(s => s.Events.SequenceEqual([$"open {uri}"])) == 1);
        Assert.Equal("{\"mode\":\"two\"}", ReadShared(Path.Combine(rig.Dump, "init-options.json")));
    }

    [Fact]
    public async Task The_server_gets_the_matching_part_of_its_initialization_options_when_it_asks_for_the_configuration()
    {
        await using var rig = await Rig.CreateAsync(["--config", "fake,fake.level,mode,*,missing,mode.deeper"], declared: true,
            initializationOptions: "{\"mode\":\"strict\",\"fake\":{\"level\":3}}");
        var doc = rig.Add("config.fake", "x\n");

        await rig.RunAsync(doc, LspPlugin.StartCommand);

        var answer = Path.Combine(rig.Dump, "config-answer.json");
        await T.Until(() => File.Exists(answer) && ReadShared(answer).Length > 0);
        Assert.Equal("[{\"level\":3},3,\"strict\",{\"mode\":\"strict\",\"fake\":{\"level\":3}},null,null]", ReadShared(answer));
        Assert.Contains("configuration=yes", ReadShared(Assert.Single(Directory.GetFiles(rig.Dump, "process-*")))); // the client announces that it answers workspace/configuration
    }

    [Fact]
    public async Task A_server_without_initialization_options_gets_nothing_for_every_item_it_asks_about()
    {
        await using var rig = await Rig.CreateAsync(["--config", "fake,*"], declared: true);
        var doc = rig.Add("bare.fake", "x\n");

        await rig.RunAsync(doc, LspPlugin.StartCommand);

        var answer = Path.Combine(rig.Dump, "config-answer.json");
        await T.Until(() => File.Exists(answer) && ReadShared(answer).Length > 0);
        Assert.Equal("[null,null]", ReadShared(answer));
    }

    // ---- the pieces without a process ----

    [Fact]
    public void Positions_count_utf16_units_and_the_three_line_breaks()
    {
        var map = new LineMap("a😀b\r\ncd\ref\n");
        Assert.Equal((0, 4), map.PositionOf(4)); // after the emoji (two units) and "a"... "a" 1 + 2 + "b" 1
        Assert.Equal((1, 1), map.PositionOf(7));
        Assert.Equal((2, 0), map.PositionOf(9));
        Assert.Equal(4, map.OffsetOf(0, 99)); // a column past the line stops before its break
        Assert.Equal(7, map.OffsetOf(1, 1));
        Assert.Equal(map.OffsetOf(3, 0), map.OffsetOf(99, 0));
        Assert.Equal("cd", map.LineText(1));
    }

    [Fact]
    public void The_change_between_two_texts_never_cuts_a_surrogate_pair_and_applies_back()
    {
        var random = new Random(3);
        string[] alphabet = ["a", "b", "😀", "😁", "é", "\n"];
        for (int n = 0; n < 2000; n++)
        {
            string Make() => string.Concat(Enumerable.Range(0, random.Next(0, 8)).Select(_ => alphabet[random.Next(alphabet.Length)]));
            string old = Make(), next = Make();
            var change = TextChange.Between(old, next);
            Assert.Equal(old == next, change is null);
            if (change is not { } c) continue;
            Assert.Equal(next, c.ApplyTo(old));
            Assert.False(c.Start > 0 && c.Start < old.Length && char.IsLowSurrogate(old[c.Start]));
            Assert.False(c.OldEnd > 0 && c.OldEnd < old.Length && char.IsLowSurrogate(old[c.OldEnd]));
        }
    }

    [Fact]
    public void The_catalog_names_the_roslyn_launch_line_without_telemetry_and_every_text_has_four_languages()
    {
        // data only: no Roslyn or csharp-ls server is started by any test
        var roslyn = ServerCatalog.BuiltIn.Resolve("csharp", out _)!;
        Assert.StartsWith("Microsoft.CodeAnalysis.LanguageServer", Path.GetFileName(roslyn.Program));
        Assert.Equal(new[] { "--stdio", "--telemetryLevel", "off" }, roslyn.Arguments.ToArray());
        Assert.Null(ServerCatalog.BuiltIn.Resolve("nothing", out var found));
        Assert.False(found);
        Assert.True(Texts.Complete);
    }

    [Fact]
    public async Task The_plugin_without_its_options_exits_with_two()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Plugins.Lsp.dll");
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(T.DotnetHost, dll) { RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false })!;
        process.StandardInput.Close();
        await process.WaitForExitAsync();
        Assert.Equal(2, process.ExitCode);
    }
}

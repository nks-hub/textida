using System.Text;
using System.Text.Json;
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

    sealed class Rig : IAsyncDisposable
    {
        public required PluginHost.PluginHost Host { get; init; }
        public required DocumentApi Docs { get; init; }
        public required string Folder { get; init; }
        public required string Dump { get; init; }
        public required Owner Owner { get; init; }
        public required UiApiTests.RecordingUi Ui { get; init; }
        public required DocumentApiTests.RecordingTools Tools { get; init; }
        public readonly List<Document> Documents = [];

        public static async Task<Rig> CreateAsync(string[]? serverArgs = null, long maxBytes = 1 << 16, string? externalName = null, string? externalContent = null)
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
            File.WriteAllText(catalog, JsonSerializer.Serialize(new[] { new { language = "fake", program = T.DotnetFullPath, arguments } }));

            var ui = new UiApiTests.RecordingUi();
            var tools = new DocumentApiTests.RecordingTools();
            var owner = new Owner();
            PluginHost.PluginHost? host = null;
            PluginEventSender send = (plugin, name, payload) => host!.SendEventAsync(plugin, name, payload);
            var permissions = new PluginPermissions();
            permissions.Grant("lsp", DocumentPermissions.Read, GrantScope.All);
            var docs = new DocumentApi(new DocumentApiOptions
            {
                MaxReadBytes = 1 << 16, MaxFindMatches = 10, MaxSnapshotsPerPlugin = 2, SnapshotLifetime = TimeSpan.FromSeconds(10),
                EventQueueCapacity = 64, MaxRememberedBatches = 16, Permissions = permissions, CallLog = new PluginCallLog(100), Proposals = new NoProposals(),
                Tools = tools, MaxSymbolsPerDocument = 10, MaxSymbolTextLength = 400, MaxSnippetLength = 100, MaxSnippetCarets = 2, TabTimeout = TimeSpan.FromSeconds(1),
                MaxSecretScanBytes = 1 << 20, MaxClipboardChars = 1000, MaxScanBytes = 1 << 22, MaxVirtualDocumentsPerPlugin = 4, MaxVirtualBytes = 1 << 20,
                MaxDiagnosticsPerDocument = 100, MaxCompletionItems = 100, MaxCompletionEdits = 4, CompletionTimeout = TimeSpan.FromSeconds(5),
            }, owner, send, (plugin, method, payload, timeout, ct) => host!.CallAsync(plugin, method, payload, timeout, ct));
            var uiApi = new UiApi(new UiApiOptions { MaxPanelsPerPlugin = 2, MaxItemsPerPanel = 100, MaxTreeDepth = 3, MaxTextLength = 2000, MaxTasksPerPlugin = 2, MaxPickItems = 10 }, ui, send);
            host = new PluginHost.PluginHost(PluginHostTests.Options(), PluginApiRouter.Create(docs.HandleAsync, ("ui/", uiApi.HandleAsync)), docs);
            var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Plugins.Lsp.dll");
            Assert.True(File.Exists(dll), dll);
            host.Register(new PluginSpec("lsp", "LSP", T.DotnetHost,
                [dll, "--max-bytes", maxBytes.ToString(), "--start-timeout-ms", "20000", "--request-timeout-ms", "10000", "--stop-timeout-ms", "3000", "--max-locations", "50",
                 "--stderr-lines", "5", "--max-message-chars", "100", "--max-diagnostics", "50", "--catalog", catalog], ["onCommand:lsp"]));
            await host.ActivateAsync("onCommand:lsp");
            return new Rig { Host = host, Docs = docs, Folder = folder, Dump = dump, Owner = owner, Ui = ui, Tools = tools };
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

        public async Task<JsonElement> RunAsync(Document doc, string command, long? offset = null)
        {
            var json = $"{{\"command\":\"{command}\",\"document\":\"{doc.Id}\",\"revision\":{doc.Revision},\"language\":\"en\",\"lspLanguage\":\"fake\"{(offset is { } o ? $",\"offset\":{o}" : "")}}}";
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

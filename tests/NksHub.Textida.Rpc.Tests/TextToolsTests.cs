using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Plugins.TextTools;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>The text tools: the engines on their own, then a real plugin process against the real host, document API and UI API.</summary>
public partial class TextToolsTests
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
        public ValueTask<ProposalDecision> SubmitAsync(EditProposal proposal, CancellationToken cancellation) => ValueTask.FromResult(new ProposalDecision("p", ProposalOutcome.Rejected, 0, 0));
    }

    sealed class Rig : IAsyncDisposable
    {
        public required PluginHost.PluginHost Host { get; init; }
        public required DocumentApi Docs { get; init; }
        public required UiApi Ui { get; init; }
        public required Document Doc { get; init; }
        public required UiApiTests.RecordingUi Recorded { get; init; }
        public required DocumentApiTests.RecordingTools Tools { get; init; }

        /// <param name="holdReads">When given, each read of bytes by the plugin waits for the task it returns, so a test can cancel a run that cannot have finished yet however slow the machine is.</param>
        public static async Task<Rig> CreateAsync(byte[] content, TextCodec codec, IReadOnlyList<(long Anchor, long Caret)>? selections = null, int chunk = 1 << 12, Func<Task>? holdReads = null) =>
            await CreateAsync(Document.FromBytes(content, PagedBackendFactory.Instance, new DocumentOptions { Format = codec }), selections, chunk, holdReads: holdReads);

        /// <summary>The rig over a document the test opened itself (a file); <paramref name="maxRead"/> is what one read of the document API may return, the chunk must not exceed it.</summary>
        public static async Task<Rig> CreateAsync(Document doc, IReadOnlyList<(long Anchor, long Caret)>? selections = null, int chunk = 1 << 12, int maxRead = 1 << 16, NksHub.Textida.Rpc.RpcLimits? limits = null, TimeSpan? callTimeout = null, Func<Task>? holdReads = null)
        {
            var recorded = new UiApiTests.RecordingUi();
            PluginHost.PluginHost? host = null;
            PluginEventSender send = (plugin, name, payload) => host!.SendEventAsync(plugin, name, payload);
            var permissions = new PluginPermissions();
            permissions.Grant("tools", DocumentPermissions.Read, GrantScope.All);
            permissions.Grant("tools", DocumentPermissions.ClipboardWrite, GrantScope.All);
            var tools = new DocumentApiTests.RecordingTools();
            var docs = new DocumentApi(new DocumentApiOptions
            {
                MaxReadBytes = maxRead, MaxFindMatches = 10, MaxSnapshotsPerPlugin = 2, SnapshotLifetime = TimeSpan.FromSeconds(10),
                EventQueueCapacity = 64, MaxRememberedBatches = 16, Permissions = permissions, CallLog = new PluginCallLog(100), Proposals = new NoProposals(),
                Tools = tools, MaxSymbolsPerDocument = 10, MaxSymbolTextLength = 50, MaxSnippetLength = 100, MaxSnippetCarets = 2, TabTimeout = TimeSpan.FromSeconds(1), MaxSecretScanBytes = 1 << 20, MaxClipboardChars = 1000, MaxScanBytes = 1 << 22, MaxVirtualDocumentsPerPlugin = 4, MaxVirtualBytes = 1 << 20, MaxDiagnosticsPerDocument = 10, MaxCompletionItems = 10, MaxCompletionEdits = 4, CompletionTimeout = TimeSpan.FromSeconds(2),
            }, new Owner(), send, (plugin, method, payload, timeout, ct) => host!.CallAsync(plugin, method, payload, timeout, ct));
            var ui = new UiApi(new UiApiOptions { MaxPanelsPerPlugin = 2, MaxItemsPerPanel = 10, MaxTreeDepth = 3, MaxTasksPerPlugin = 2, MaxTextLength = 4000, MaxPickItems = 100, TaskCancelGrace = TimeSpan.FromSeconds(30) }, recorded, send);
            PluginApiCallback documents = docs.HandleAsync;
            if (holdReads is not null)
            {
                var direct = documents;
                documents = async (plugin, method, parameters, ct) =>
                {
                    if (method == DocumentMethods.ReadBytes) await holdReads().WaitAsync(ct);
                    return await direct(plugin, method, parameters, ct);
                };
            }
            host = new PluginHost.PluginHost(PluginHostTests.Options(limits is null && callTimeout is null ? null : o => o with { Limits = limits ?? o.Limits, DefaultCallTimeout = callTimeout ?? o.DefaultCallTimeout }), PluginApiRouter.Create(documents, ("ui/", ui.HandleAsync)), docs);
            var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Plugins.TextTools.dll");
            Assert.True(File.Exists(dll), dll);
            host.Register(new PluginSpec("tools", "Tools", T.DotnetHost, [dll, "--chunk-bytes", chunk.ToString(), "--progress-ms", "0"], ["onCommand:tools"]));
            await host.ActivateAsync("onCommand:tools");
            Func<SelectionSet?>? selector = selections is null ? null : () => SelectionSet.Create([.. selections.Select(s => new TextSelection(s.Anchor, s.Caret))], 0);
            docs.Expose(doc, selector);
            return new Rig { Host = host, Docs = docs, Ui = ui, Doc = doc, Recorded = recorded, Tools = tools };
        }

        public async Task<string> RunAsync(string command, string language = "en", string extra = "", long? revision = null)
        {
            var json = $"{{\"command\":\"{command}\",\"document\":\"{Doc.Id}\",\"revision\":{revision ?? Doc.Revision},\"language\":\"{language}\"{extra}}}";
            return (await Host.CallAsync("tools", "command/execute", Encoding.UTF8.GetBytes(json))).GetProperty("status").GetString()!;
        }

        /// <summary>The whole answer of a command, for the ones that hand more than a status back to the editor.</summary>
        public async Task<JsonElement> RunRawAsync(string command, string extra = "")
        {
            var json = $"{{\"command\":\"{command}\",\"document\":\"{Doc.Id}\",\"revision\":{Doc.Revision},\"language\":\"en\"{extra}}}";
            return (await Host.CallAsync("tools", "command/execute", Encoding.UTF8.GetBytes(json))).Clone();
        }

        public string LastNotice => Recorded.Notices[^1].Message;

        public byte[] Bytes() => Doc.GetSnapshot().ReadBytes(new ByteRange(0, Doc.ByteLength));

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await Docs.DisposeAsync();
            Doc.Dispose();
        }
    }

    static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    static byte[] RandomBytes(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    static ChunkReader ReaderOf(byte[] data) => (offset, buffer, _) =>
    {
        int n = (int)Math.Min(buffer.Length, data.Length - offset);
        data.AsMemory((int)offset, n).CopyTo(buffer);
        return ValueTask.FromResult(n);
    };

    // ---- statistics ----

    public static IEnumerable<object[]> Codecs()
    {
        foreach (var chunk in new[] { 4, 5, 7, 64, 1 << 20 })
            foreach (var codec in new[] { TextCodec.Utf8, TextCodec.Utf16LE, TextCodec.Utf16BEBom, TextCodec.Utf32LE, TextCodec.Cp1250 })
                yield return new object[] { codec.Name, chunk };
    }

    [Theory]
    [MemberData(nameof(Codecs))]
    public async Task Statistics_do_not_depend_on_where_the_chunks_are_cut(string codecName, int chunk)
    {
        var codec = TextCodec.ByName(codecName)!;
        bool emoji = codec.Kind != CodecKind.SingleByte; // one character, two UTF-16 units
        var text = "První řádek, ěščř " + (emoji ? "😀 " : "") + "x\r\nDruhý\n\rtřetí\r\r\nčtvrtý bez konce";
        byte[] bytes = [.. codec.Bom, .. codec.Encode(text)];
        int bom = codec.Bom.Length;
        var statistics = new TextStatistics(codec);
        var outcome = await ByteScan.RunAsync(bom, bytes.Length, chunk, ReaderOf(bytes), statistics.Add, null, CancellationToken.None);
        var result = statistics.Finish(bytes.Length);

        Assert.True(outcome.Completed);
        Assert.Equal(bytes.Length - bom, outcome.Done);
        Assert.Equal(text.Length - (emoji ? 1 : 0), result.Characters);
        Assert.Equal(9, result.Words);   // the emoji is no word
        Assert.Equal(6, result.Lines);   // CRLF, LF, CR, CR, CRLF are five terminators
        Assert.Equal(emoji ? 21 : 19, result.LongestLine);
    }

    [Fact]
    public async Task Statistics_of_an_empty_text_is_one_empty_line()
    {
        var statistics = new TextStatistics(TextCodec.Utf8);
        await ByteScan.RunAsync(0, 0, 16, ReaderOf([]), statistics.Add, null, CancellationToken.None);
        Assert.Equal(new TextStatisticsResult(0, 0, 0, 1, 0), statistics.Finish(0));
    }

    [Fact]
    public async Task Frequency_lists_count_lower_cased_words_across_chunks_and_stop_at_the_distinct_word_limit()
    {
        var bytes = Utf8("Aa aa b\nB aa c");
        var statistics = new TextStatistics(TextCodec.Utf8, maxDistinctWords: 2, maxWordLength: 64);
        await ByteScan.RunAsync(0, bytes.Length, 3, ReaderOf(bytes), statistics.Add, null, CancellationToken.None);
        statistics.Finish(bytes.Length);

        Assert.Equal([("aa", 3L), ("b", 2L)], statistics.TopWords(5));
        Assert.True(statistics.WordTableFull);
        Assert.Equal(("a", 5L), statistics.TopCharacters(1)[0]);
    }

    [Fact]
    public async Task Crc32_matches_the_published_check_value()
    {
        var bytes = Utf8("123456789");
        var (hex, _) = await BlockHash.ComputeAsync(BlockHash.Crc32, 0, bytes.Length, 4, ReaderOf(bytes), null, CancellationToken.None);
        Assert.Equal("cbf43926", hex);
    }

    [Theory]
    [InlineData("XIV", 14)]
    [InlineData("mcmxciv", 1994)]
    [InlineData("MMMCMXCIX", 3999)]
    public void Roman_numerals_are_read_and_written(string text, int number)
    {
        Assert.True(BaseConverter.TryParseRoman(text, out var value));
        Assert.Equal(number, (int)value);
        Assert.Equal(text.ToUpperInvariant(), BaseConverter.ToRoman(value));
        Assert.False(BaseConverter.TryParseRoman("IIII", out _));
    }

    [Fact]
    public async Task Statistics_through_the_plugin_are_shown_in_the_language_of_the_user_and_leave_the_document_alone()
    {
        var content = Utf8("ab cd\r\nef\n");
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8);
        var revision = rig.Doc.Revision;

        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Statistics, "en"));
        Assert.Equal("Bytes: 10, characters: 10, words: 3, lines: 3, longest line: 5 characters.", rig.LastNotice);
        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Statistics, "cs"));
        Assert.Equal("Bajty: 10, znaky: 10, slova: 3, řádky: 3, nejdelší řádek: 5 znaků.", rig.LastNotice);

        Assert.Equal(content, rig.Bytes());
        Assert.Equal(revision, rig.Doc.Revision);
        Assert.True(rig.Recorded.Started[0].Cancellable);
        Assert.Equal(2, rig.Recorded.Ended.Count);
    }

    [Fact]
    public async Task A_cancelled_statistic_is_reported_as_incomplete_with_how_far_it_got()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var rig = await Rig.CreateAsync(Utf8(string.Concat(Enumerable.Repeat("word and more\n", 1 << 18))), TextCodec.Utf8, chunk: 1 << 10, holdReads: () => release.Task);
        var run = rig.RunAsync(TextToolsPlugin.Statistics);
        await CancelTaskAsync(rig);
        release.SetResult();

        Assert.Equal("incomplete", await run);
        Assert.StartsWith("Incomplete statistics, ", rig.LastNotice);
        Assert.Equal(NoticeLevel.Warning, rig.Recorded.Notices[^1].Level);
        Assert.Single(rig.Recorded.Ended);
    }

    [Fact]
    public async Task Each_cancelled_statistic_gives_back_its_snapshot_and_its_task()
    {
        // the reads stay held until the test has cancelled the run; each round gets a gate of its own
        var gates = new TaskCompletionSource[3];
        for (int i = 0; i < gates.Length; i++) gates[i] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int round = 0;
        await using var rig = await Rig.CreateAsync(Utf8(string.Concat(Enumerable.Repeat("word and more\n", 1 << 18))), TextCodec.Utf8, chunk: 1 << 10, holdReads: () => gates[Volatile.Read(ref round)].Task);
        // the rig allows two snapshots and two tasks, so the third run starts only if the cancelled ones gave theirs back
        for (int i = 0; i < 3; i++)
        {
            Volatile.Write(ref round, i);
            var run = rig.RunAsync(TextToolsPlugin.Statistics);
            await CancelTaskAsync(rig, taskIndex: i);
            gates[i].SetResult();
            Assert.Equal("incomplete", await run);
        }
        Assert.Equal(3, rig.Recorded.Ended.Count);
    }

    static async Task CancelTaskAsync(Rig rig, int taskIndex = 0)
    {
        for (int i = 0; i < 2000 && rig.Recorded.Started.Count <= taskIndex; i++) await Task.Delay(5);
        string id;
        lock (rig.Recorded.Started) id = rig.Recorded.Started[taskIndex].Id;
        Assert.True(await rig.Ui.CancelTaskAsync("tools", id));
    }

    // ---- hashes ----

    static string ExternalSha256(string path)
    {
        var (file, args) = OperatingSystem.IsWindows() ? ("certutil", $"-hashfile \"{path}\" SHA256")
            : OperatingSystem.IsMacOS() ? ("shasum", $"-a 256 \"{path}\"") : ("sha256sum", $"\"{path}\"");
        using var process = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true, UseShellExecute = false })!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        // certutil: a header line, the hash in groups or not, a footer line; the others: the hash first
        return System.Text.RegularExpressions.Regex.Match(output.Replace(" ", ""), "[0-9a-fA-F]{64}").Value.ToLowerInvariant();
    }

    [Fact]
    public async Task Sha256_of_a_file_is_the_one_the_external_tool_gives_and_the_run_can_be_cancelled()
    {
        var content = RandomBytes(3 * (1 << 20) + 123, 7);
        var path = Path.Combine(Path.GetTempPath(), "textida-hash-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllBytesAsync(path, content);
        try
        {
            await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, chunk: 2048);
            Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Hash, extra: ",\"algorithm\":\"sha256\""));

            var expected = ExternalSha256(path);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), expected);
            Assert.Equal($"SHA-256 ({content.Length.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} bytes): {expected}", rig.LastNotice);

            var again = rig.RunAsync(TextToolsPlugin.Hash, extra: ",\"algorithm\":\"sha256\"");
            await CancelTaskAsync(rig, taskIndex: 1);
            Assert.Equal("cancelled", await again);
            Assert.StartsWith("The hash was cancelled after ", rig.LastNotice);
            Assert.DoesNotContain(expected, rig.LastNotice);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("md5")]
    [InlineData("sha1")]
    [InlineData("sha384")]
    [InlineData("sha512")]
    public async Task A_selection_is_hashed_over_its_raw_bytes_and_the_algorithm_can_be_picked(string algorithm)
    {
        var content = RandomBytes(100_000, 3);
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, [(70_000, 1_000)], chunk: 4096); // selected backwards
        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Hash, extra: $",\"algorithm\":\"{algorithm}\""));
        var part = content.AsSpan(1_000, 69_000);
        var expected = algorithm switch { "md5" => MD5.HashData(part), "sha1" => SHA1.HashData(part), "sha384" => SHA384.HashData(part), _ => SHA512.HashData(part) };
        Assert.EndsWith(Convert.ToHexStringLower(expected), rig.LastNotice);
    }

    [Fact]
    public async Task Without_an_algorithm_the_user_picks_one_and_dismissing_the_pick_stops_quietly()
    {
        await using var rig = await Rig.CreateAsync(Utf8("abc"), TextCodec.Utf8);
        Assert.Equal("cancelled", await rig.RunAsync(TextToolsPlugin.Hash));
        Assert.Empty(rig.Recorded.Notices);
        rig.Recorded.Pick = _ => "sha1";
        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Hash));
        Assert.EndsWith("a9993e364706816aba3e25717850c26c9cd0d89d", rig.LastNotice);
        Assert.Equal(Checksums.All.Count, rig.Recorded.Picks[0].Items.Count); // the list of the checksum dialog of the hex view
        Assert.Contains(rig.Recorded.Picks[0].Items, i => i.Label == "CRC-32");
        Assert.Contains(rig.Recorded.Picks[0].Items, i => i.Label == "XXH3-64");
    }

    [Theory]
    [InlineData("xxh3", "abc", "78af5f94892f3950")]
    [InlineData("crc-32c", "123456789", "e3069283")]
    [InlineData("crc32", "123456789", "cbf43926")]
    [InlineData("adler32", "123456789", "091e01de")]
    [InlineData("crc-64/xz", "123456789", "995dc9bbdf1939fa")]
    public async Task The_algorithms_of_the_hex_dialog_give_their_published_values_in_the_text_command(string id, string text, string expected)
    {
        await using var rig = await Rig.CreateAsync(Utf8(text), TextCodec.Utf8);
        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Hash, extra: $",\"algorithm\":\"{id}\""));
        Assert.EndsWith(": " + expected, rig.LastNotice);
    }

    [Fact]
    public async Task Xxh3_does_not_depend_on_where_the_chunks_are_cut()
    {
        var content = RandomBytes(100_000, 5);
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, chunk: 4097);
        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Hash, extra: ",\"algorithm\":\"xxh3\""));
        Assert.EndsWith(": " + Convert.ToHexStringLower(System.IO.Hashing.XxHash3.Hash(content)), rig.LastNotice);
    }

    [Fact]
    public async Task A_pasted_hash_is_compared_whatever_separators_it_has_and_an_unknown_length_asks_for_the_algorithm()
    {
        await using var rig = await Rig.CreateAsync(Utf8("123456789"), TextCodec.Utf8);
        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Hash, extra: ",\"expected\":\"CB:F4:39:26\"")); // 8 digits: CRC-32
        Assert.EndsWith("Matches the pasted hash.", rig.LastNotice);
        Assert.Equal("mismatch", await rig.RunAsync(TextToolsPlugin.Hash, extra: ",\"expected\":\"0xcbf43927\""));
        Assert.EndsWith("Does NOT match the pasted hash.", rig.LastNotice);
        Assert.Empty(rig.Recorded.Picks);

        rig.Recorded.Pick = _ => "xxh3"; // 16 digits could be several algorithms
        string xxh3 = Convert.ToHexStringLower(System.IO.Hashing.XxHash3.Hash(Utf8("123456789")));
        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Hash, extra: $",\"expected\":\"{xxh3.ToUpperInvariant()}\""));
        Assert.EndsWith("Matches the pasted hash.", rig.LastNotice);
        Assert.Single(rig.Recorded.Picks);
    }

    [Fact]
    public async Task Several_selections_are_hashed_one_block_each()
    {
        await using var rig = await Rig.CreateAsync(Utf8("abcdef"), TextCodec.Utf8, [(0, 2), (3, 5)]);
        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Hash, extra: ",\"algorithm\":\"sha1\""));
        string Sha1(string text) => Convert.ToHexStringLower(SHA1.HashData(Utf8(text)));
        Assert.Equal($"Part 1: SHA-1 (2 bytes): {Sha1("ab")}\nPart 2: SHA-1 (2 bytes): {Sha1("de")}", rig.LastNotice);
        Assert.Equal(NoticeLevel.Info, rig.Recorded.Notices[^1].Level);
    }

    [Fact]
    public async Task With_the_dialog_flag_the_hash_is_handed_back_to_the_editor_part_by_part_and_nothing_is_shown_copied_or_compared()
    {
        await using var rig = await Rig.CreateAsync(Utf8("abcdef"), TextCodec.Utf8, [(0, 2), (3, 5)]);
        string Sha1(string text) => Convert.ToHexStringLower(SHA1.HashData(Utf8(text)));

        // copy and expected belong to the dialog of the editor then: they change nothing here
        var answer = await rig.RunRawAsync(TextToolsPlugin.Hash, ",\"algorithm\":\"sha1\",\"dialog\":true,\"copy\":true,\"expected\":\"00\"");

        Assert.Equal("completed", answer.GetProperty("status").GetString());
        Assert.Equal("SHA-1", answer.GetProperty("algorithm").GetString());
        Assert.True(answer.GetProperty("selection").GetBoolean());
        Assert.Equal([(0L, 2L, Sha1("ab")), (3L, 5L, Sha1("de"))],
            answer.GetProperty("blocks").EnumerateArray().Select(b => (b.GetProperty("start").GetInt64(), b.GetProperty("end").GetInt64(), b.GetProperty("hash").GetString()!)));
        Assert.Empty(rig.Recorded.Notices);

        await using var whole = await Rig.CreateAsync(Utf8("abcdef"), TextCodec.Utf8);
        var all = await whole.RunRawAsync(TextToolsPlugin.Hash, ",\"algorithm\":\"sha1\",\"dialog\":true");
        Assert.False(all.GetProperty("selection").GetBoolean());
        var block = Assert.Single(all.GetProperty("blocks").EnumerateArray());
        Assert.Equal((0L, 6L, Sha1("abcdef")), (block.GetProperty("start").GetInt64(), block.GetProperty("end").GetInt64(), block.GetProperty("hash").GetString()!));
        Assert.Empty(whole.Recorded.Notices);
    }

    [Fact]
    public async Task Without_the_dialog_flag_the_hash_is_still_a_notice_and_the_answer_carries_the_same_data()
    {
        await using var rig = await Rig.CreateAsync(Utf8("abc"), TextCodec.Utf8);

        var answer = await rig.RunRawAsync(TextToolsPlugin.Hash, ",\"algorithm\":\"sha1\"");

        Assert.EndsWith("a9993e364706816aba3e25717850c26c9cd0d89d", rig.LastNotice);
        Assert.Equal("a9993e364706816aba3e25717850c26c9cd0d89d", Assert.Single(answer.GetProperty("blocks").EnumerateArray()).GetProperty("hash").GetString());
    }

    [Fact]
    public async Task A_command_started_on_an_old_revision_is_rejected()
    {
        await using var rig = await Rig.CreateAsync(Utf8("abc"), TextCodec.Utf8);
        var old = rig.Doc.Revision;
        rig.Doc.Apply([new ByteEdit(new ByteRange(3, 3), Utf8("d"))], new EditOptions { BaseRevision = old, Source = "test" });
        Assert.Equal("stale", await rig.RunAsync(TextToolsPlugin.Statistics, revision: old));
        Assert.Equal("stale", await rig.RunAsync(TextToolsPlugin.Hash, extra: ",\"algorithm\":\"sha1\"", revision: old));
        Assert.All(rig.Recorded.Notices, n => Assert.Equal(NoticeLevel.Warning, n.Level));
        Assert.Empty(rig.Recorded.Started);
    }

    // ---- block calculation ----

    [Theory]
    [InlineData("cs", "1 234,50", 1234.5)]
    [InlineData("cs", "1 234,50", 1234.5)]
    [InlineData("sk", "-1 234 567,125", -1234567.125)]
    [InlineData("de", "1.234,5", 1234.5)]
    [InlineData("de", "1234,5", 1234.5)]
    [InlineData("en", "1,234.50", 1234.5)]
    [InlineData("en", "-0.5", -0.5)]
    [InlineData("en", "+12", 12)]
    [InlineData("cs", ",5", 0.5)]
    public void A_number_is_read_the_way_its_language_writes_it(string language, string text, double expected)
    {
        Assert.True(NumberFormat.For(language).TryParse(text, allowSpace: true, out var value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("en", "1,23")]
    [InlineData("en", "1.2.3")]
    [InlineData("de", "1.23,4")]
    [InlineData("cs", "1 23,4")]
    [InlineData("en", "12a")]
    [InlineData("en", "-")]
    [InlineData("en", "1.")]
    [InlineData("en", "")]
    [InlineData("de", "1,234.5")]
    public void Text_that_is_not_a_number_in_the_language_is_refused(string language, string text) =>
        Assert.False(NumberFormat.For(language).TryParse(text, allowSpace: true, out _));

    [Theory]
    [InlineData("cs", "1 234,5\n10,25\n-4 000")]
    [InlineData("sk", "1 234,5\n10,25\n-4 000")]
    [InlineData("de", "1.234,5\n10,25\n-4.000")]
    [InlineData("en", "1,234.5\n10.25\n-4,000")]
    public void A_column_adds_up_in_every_format(string language, string column)
    {
        var result = BlockCalculator.Calculate([column], NumberFormat.For(language));
        Assert.Equal((3, 0, -2755.25m, -4000m, 1234.5m), (result.Count, result.Ignored, result.Sum, result.Min, result.Max));
        Assert.Equal(-2755.25m / 3, result.Average);
    }

    [Fact]
    public void Pieces_that_are_not_numbers_are_skipped_and_counted_and_a_line_with_several_numbers_is_split()
    {
        var result = BlockCalculator.Calculate(["a 1,5 2,5;x\n\n  3  "], NumberFormat.For("cs"));
        Assert.Equal((3, 2, 7m), (result.Count, result.Ignored, result.Sum));
    }

    [Theory]
    [InlineData("cs", "1 234,50\n10,25\n-4 000", "de")]
    [InlineData("sk", "1 234,50\n10,25\n-4 000", "en")]
    [InlineData("de", "1.234,50\n10,25\n-4.000", "cs")]
    [InlineData("en", "1,234.50\n10.25\n-4,000", "de")]
    public void The_format_of_a_column_is_recognised_whatever_the_language_of_the_interface(string dataLanguage, string column, string interfaceLanguage)
    {
        var (result, format) = BlockCalculator.CalculateAuto([column], interfaceLanguage);
        Assert.Equal((3, 0, -2755.25m), (result.Count, result.Ignored, result.Sum));
        Assert.Equal(NumberFormat.For(dataLanguage).Format(1234.5m), format.Format(1234.5m));
    }

    [Fact]
    public void When_two_formats_read_a_column_equally_the_one_of_the_interface_language_wins()
    {
        Assert.Equal(1.234m, BlockCalculator.CalculateAuto(["1,234"], "cs").Result.Sum);
        Assert.Equal(1234m, BlockCalculator.CalculateAuto(["1,234"], "en").Result.Sum);
        Assert.Equal(1234m, BlockCalculator.CalculateAuto(["1.234"], "de").Result.Sum);
        Assert.Equal(1.234m, BlockCalculator.CalculateAuto(["1.234"], "en").Result.Sum);
    }

    [Fact]
    public void A_sum_beyond_the_range_of_a_decimal_is_an_overflow()
    {
        var big = decimal.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<OverflowException>(() => BlockCalculator.Calculate([big + "\n" + big], NumberFormat.For("en")));
    }

    [Fact]
    public async Task The_sum_of_a_column_is_shown_in_the_format_of_the_ui_language_whatever_the_format_of_the_data_and_the_document_is_untouched()
    {
        var content = Utf8("a 1 234,50\nb 10,25\nc -4 000,5\n");
        // the numbers are in the middle of each line: select "1 234,50", "10,25" and "-4 000,5" as three selections of a block selection
        string text = Encoding.UTF8.GetString(content);
        var ranges = new List<(long, long)>();
        foreach (var n in new[] { "1 234,50", "10,25", "-4 000,5" })
        {
            int at = text.IndexOf(n, StringComparison.Ordinal);
            ranges.Add((Encoding.UTF8.GetByteCount(text[..at]), Encoding.UTF8.GetByteCount(text[..(at + n.Length)])));
        }
        await using var rig = await Rig.CreateAsync(content, TextCodec.Utf8, ranges);
        var revision = rig.Doc.Revision;
        var format = ""; // the format of the numbers (Czech here) is recognised, whatever the language of the interface

        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Calculate, "cs", format));
        var average = -2755.75m / 3;
        Assert.Equal($"Počet: 3, součet: -2 755,75, nejmenší: -4 000,5, největší: 1 234,5, průměr: {NumberFormat.For("cs").Format(average)}.", rig.LastNotice);
        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Calculate, "en", format));
        Assert.Equal($"Count: 3, sum: -2,755.75, smallest: -4,000.5, largest: 1,234.5, average: {NumberFormat.For("en").Format(average)}.", rig.LastNotice);
        Assert.StartsWith("-918.58333", NumberFormat.For("en").Format(average));
        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Calculate, "de", format));
        Assert.StartsWith("Anzahl: 3, Summe: -2.755,75, kleinster: -4.000,5, größter: 1.234,5", rig.LastNotice);

        Assert.Equal(content, rig.Bytes());
        Assert.Equal(revision, rig.Doc.Revision);
    }

    [Fact]
    public async Task Calculating_without_a_selection_or_without_numbers_says_so()
    {
        await using var none = await Rig.CreateAsync(Utf8("1 2 3"), TextCodec.Utf8);
        Assert.Equal("failed", await none.RunAsync(TextToolsPlugin.Calculate));
        Assert.Equal("Select the numbers to add up.", none.LastNotice);

        await using var words = await Rig.CreateAsync(Utf8("abc def"), TextCodec.Utf8, [(0, 7)]);
        Assert.Equal("failed", await words.RunAsync(TextToolsPlugin.Calculate));
        Assert.Equal("There are no numbers in the selected text.", words.LastNotice);
    }

    // ---- number bases ----

    [Theory]
    [InlineData("0", "0", "0x0", "0b0")]
    [InlineData("255", "255", "0xFF", "0b11111111")]
    [InlineData("-1", "-1", "-0x1", "-0b1")]
    [InlineData("-9223372036854775808", "-9223372036854775808", "-0x8000000000000000", "-0b1000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("9223372036854775807", "9223372036854775807", "0x7FFFFFFFFFFFFFFF", "0b111111111111111111111111111111111111111111111111111111111111111")]
    [InlineData("18446744073709551615", "18446744073709551615", "0xFFFFFFFFFFFFFFFF", "0b1111111111111111111111111111111111111111111111111111111111111111")]
    [InlineData("0xff", "255", "0xFF", "0b11111111")]
    [InlineData("0b101", "5", "0x5", "0b101")]
    [InlineData("0o777", "511", "0x1FF", "0b111111111")]
    [InlineData("-0x10", "-16", "-0x10", "-0b10000")]
    public async Task Numbers_convert_at_the_extremes_of_64_bits(string input, string dec, string hex, string bin)
    {
        Assert.True(BaseConverter.TryParse(input, out var value));
        Assert.Equal(dec, value.ToString());
        Assert.Equal(hex.Replace("0x", ""), (value.Sign < 0 ? "-" : "") + BaseConverter.ToRadix(BigInteger.Abs(value), 16));
        await using var rig = await Rig.CreateAsync(Utf8(""), TextCodec.Utf8);
        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Bases, extra: $",\"text\":\"{input}\""));
        Assert.StartsWith($"Decimal: {dec}, hexadecimal: {hex}, octal: ", rig.LastNotice);
        // Roman numeral and date follow the binary part when the number fits them
        Assert.Equal(bin, rig.LastNotice.Split(", binary: ")[1].Split(' ')[0]);
    }

    [Fact]
    public async Task A_small_number_also_gives_its_Roman_numeral_and_the_date_it_is_as_milliseconds()
    {
        await using var rig = await Rig.CreateAsync(Utf8(""), TextCodec.Utf8);
        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Bases, extra: ",\"text\":\"1994\""));
        Assert.EndsWith(" Roman: MCMXCIV. As milliseconds since 1 Jan 1970: 1970-01-01 00:00:01.994 UTC.", rig.LastNotice);
        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Bases, extra: ",\"text\":\"xiv\""));
        Assert.StartsWith("Decimal: 14, ", rig.LastNotice);
    }

    [Fact]
    public void Every_radix_from_2_to_16_round_trips_the_extremes()
    {
        foreach (var v in new BigInteger[] { 0, 1, 15, 16, long.MinValue, long.MaxValue, ulong.MaxValue })
            for (int radix = 2; radix <= 16; radix++)
            {
                var text = BaseConverter.ToRadix(v, radix);
                BigInteger back = 0;
                foreach (var c in text.TrimStart('-')) back = back * radix + Convert.ToInt32(c.ToString(), 16);
                Assert.Equal(v, text.StartsWith('-') ? -back : back);
            }
    }

    [Theory]
    [InlineData("18446744073709551616")]
    [InlineData("-9223372036854775809")]
    [InlineData("0x1FFFFFFFFFFFFFFFF")]
    [InlineData("0b102")]
    [InlineData("12x")]
    [InlineData("")]
    [InlineData("0x")]
    [InlineData("1.5")]
    public void Values_outside_64_bits_and_text_that_is_not_an_integer_are_refused(string input) =>
        Assert.False(BaseConverter.TryParse(input, out _));

    // ---- expressions ----

    [Theory]
    [InlineData("1+2*3", 7)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("10/4", 2.5)]
    [InlineData("2^3^2", 512)]
    [InlineData("-2^2", -4)]
    [InlineData("2^-1", 0.5)]
    [InlineData("17 MOD 5", 2)]
    [InlineData("17.6 mod 5.4", 3)]
    [InlineData("-17 MOD 5", -2)]
    [InlineData("10 - 2 - 3", 5)]
    [InlineData("abs(-3.5)+sqr(3)+sqrt(16)", 16.5)]
    [InlineData("round(2.5)+trunc(-2.7)", 0)]
    [InlineData("ln(exp(2))", 2)]
    [InlineData("sin(0)+cos(0)+atan(0)", 1)]
    [InlineData("1.5e2", 150)]
    [InlineData(".5+.25", 0.75)]
    public void Expressions_are_evaluated(string text, double expected) =>
        Assert.Equal(expected, ExpressionEvaluator.Evaluate(text), 12);

    [Theory]
    [InlineData("System(\"calc\")", ExpressionError.UnknownName)]
    [InlineData("Process.Start(1)", ExpressionError.UnknownName)]
    [InlineData("exec(1)", ExpressionError.UnknownName)]
    [InlineData("calc", ExpressionError.UnknownName)]
    [InlineData("PI", ExpressionError.UnknownName)]
    [InlineData("sin 1", ExpressionError.UnknownName)]
    [InlineData("1/0", ExpressionError.DivisionByZero)]
    [InlineData("5 MOD 0", ExpressionError.DivisionByZero)]
    [InlineData("LN(0)", ExpressionError.Domain)]
    [InlineData("SQRT(-1)", ExpressionError.Domain)]
    [InlineData("(-2)^2", ExpressionError.Domain)]
    [InlineData("0^2", ExpressionError.Domain)]
    [InlineData("ROUND(1e10)", ExpressionError.Range)]
    [InlineData("SIN(1e19)", ExpressionError.Range)]
    [InlineData("EXP(1000)", ExpressionError.Range)]
    [InlineData("1e999", ExpressionError.Range)]
    [InlineData("1+", ExpressionError.Syntax)]
    [InlineData("(1", ExpressionError.Syntax)]
    [InlineData("1 2", ExpressionError.Syntax)]
    [InlineData("", ExpressionError.Syntax)]
    [InlineData("1;2", ExpressionError.Syntax)]
    [InlineData("2 $ 3", ExpressionError.Syntax)]
    public void Expressions_that_call_anything_else_or_leave_the_ranges_are_refused(string text, ExpressionError error) =>
        Assert.Equal(error, Assert.Throws<ExpressionException>(() => ExpressionEvaluator.Evaluate(text)).Error);

    [Fact]
    public void Deeply_nested_parentheses_end_in_an_error_not_in_a_crash()
    {
        var text = new string('(', 200_000) + "1" + new string(')', 200_000);
        Assert.Throws<InsufficientExecutionStackException>(() => ExpressionEvaluator.Evaluate(text));
    }

    [Fact]
    public async Task The_expression_dialog_starts_with_the_selection_shows_the_result_in_the_ui_language_and_a_system_call_is_refused()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x = 1000*1.5 + 2"), TextCodec.Utf8, [(4, 16)]);
        string? offered = null;
        rig.Recorded.Input = request => { offered = request.Value; return request.Value; };

        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Expression, "cs"));
        Assert.Equal("1000*1.5 + 2", offered);
        Assert.Equal("1000*1.5 + 2 = 1 502", rig.LastNotice);
        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Expression, "de", ",\"text\":\"1000*1.5/3\""));
        Assert.Equal("1000*1.5/3 = 500", rig.LastNotice);
        Assert.Equal("completed", await rig.RunAsync(TextToolsPlugin.Expression, "de", ",\"text\":\"10000/3\""));
        Assert.StartsWith("10000/3 = 3.333,33333", rig.LastNotice);

        var before = rig.Recorded.Notices.Count;
        Assert.Equal("failed", await rig.RunAsync(TextToolsPlugin.Expression, "en", ",\"text\":\"System(\\\"calc\\\")\""));
        Assert.StartsWith("Unknown name 'System'.", rig.LastNotice);
        Assert.Equal(before + 1, rig.Recorded.Notices.Count);
        Assert.Equal(NoticeLevel.Error, rig.Recorded.Notices[^1].Level);

        rig.Recorded.Input = _ => null;
        Assert.Equal("cancelled", await rig.RunAsync(TextToolsPlugin.Expression));
    }

    // ---- large input, nothing held in memory ----

    /// <summary>A source of <paramref name="total"/> bytes that repeats a pattern, without holding more than a block of it.</summary>
    static ChunkReader Pattern(byte[] pattern, long total)
    {
        var block = new byte[pattern.Length * 8192];
        for (int i = 0; i < 8192; i++) pattern.CopyTo(block, i * pattern.Length);
        return (offset, buffer, _) =>
        {
            int n = (int)Math.Min(buffer.Length, total - offset);
            int phase = (int)(offset % pattern.Length), done = 0;
            while (done < n)
            {
                int part = Math.Min(n - done, block.Length - phase);
                block.AsMemory(phase, part).CopyTo(buffer[done..]);
                done += part;
                phase = (phase + part) % block.Length;
            }
            return ValueTask.FromResult(n);
        };
    }

    const long TenGiB = 10L << 30;
    const long WorkingMemory = 64L << 20;

    static Func<long, ValueTask> LiveMemoryProbe(List<long> samples)
    {
        int n = 0;
        return _ =>
        {
            if (++n % 256 == 0)
            {
                GC.Collect();
                samples.Add(GC.GetTotalMemory(true));
            }
            return ValueTask.CompletedTask;
        };
    }

    [Fact]
    [Trait("Category", "Large")]
    public async Task Statistics_of_ten_gibibytes_run_with_a_bounded_working_set_and_can_be_cancelled()
    {
        var pattern = Utf8("ab cd\r\n");
        long total = pattern.Length * (TenGiB / pattern.Length + 1);
        var samples = new List<long>();
        var statistics = new TextStatistics(TextCodec.Utf8);
        var outcome = await ByteScan.RunAsync(0, total, 1 << 20, Pattern(pattern, total), statistics.Add, LiveMemoryProbe(samples), CancellationToken.None);
        var result = statistics.Finish(total);

        Assert.True(outcome.Completed);
        long repeats = total / pattern.Length;
        Assert.Equal(new TextStatisticsResult(total, total, 2 * repeats, repeats + 1, 5), result);
        Assert.True(samples.Count > 30);
        Assert.True(samples.Max() < WorkingMemory, $"live memory reached {samples.Max()} bytes");

        using var cancel = new CancellationTokenSource();
        var cancelled = await ByteScan.RunAsync(0, total, 1 << 20, Pattern(pattern, total), new TextStatistics(TextCodec.Utf8).Add,
            done => { if (done > (64 << 20)) cancel.Cancel(); return ValueTask.CompletedTask; }, cancel.Token);
        Assert.False(cancelled.Completed);
        Assert.InRange(cancelled.Done, 64 << 20, 66 << 20);
    }

    [Fact]
    [Trait("Category", "Large")]
    public async Task Sha256_of_ten_gibibytes_matches_a_direct_computation_with_a_bounded_working_set()
    {
        var pattern = RandomBytes(1021, 11); // a prime length, so the chunk edges fall on every phase
        var samples = new List<long>();
        var (hex, outcome) = await BlockHash.ComputeAsync(HashAlgorithmName.SHA256, 0, TenGiB, 1 << 20, Pattern(pattern, TenGiB), LiveMemoryProbe(samples), CancellationToken.None);

        using var direct = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var reader = Pattern(pattern, TenGiB);
        var buffer = new byte[1 << 20];
        for (long at = 0; at < TenGiB; at += buffer.Length)
        {
            int n = await reader(at, buffer, CancellationToken.None);
            direct.AppendData(buffer.AsSpan(0, n));
        }

        Assert.True(outcome.Completed);
        Assert.Equal(Convert.ToHexStringLower(direct.GetHashAndReset()), hex);
        Assert.True(samples.Max() < WorkingMemory, $"live memory reached {samples.Max()} bytes");
    }
}

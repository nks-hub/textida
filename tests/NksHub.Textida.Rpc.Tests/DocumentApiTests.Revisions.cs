using System.Diagnostics;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>Reads bound to a revision in several encodings and in a paged file, snapshots with a budget, and positions mapped between revisions.</summary>
public partial class DocumentApiTests
{
    static string Of(Document d, string rest = "") => $"{{\"document\":\"{d.Id}\"{(rest.Length > 0 ? "," + rest : "")}}}";

    static long[] Starts(JsonElement found) => [.. found.GetProperty("matches").EnumerateArray().Select(m => m.GetProperty("start").GetInt64())];

    /// <summary>Line and UTF-16 column of every character of the text; a lone CR ends a line, CRLF is one line end.</summary>
    static (long Line, long Column)[] Oracle(string text)
    {
        var result = new (long, long)[text.Length + 1];
        long line = 0, col = 0;
        for (int i = 0; i < text.Length; i++)
        {
            result[i] = (line, col);
            if (text[i] == '\n' || (text[i] == '\r' && !(i + 1 < text.Length && text[i + 1] == '\n'))) { line++; col = 0; }
            else col++;
        }
        result[text.Length] = (line, col);
        return result;
    }

    /// <summary>
    /// Asks the API about every character boundary of <paramref name="text"/>: the bytes and the decoded text of the range up to it, its position, and the offset
    /// of that position. A boundary inside a CRLF is not a position.
    /// </summary>
    static async Task CheckAgainstTextAsync(Rig rig, Document doc, TextCodec codec, string text)
    {
        await T.Until(() => rig.Owner.Run(() => doc.LineIndexState.Complete));
        var expected = Oracle(text);
        long revision = rig.Owner.Run(() => doc.Revision);
        int bom = codec.Bom.Length;
        var all = rig.Owner.Run(() => doc.ReadBytes(new ByteRange(0, doc.ByteLength)));
        Assert.Equal(bom + codec.Encode(text).Length, all.Length);

        if (bom > 0) await rig.ErrorAsync(DocumentMethods.OffsetToPosition, Of(doc, $"\"revision\":{revision},\"offset\":1"), DocumentErrorCodes.PositionUnknown);
        for (int i = 0; i <= text.Length; i++)
        {
            if (i > 0 && i < text.Length && char.IsHighSurrogate(text[i - 1])) continue;
            long offset = bom + codec.Encode(text[..i]).Length;
            string at = $"{codec.Name}, character {i}";

            var bytes = await rig.CallAsync(DocumentMethods.ReadBytes, Of(doc, $"\"revision\":{revision},\"start\":{bom},\"end\":{offset}"));
            Assert.True(all[bom..(int)offset].AsSpan().SequenceEqual(FromB64(bytes)), at);
            var read = await rig.CallAsync(DocumentMethods.ReadText, Of(doc, $"\"revision\":{revision},\"start\":{bom},\"end\":{offset}"));
            Assert.Equal(text[..i], read.GetProperty("text").GetString());
            Assert.Equal(0, read.GetProperty("invalid").GetArrayLength());

            if (i > 0 && i < text.Length && text[i - 1] == '\r' && text[i] == '\n')
            {
                await rig.ErrorAsync(DocumentMethods.OffsetToPosition, Of(doc, $"\"revision\":{revision},\"offset\":{offset}"), DocumentErrorCodes.PositionUnknown);
                continue;
            }
            var position = await rig.CallAsync(DocumentMethods.OffsetToPosition, Of(doc, $"\"revision\":{revision},\"offset\":{offset}"));
            Assert.True(expected[i] == (position.GetProperty("line").GetInt64(), position.GetProperty("column").GetInt64()), at + ": " + position.GetRawText());
            var back = await rig.CallAsync(DocumentMethods.PositionToOffset, Of(doc, $"\"revision\":{revision},\"line\":{expected[i].Line},\"column\":{expected[i].Column}"));
            Assert.True(offset == back.GetProperty("offset").GetInt64(), at + ": " + back.GetRawText());
        }
    }

    [Fact]
    public async Task PositionsRangesAndTextAgreeInEveryEncodingWithCzechEmojiAndCrLf()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"));
        const string czech = "Příliš žluťoučký\r\nkůň úpěl\n\r\nďábelské\r\rody";
        const string emoji = "a😀b\n😀😀\r\nž";
        TextCodec[] codecs = [TextCodec.Utf8, TextCodec.Utf8Bom, TextCodec.Utf16LE, TextCodec.Utf16LEBom, TextCodec.Utf16BEBom, TextCodec.Cp1250];
        foreach (var codec in codecs)
            foreach (var text in codec == TextCodec.Cp1250 ? new[] { czech } : new[] { czech, emoji })
            {
                byte[] bytes = [.. codec.Bom, .. codec.Encode(text)];
                using var doc = Document.FromBytes(bytes, PagedBackendFactory.Instance, new DocumentOptions { Format = codec });
                rig.Owner.Run(() => { rig.Api.Expose(doc); return 0; });
                await CheckAgainstTextAsync(rig, doc, codec, text);
                rig.Owner.Run(() => { rig.Api.Withdraw(doc); return 0; });
            }
    }

    [Fact]
    public async Task APagedFileGivesTheSamePositionsAndTextAsItsDecodedContent()
    {
        var dir = T.TempDir();
        try
        {
            await using var rig = await Rig.CreateAsync(Utf8("x"));
            const string text = "Příliš žluťoučký\r\nkůň 😀 úpěl\n\r\nďábelské\r\rody";
            foreach (var codec in new[] { TextCodec.Utf8, TextCodec.Utf16LEBom })
            {
                var path = Path.Combine(dir, codec.CodePage + ".txt");
                File.WriteAllBytes(path, [.. codec.Bom, .. codec.Encode(text)]);
                var doc = Document.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = codec });
                try
                {
                    rig.Owner.Run(() => { rig.Api.Expose(doc); return 0; });
                    await CheckAgainstTextAsync(rig, doc, codec, text);
                }
                finally { rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; }); }
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ATextRangeThatCutsACharacterIsRefusedOrGrownToWholeCharacters()
    {
        await using var rig = await Rig.CreateAsync(Utf8("a😀b"), codec: TextCodec.Utf8);
        var rev = rig.Revision;
        await rig.ErrorAsync(DocumentMethods.ReadText, rig.D($"\"revision\":{rev},\"start\":2,\"end\":4"), DocumentErrorCodes.InvalidRange);
        var grown = await rig.CallAsync(DocumentMethods.ReadText, rig.D($"\"revision\":{rev},\"start\":2,\"end\":4,\"snap\":true"));
        Assert.Equal((1L, 5L), (grown.GetProperty("start").GetInt64(), grown.GetProperty("end").GetInt64()));
        Assert.Equal("😀", grown.GetProperty("text").GetString());
        // bytes are not text, so the same cut is a plain read
        Assert.Equal(Utf8("a😀b")[2..4], FromB64(await rig.CallAsync(DocumentMethods.ReadBytes, rig.D($"\"revision\":{rev},\"start\":2,\"end\":4"))));
    }

    [Fact]
    public async Task AStaleRevisionIsAnsweredFromASnapshotOfThatRevisionOrRefusedWithTheCurrentOne()
    {
        await using var rig = await Rig.CreateAsync(Utf8("příliš žluťoučký"));
        var first = rig.Revision;
        var word = Utf8("příliš");
        var snapshot = (await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D($"\"revision\":{first}"))).GetProperty("snapshot").GetString()!;
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("Hle: ")));
        var second = rig.Revision;

        var old = await rig.CallAsync(DocumentMethods.ReadBytes, rig.D($"\"revision\":{first},\"start\":0,\"end\":{word.Length}"));
        Assert.Equal(word, FromB64(old));
        Assert.Equal(first, old.GetProperty("revision").GetInt64());
        var found = await rig.CallAsync(DocumentMethods.Find, rig.D($"\"revision\":{first},\"text\":\"ž\""));
        Assert.Equal([word.Length + 1L], Starts(found));
        Assert.Equal("Hle: ", (await rig.CallAsync(DocumentMethods.ReadText, rig.D($"\"revision\":{second},\"start\":0,\"end\":5"))).GetProperty("text").GetString());
        var info = await rig.CallAsync(DocumentMethods.Info, rig.D());
        Assert.Equal(rig.Owner.Run(() => rig.Doc.OldestMappableRevision), info.GetProperty("oldestMappableRevision").GetInt64());

        // the snapshot of the second revision does not stand in for the first one, and nothing can be pinned of a revision the document has left
        await rig.CallAsync(DocumentMethods.SnapshotRelease, $"{{\"snapshot\":\"{snapshot}\"}}");
        await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D($"\"revision\":{second}"));
        var stale = await rig.ErrorAsync(DocumentMethods.ReadBytes, rig.D($"\"revision\":{first},\"start\":0,\"end\":1"), DocumentErrorCodes.RevisionConflict);
        Assert.Equal("Stale", stale.GetProperty("data").GetProperty("reason").GetString());
        Assert.Equal(second, stale.GetProperty("data").GetProperty("currentRevision").GetInt64());
        await rig.ErrorAsync(DocumentMethods.SnapshotCreate, rig.D($"\"revision\":{first}"), DocumentErrorCodes.RevisionConflict);
    }

    [Fact]
    public async Task SnapshotsAreLimitedByBytesAndReleasedWhenThePluginOrTheDocumentEnds()
    {
        await using var rig = await Rig.CreateAsync(Utf8("hello"), tune: o => o with { MaxSnapshotsPerPlugin = 10, MaxSnapshotBytesPerPlugin = 100 });
        long each = 0;
        int taken = 0;
        string? first = null;
        JsonElement refused = default;
        for (; taken < 10; taken++)
        {
            var r = await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D());
            if (r.TryGetProperty("error", out _)) { refused = r; break; }
            each = r.GetProperty("retainedBytes").GetInt64();
            first ??= r.GetProperty("snapshot").GetString();
        }
        Assert.InRange(taken, 1, 9);
        Assert.Equal(DocumentErrorCodes.SnapshotQuotaExceeded, refused.GetProperty("error").GetInt32());
        var data = refused.GetProperty("data");
        Assert.Equal("Bytes", data.GetProperty("reason").GetString());
        Assert.Equal(100, data.GetProperty("limitBytes").GetInt64());
        Assert.Equal(taken * each, data.GetProperty("retainedBytes").GetInt64());
        Assert.True(taken * each <= 100 && (taken + 1) * each > 100);

        // a released snapshot makes room again
        Assert.True((await rig.CallAsync(DocumentMethods.SnapshotRelease, $"{{\"snapshot\":\"{first}\"}}")).GetProperty("released").GetBoolean());
        Assert.True((await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D())).TryGetProperty("snapshot", out _));
        Assert.Equal(taken, rig.Api.PinnedSnapshots("p"));

        // the plugin ended: everything it pinned goes at once
        rig.Owner.Run(() => { rig.Api.PluginEnded("p"); return 0; });
        Assert.Equal(0, rig.Api.PinnedSnapshots("p"));
        await rig.ErrorAsync(DocumentMethods.ReadBytes, $"{{\"snapshot\":\"{first}\",\"start\":0,\"end\":1}}", DocumentErrorCodes.SnapshotExpired);

        // a document that is withdrawn takes the snapshots of it along
        using var other = Document.FromBytes(Utf8("other"), PagedBackendFactory.Instance);
        rig.Owner.Run(() => { rig.Api.Expose(other); return 0; });
        var pinned = (await rig.CallAsync(DocumentMethods.SnapshotCreate, Of(other))).GetProperty("snapshot").GetString();
        Assert.Equal(1, rig.Api.PinnedSnapshots("p"));
        rig.Owner.Run(() => { rig.Api.Withdraw(other); return 0; });
        Assert.Equal(0, rig.Api.PinnedSnapshots("p"));
        await rig.ErrorAsync(DocumentMethods.ReadBytes, $"{{\"snapshot\":\"{pinned}\",\"start\":0,\"end\":1}}", DocumentErrorCodes.SnapshotExpired);
    }

    [Fact]
    public async Task AnExpiredSnapshotIsReleasedEvenWhenNobodyCallsTheApi()
    {
        await using var rig = await Rig.CreateAsync(Utf8("hello"), tune: o => o with { SnapshotLifetime = TimeSpan.FromMilliseconds(300) });
        await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D());
        Assert.Equal(1, rig.Api.PinnedSnapshots("p"));
        await T.Until(() => rig.Api.PinnedSnapshots("p") == 0, 10_000);
    }

    [Fact]
    public async Task PositionsMapToTheCurrentRevisionAndSayWhenTheirTextWasDeleted()
    {
        await using var rig = await Rig.CreateAsync(Utf8("0123456789"), tune: o => o with { MaxMapItems = 8 });
        var from = rig.Revision;
        var snapshot = (await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D())).GetProperty("snapshot").GetString()!;
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("AB")));
        rig.Owner.Run(() => rig.Doc.Delete(new ByteRange(6, 8)));

        // the text "45" of the first revision is gone; 4 and 6 are at its edges, 5 is inside it
        const string positions = "[{\"offset\":1},{\"offset\":0},{\"offset\":0,\"gravity\":\"right\"},{\"offset\":5},{\"offset\":4},{\"offset\":6},{\"offset\":10}]";
        foreach (var source in new[] { $"\"fromRevision\":{from}", $"\"snapshot\":\"{snapshot}\"" })
        {
            var r = await rig.CallAsync(DocumentMethods.MapPositions, rig.D($"{source},\"positions\":{positions}"));
            Assert.Equal(from + 2, r.GetProperty("revision").GetInt64());
            var items = r.GetProperty("positions").EnumerateArray().ToList();
            Assert.Equal(["mapped:3", "mapped:0", "mapped:2", "deleted", "mapped:6", "mapped:6", "mapped:10"],
                items.Select(x => x.GetProperty("status").GetString() == "mapped" ? "mapped:" + x.GetProperty("offset").GetInt64() : x.GetProperty("status").GetString()!));
            Assert.False(items[3].TryGetProperty("offset", out _));
        }

        var ranges = await rig.CallAsync(DocumentMethods.MapRanges, rig.D($"\"snapshot\":\"{snapshot}\",\"ranges\":[{{\"start\":1,\"end\":3}}]"));
        Assert.Equal([(3L, 5L)], ranges.GetProperty("ranges").EnumerateArray().Select(x => (x.GetProperty("start").GetInt64(), x.GetProperty("end").GetInt64())));
        var touched = await rig.ErrorAsync(DocumentMethods.MapRanges, rig.D($"\"snapshot\":\"{snapshot}\",\"ranges\":[{{\"start\":5,\"end\":7}}]"), DocumentErrorCodes.RevisionConflict);
        Assert.Equal("Overlap", touched.GetProperty("data").GetProperty("reason").GetString());

        var future = await rig.ErrorAsync(DocumentMethods.MapPositions, rig.D($"\"fromRevision\":{from + 9},\"positions\":[]"), DocumentErrorCodes.RevisionConflict);
        Assert.Equal("FutureRevision", future.GetProperty("data").GetProperty("reason").GetString());
        // past the end of a revision whose length is known (a snapshot, the current one)
        await rig.ErrorAsync(DocumentMethods.MapPositions, rig.D($"\"snapshot\":\"{snapshot}\",\"positions\":[{{\"offset\":11}}]"), DocumentErrorCodes.InvalidRange);
        await rig.ErrorAsync(DocumentMethods.MapPositions, rig.D($"\"fromRevision\":{rig.Revision},\"positions\":[{{\"offset\":11}}]"), DocumentErrorCodes.InvalidRange);
        await rig.ErrorAsync(DocumentMethods.MapPositions, rig.D($"\"fromRevision\":{from},\"positions\":[{{\"offset\":1,\"gravity\":\"up\"}}]"), RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.MapPositions, rig.D("\"positions\":[]"), RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.MapPositions, "{\"snapshot\":\"nothing\",\"positions\":[]}", DocumentErrorCodes.SnapshotExpired);

        var tooMany = string.Join(",", Enumerable.Repeat("{\"offset\":1}", 9));
        await rig.ErrorAsync(DocumentMethods.MapPositions, rig.D($"\"fromRevision\":{from},\"positions\":[{tooMany}]"), RpcErrorCodes.MessageTooLarge);
        var tooManyRanges = string.Join(",", Enumerable.Repeat("{\"start\":1,\"end\":2}", 9));
        await rig.ErrorAsync(DocumentMethods.MapRanges, rig.D($"\"fromRevision\":{from},\"ranges\":[{tooManyRanges}]"), RpcErrorCodes.MessageTooLarge);
    }

    [Fact]
    public async Task AChangeLogThatNoLongerReachesBackIsAConflictAndTheInfoSaysHowFarItReaches()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"));
        using var doc = Document.FromBytes(Utf8("0123456789"), PagedBackendFactory.Instance, new DocumentOptions { ChangeLogCapacity = 2 });
        rig.Owner.Run(() => { rig.Api.Expose(doc); return 0; });
        var from = rig.Owner.Run(() => doc.Revision);
        for (int i = 0; i < 3; i++) rig.Owner.Run(() => doc.Insert(0, Utf8("a")));

        var info = await rig.CallAsync(DocumentMethods.Info, Of(doc));
        Assert.Equal(from + 1, info.GetProperty("oldestMappableRevision").GetInt64());
        var truncated = await rig.ErrorAsync(DocumentMethods.MapPositions, Of(doc, $"\"fromRevision\":{from},\"positions\":[{{\"offset\":1}}]"), DocumentErrorCodes.RevisionConflict);
        Assert.Equal("LogTruncated", truncated.GetProperty("data").GetProperty("reason").GetString());
        var ok = await rig.CallAsync(DocumentMethods.MapPositions, Of(doc, $"\"fromRevision\":{from + 1},\"positions\":[{{\"offset\":1}}]"));
        Assert.Equal(3, ok.GetProperty("positions")[0].GetProperty("offset").GetInt64());
        rig.Owner.Run(() => { rig.Api.Withdraw(doc); return 0; });
    }

    [Fact]
    public async Task ASearchStopsAfterTheScanLimitAndSaysWhereToGoOn()
    {
        var chars = new string('.', 200).ToCharArray();
        // the third match starts before the limit and ends after it, the fourth starts after it
        foreach (var at in new[] { 10, 90, 98, 110 }) "NEEDLE".CopyTo(0, chars, at, 6);
        await using var rig = await Rig.CreateAsync(Utf8(new string(chars)), tune: o => o with { MaxScanBytes = 100 });
        var rev = rig.Revision;
        foreach (var (method, query) in new[] { (DocumentMethods.Find, "\"text\":\"NEEDLE\""), (DocumentMethods.FindBytes, "\"pattern\":\"4E 45 45 44 4C 45\"") })
        {
            var first = await rig.CallAsync(method, rig.D($"\"revision\":{rev},{query}"));
            Assert.Equal([10L, 90L, 98L], Starts(first));
            Assert.True(first.GetProperty("truncated").GetBoolean());
            Assert.Equal(100, first.GetProperty("next").GetInt64());
            var rest = await rig.CallAsync(method, rig.D($"\"revision\":{rev},{query},\"start\":100"));
            Assert.Equal([110L], Starts(rest));
            Assert.False(rest.GetProperty("truncated").GetBoolean());
            Assert.Equal(JsonValueKind.Null, rest.GetProperty("next").ValueKind);
        }
    }

    [Fact]
    public async Task AFileWithInvalidUtf8IsReadAndWrittenBackWithoutChangingTheBytesAroundTheInvalidOnes()
    {
        var dir = T.TempDir();
        try
        {
            byte[] head = Utf8("Příliš "), bad1 = [0xFF, 0xC3], middle = Utf8(" žluťoučký 😀 "), bad2 = [0xF0, 0x9F, 0x98], tail = Utf8(" kůň\r\n");
            byte[] original = [.. head, .. bad1, .. middle, .. bad2, .. tail];
            var path = Path.Combine(dir, "invalid.txt");
            File.WriteAllBytes(path, original);
            await using var rig = await Rig.CreateAsync(Utf8("x"));
            var doc = Document.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
            try
            {
                rig.Owner.Run(() => { rig.Api.Expose(doc); return 0; });
                var rev = rig.Owner.Run(() => doc.Revision);
                async Task AssertReadAsync(long revision, byte[] bytes)
                {
                    var read = await rig.CallAsync(DocumentMethods.ReadText, Of(doc, $"\"revision\":{revision},\"start\":0,\"end\":{bytes.Length}"));
                    var decoded = read.GetProperty("text").GetString()!;
                    var replaced = Enumerable.Range(0, decoded.Length).Where(i => decoded[i] == '�').ToArray();
                    Assert.Equal(3, replaced.Length);
                    Assert.Equal(replaced, read.GetProperty("invalid").EnumerateArray().Select(x => x.GetInt32()));
                    Assert.Equal(bytes, FromB64(await rig.CallAsync(DocumentMethods.ReadBytes, Of(doc, $"\"revision\":{revision},\"start\":0,\"end\":{bytes.Length}"))));
                }
                await AssertReadAsync(rev, original);

                // the plugin changes the words and writes the unreadable bytes back as it read them
                long bad1At = head.Length, bad2At = head.Length + bad1.Length + middle.Length, wordAt = bad2At + bad2.Length + 1;
                int first = Utf8("Příliš").Length, second = Utf8("kůň").Length;
                var edits = $"[{{\"start\":0,\"end\":{first},\"text\":\"Hotovo\"}},"
                    + $"{{\"start\":{bad1At},\"end\":{bad1At + bad1.Length},\"bytes\":\"{B64(bad1)}\"}},"
                    + $"{{\"start\":{bad2At},\"end\":{bad2At + bad2.Length},\"bytes\":\"{B64(bad2)}\"}},"
                    + $"{{\"start\":{wordAt},\"end\":{wordAt + second},\"text\":\"pes\"}}]";
                var apply = await rig.CallAsync(DocumentMethods.EditApply, Of(doc, $"\"revision\":{rev},\"batchId\":\"invalid-1\",\"edits\":{edits}"));
                Assert.True(apply.GetProperty("applied").GetBoolean());

                byte[] expected = [.. Utf8("Hotovo "), .. bad1, .. middle, .. bad2, .. Utf8(" pes\r\n")];
                Assert.Equal(expected, rig.Owner.Run(() => doc.ReadBytes(new ByteRange(0, doc.ByteLength))));
                await AssertReadAsync(apply.GetProperty("revision").GetInt64(), expected);
                var saved = await doc.SaveAsync();
                Assert.True(saved.Saved);
                Assert.Equal(expected, File.ReadAllBytes(path));
            }
            finally { rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; }); }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    [Trait("Category", "Large")]
    public async Task APluginReadsRangesOfATenGibibytePagedDocumentWithoutLoadingTheFile()
    {
        const long length = 10L << 30;
        var head = Utf8("Příliš žluťoučký kůň 😀\r\n");
        var middle = Utf8("úpěl ďábelské ódy\n");
        var tail = Utf8("konec 😀");
        long middleAt = (5L << 30) + 3, tailAt = length - tail.Length;
        using var file = new SparseFile(length, (0, head), (middleAt, middle), (tailAt, tail));
        await using var rig = await Rig.CreateAsync(Utf8("x"));
        var doc = Document.Open(file.Path, new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = TextCodec.Utf8 });
        try
        {
            rig.Owner.Run(() => { rig.Api.Expose(doc); return 0; });
            long before = GC.GetTotalMemory(true);
            var rev = rig.Owner.Run(() => doc.Revision);

            var info = await rig.CallAsync(DocumentMethods.Info, Of(doc));
            Assert.Equal(length, info.GetProperty("byteLength").GetInt64());
            Assert.False(info.GetProperty("lineIndex").GetProperty("complete").GetBoolean());

            Assert.Equal(middle, FromB64(await rig.CallAsync(DocumentMethods.ReadBytes, Of(doc, $"\"revision\":{rev},\"start\":{middleAt},\"end\":{middleAt + middle.Length}"))));
            Assert.Equal("konec 😀", (await rig.CallAsync(DocumentMethods.ReadText, Of(doc, $"\"revision\":{rev},\"start\":{tailAt},\"end\":{length}"))).GetProperty("text").GetString());
            await rig.ErrorAsync(DocumentMethods.ReadText, Of(doc, $"\"revision\":{rev},\"start\":{length - 3},\"end\":{length}"), DocumentErrorCodes.InvalidRange);
            var grown = await rig.CallAsync(DocumentMethods.ReadText, Of(doc, $"\"revision\":{rev},\"start\":{length - 3},\"end\":{length},\"snap\":true"));
            Assert.Equal(length - 4, grown.GetProperty("start").GetInt64());
            Assert.Equal("😀", grown.GetProperty("text").GetString());
            await rig.ErrorAsync(DocumentMethods.ReadBytes, Of(doc, $"\"revision\":{rev},\"start\":{length - 1},\"end\":{length + 1}"), DocumentErrorCodes.InvalidRange);
            await rig.ErrorAsync(DocumentMethods.ReadBytes, Of(doc, $"\"revision\":{rev},\"start\":{middleAt},\"end\":{middleAt + (1 << 16) + 1}"), RpcErrorCodes.MessageTooLarge);
            await rig.ErrorAsync(DocumentMethods.ReadText, Of(doc, $"\"revision\":{rev},\"start\":{middleAt},\"end\":{middleAt + (1 << 16) + 1}"), RpcErrorCodes.MessageTooLarge);

            // no line index yet: the position is unknown, not line zero
            var unknown = await rig.ErrorAsync(DocumentMethods.OffsetToPosition, Of(doc, $"\"revision\":{rev},\"offset\":{middleAt}"), DocumentErrorCodes.PositionUnknown);
            Assert.False(unknown.GetProperty("data").GetProperty("complete").GetBoolean());

            // a search looks at a bounded stretch and says where it went on
            long inMiddle = middleAt + Utf8("úpěl ďábelské ").Length;
            var found = await rig.CallAsync(DocumentMethods.Find, Of(doc, $"\"revision\":{rev},\"text\":\"ódy\",\"start\":{5L << 30}"));
            Assert.Equal([inMiddle], Starts(found));
            Assert.True(found.GetProperty("truncated").GetBoolean());
            Assert.Equal((5L << 30) + (1 << 22), found.GetProperty("next").GetInt64());
            var foundBytes = await rig.CallAsync(DocumentMethods.FindBytes, Of(doc, $"\"revision\":{rev},\"pattern\":\"C3 B3 64 79\",\"start\":{5L << 30}"));
            Assert.Equal([inMiddle], Starts(foundBytes));
            Assert.Equal((5L << 30) + (1 << 22), foundBytes.GetProperty("next").GetInt64());

            // an edit at the start moves everything; the old revision is refused and the positions of it map
            rig.Owner.Run(() => doc.Insert(0, Utf8("x")));
            var stale = await rig.ErrorAsync(DocumentMethods.ReadBytes, Of(doc, $"\"revision\":{rev},\"start\":{middleAt},\"end\":{middleAt + 1}"), DocumentErrorCodes.RevisionConflict);
            Assert.Equal(rev + 1, stale.GetProperty("data").GetProperty("currentRevision").GetInt64());
            var mapped = await rig.CallAsync(DocumentMethods.MapPositions, Of(doc, $"\"fromRevision\":{rev},\"positions\":[{{\"offset\":{middleAt}}},{{\"offset\":{tailAt}}}]"));
            Assert.Equal([middleAt + 1, tailAt + 1], mapped.GetProperty("positions").EnumerateArray().Select(x => x.GetProperty("offset").GetInt64()));
            Assert.Equal(middle, FromB64(await rig.CallAsync(DocumentMethods.ReadBytes, Of(doc, $"\"revision\":{rev + 1},\"start\":{middleAt + 1},\"end\":{middleAt + 1 + middle.Length}"))));

            Assert.True(GC.GetTotalMemory(true) - before < 128L << 20, "the reads must not load the file");
        }
        finally { rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; }); }
    }

    sealed class SparseFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(T.TempDir(), "huge.txt");

        public SparseFile(long length, params (long At, byte[] Bytes)[] marks)
        {
            File.WriteAllBytes(Path, []);
            if (OperatingSystem.IsWindows())
            {
                var command = new ProcessStartInfo("fsutil") { CreateNoWindow = true, UseShellExecute = false };
                foreach (var arg in new[] { "sparse", "setflag", Path }) command.ArgumentList.Add(arg);
                using var process = Process.Start(command)!;
                process.WaitForExit();
                Assert.Equal(0, process.ExitCode);
            }
            using var stream = new FileStream(Path, FileMode.Open, FileAccess.Write);
            stream.SetLength(length);
            foreach (var (at, bytes) in marks)
            {
                stream.Position = at;
                stream.Write(bytes);
            }
        }

        public void Dispose() => Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, true);
    }
}

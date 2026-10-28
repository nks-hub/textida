using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>Position mapping, character boundaries, scan limits and snapshot sizes checked against Czech text, emoji and CRLF in every encoding the editor opens.</summary>
public partial class DocumentApiTests
{
    /// <summary>The paged backend with snapshots that are whole copies, like the ones of a backend that keeps its text in a text control.</summary>
    sealed class CopyingFactory : IBackendFactory
    {
        public BackendCapabilities Capabilities => PagedBackendFactory.Instance.Capabilities;

        public IByteBackend Create(ReadOnlyMemory<byte> bytes, TextCodec codec) => new Copying(PagedBackendFactory.Instance.Create(bytes, codec));

        sealed class Copying(IByteBackend inner) : IByteBackend
        {
            public BackendCapabilities Capabilities => inner.Capabilities;
            public TextCodec Codec => inner.Codec;
            public long ByteLength => inner.ByteLength;
            public string? BackingPath => inner.BackingPath;
            public event Action<BackendEdit>? EditedByBackend { add => inner.EditedByBackend += value; remove => inner.EditedByBackend -= value; }
            public int Read(long offset, Span<byte> destination) => inner.Read(offset, destination);
            public void Replace(long offset, long deleteLength, ReadOnlySpan<byte> insert) => inner.Replace(offset, deleteLength, insert);
            public string ReadText(ByteRange range) => inner.ReadText(range);
            public void Reinterpret(TextCodec codec) => inner.Reinterpret(codec);
            public bool Supports(TextCodec codec) => inner.Supports(codec);
            public (long Lines, bool Complete) LineIndexState => inner.LineIndexState;
            public bool TryGetPosition(long offset, out LinePosition position) => inner.TryGetPosition(offset, out position);
            public bool TryGetOffset(LinePosition position, out long offset) => inner.TryGetOffset(position, out offset);
            public void CopyTo(Stream destination) => inner.CopyTo(destination);
            public void ReplaceBackingFile(string tempPath, string targetPath) => inner.ReplaceBackingFile(tempPath, targetPath);
            public void Dispose() => inner.Dispose();

            public IByteSnapshot CreateSnapshot()
            {
                var copy = new byte[inner.ByteLength];
                inner.Read(0, copy);
                return new ArraySnapshot(copy);
            }
        }
    }

    static readonly TextCodec[] UnicodeCodecs = [TextCodec.Utf8, TextCodec.Utf8Bom, TextCodec.Utf16LE, TextCodec.Utf16LEBom, TextCodec.Utf16BE, TextCodec.Utf16BEBom];

    /// <summary>Byte offset and string index of every character boundary of the text; there is none inside a surrogate pair.</summary>
    static List<(long Offset, int Index)> Boundaries(TextCodec codec, string text)
    {
        var list = new List<(long, int)>();
        for (int i = 0; i <= text.Length; i++)
        {
            if (i > 0 && i < text.Length && char.IsHighSurrogate(text[i - 1])) continue;
            list.Add((codec.Bom.Length + codec.Encode(text[..i]).Length, i));
        }
        return list;
    }

    static string Quote(string s) => JsonSerializer.Serialize(s);

    /// <summary>Where a boundary of the old text lies in the text after the edits, or null when the text it was inside is gone. Written from the pieces of the new text, not from the mapping code.</summary>
    static int? MovedBoundary(int i, bool right, int insertLength, int rs, int re, int withLength, int ds, int de)
    {
        if (i == 0) return right ? insertLength : 0;
        if (i <= rs) return insertLength + i;
        if (i < re) return null;
        if (i <= ds) return insertLength + rs + withLength + (i - re);
        if (i < de) return null;
        return insertLength + rs + withLength + (ds - re) + (i - de);
    }

    [Fact]
    public async Task PositionsAndRangesFollowATextEditInEveryEncodingForMappedAndCopiedSnapshots()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"));
        (string Text, string Replace, string With, string Delete)[] samples =
        [
            ("Příliš žluťoučký\r\nkůň úpěl\n\r\nďábelské\r\rody", "žluťoučký", "ŽLUŤ", "úpěl\n"),
            ("a😀b\n😀😀\r\nž", "😀", "ZZ", "😀😀"),
        ];
        const string insert = "Žlutý: ";
        foreach (IBackendFactory factory in new IBackendFactory[] { PagedBackendFactory.Instance, new CopyingFactory() })
            foreach (var codec in UnicodeCodecs.Append(TextCodec.Cp1250))
                foreach (var (text, replace, with, delete) in samples)
                {
                    if (codec == TextCodec.Cp1250 && text.Contains("😀")) continue;
                    string at = $"{factory.GetType().Name}, {codec.Name}, {text[..3]}";
                    int bom = codec.Bom.Length;
                    int rs = text.IndexOf(replace, StringComparison.Ordinal), re = rs + replace.Length, ds = text.IndexOf(delete, StringComparison.Ordinal), de = ds + delete.Length;
                    string edited = insert + text[..rs] + with + text[re..ds] + text[de..];
                    long Byte(string s, int chars) => bom + codec.Encode(s[..chars]).Length;

                    byte[] original = [.. codec.Bom, .. codec.Encode(text)];
                    using var doc = Document.FromBytes(original, factory, new DocumentOptions { Format = codec });
                    rig.Owner.Run(() => { rig.Api.Expose(doc); return 0; });
                    try
                    {
                        var first = rig.Owner.Run(() => doc.Revision);
                        var snapshot = (await rig.CallAsync(DocumentMethods.SnapshotCreate, Of(doc, $"\"revision\":{first}"))).GetProperty("snapshot").GetString()!;
                        var edits = $"[{{\"start\":{Byte(text, 0)},\"end\":{Byte(text, 0)},\"text\":{Quote(insert)}}},"
                            + $"{{\"start\":{Byte(text, rs)},\"end\":{Byte(text, re)},\"text\":{Quote(with)}}},"
                            + $"{{\"start\":{Byte(text, ds)},\"end\":{Byte(text, de)},\"text\":\"\"}}]";
                        var apply = await rig.CallAsync(DocumentMethods.EditApply, Of(doc, $"\"revision\":{first},\"batchId\":\"{Guid.NewGuid():N}\",\"edits\":{edits}"));
                        Assert.True(apply.TryGetProperty("applied", out var applied) && applied.GetBoolean(), at + ": " + apply.GetRawText());
                        Assert.Equal(edited, rig.Owner.Run(() => doc.Codec.WithBom(false).GetString(doc.ReadBytes(new ByteRange(bom, doc.ByteLength)))));

                        var boundaries = Boundaries(codec, text);
                        var asked = new List<(long Offset, bool Right, int Index)>();
                        foreach (var (offset, index) in boundaries)
                        {
                            asked.Add((offset, false, index));
                            asked.Add((offset, true, index));
                        }
                        string positions = "[" + string.Join(",", asked.Select(a => $"{{\"offset\":{a.Offset}{(a.Right ? ",\"gravity\":\"right\"" : "")}}}")) + "]";
                        foreach (var source in new[] { $"\"fromRevision\":{first}", $"\"snapshot\":\"{snapshot}\"" })
                        {
                            var mapped = (await rig.CallAsync(DocumentMethods.MapPositions, Of(doc, $"{source},\"positions\":{positions}"))).GetProperty("positions").EnumerateArray().ToList();
                            Assert.Equal(asked.Count, mapped.Count);
                            for (int k = 0; k < asked.Count; k++)
                            {
                                var (offset, right, index) = asked[k];
                                int? moved = MovedBoundary(index, right, insert.Length, rs, re, with.Length, ds, de);
                                string what = $"{at}, {source}, old character {index} {(right ? "right" : "left")}";
                                if (moved is null) Assert.True("deleted" == mapped[k].GetProperty("status").GetString(), what);
                                else
                                {
                                    Assert.True("mapped" == mapped[k].GetProperty("status").GetString(), what);
                                    Assert.True(Byte(edited, moved.Value) == mapped[k].GetProperty("offset").GetInt64(), what + ": " + mapped[k].GetRawText());
                                }
                            }
                        }

                        // ranges between the edits move; one that is the replaced text itself, or reaches into it, is a conflict that names it
                        (int From, int To)[] stable = [(0, rs), (re, ds), (de, text.Length)];
                        var ranges = "[" + string.Join(",", stable.Select(r => $"{{\"start\":{Byte(text, r.From)},\"end\":{Byte(text, r.To)}}}")) + "]";
                        var moved3 = (await rig.CallAsync(DocumentMethods.MapRanges, Of(doc, $"\"snapshot\":\"{snapshot}\",\"ranges\":{ranges}"))).GetProperty("ranges").EnumerateArray()
                            .Select(r => (r.GetProperty("start").GetInt64(), r.GetProperty("end").GetInt64())).ToList();
                        Assert.Equal(stable.Select(r => (Byte(edited, MovedBoundary(r.From, true, insert.Length, rs, re, with.Length, ds, de)!.Value),
                            Byte(edited, MovedBoundary(r.To, false, insert.Length, rs, re, with.Length, ds, de)!.Value))), moved3);
                        var clash = await rig.ErrorAsync(DocumentMethods.MapRanges, Of(doc, $"\"fromRevision\":{first},\"ranges\":[{ranges[1..^1]},{{\"start\":{Byte(text, rs)},\"end\":{Byte(text, re)}}}]"), DocumentErrorCodes.RevisionConflict);
                        Assert.Equal(3, clash.GetProperty("data").GetProperty("index").GetInt32());

                        // the old revision still reads as it was, the new one as edited, and positions of the new one agree with its text
                        var old = await rig.CallAsync(DocumentMethods.ReadText, Of(doc, $"\"revision\":{first},\"start\":{bom},\"end\":{Byte(text, text.Length)}"));
                        Assert.Equal(text, old.GetProperty("text").GetString());
                        var bySnapshot = await rig.CallAsync(DocumentMethods.ReadText, $"{{\"snapshot\":\"{snapshot}\",\"start\":{bom},\"end\":{Byte(text, text.Length)}}}");
                        Assert.Equal(text, bySnapshot.GetProperty("text").GetString());
                        await CheckAgainstTextAsync(rig, doc, codec, edited);
                    }
                    finally { rig.Owner.Run(() => { rig.Api.Withdraw(doc); return 0; }); }
                }
    }

    [Fact]
    public async Task OffsetsInsideACharacterAreNotPositionsAndATextReadRefusesThemOrGrowsToWholeCharacters()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"));
        const string text = "Příliš 😀 žluť\r\nkůň";
        foreach (var codec in UnicodeCodecs.Append(TextCodec.Cp1250))
        {
            string sample = codec == TextCodec.Cp1250 ? text.Replace("😀", "ž") : text;
            int bom = codec.Bom.Length;
            byte[] bytes = [.. codec.Bom, .. codec.Encode(sample)];
            using var doc = Document.FromBytes(bytes, PagedBackendFactory.Instance, new DocumentOptions { Format = codec });
            rig.Owner.Run(() => { rig.Api.Expose(doc); return 0; });
            try
            {
                await T.Until(() => rig.Owner.Run(() => doc.LineIndexState.Complete));
                long revision = rig.Owner.Run(() => doc.Revision);
                string Read(long start, long end, bool snap = false) => Of(doc, $"\"revision\":{revision},\"start\":{start},\"end\":{end}{(snap ? ",\"snap\":true" : "")}");
                var bounds = Boundaries(codec, sample);
                var indexAt = bounds.ToDictionary(b => b.Offset, b => b.Index);
                for (long o = 0; o <= bytes.Length; o++)
                {
                    string at = $"{codec.Name}, byte {o}";
                    if (o < bom)
                    {
                        await rig.ErrorAsync(DocumentMethods.ReadText, Read(o, bytes.Length), DocumentErrorCodes.InvalidRange);
                        continue;
                    }
                    if (indexAt.TryGetValue(o, out int index))
                    {
                        var head = await rig.CallAsync(DocumentMethods.ReadText, Read(bom, o));
                        Assert.True(sample[..index] == head.GetProperty("text").GetString(), at);
                        Assert.Equal(0, head.GetProperty("invalid").GetArrayLength());
                        continue;
                    }
                    await rig.ErrorAsync(DocumentMethods.ReadText, Read(bom, o), DocumentErrorCodes.InvalidRange);
                    await rig.ErrorAsync(DocumentMethods.ReadText, Read(o, bytes.Length), DocumentErrorCodes.InvalidRange);
                    await rig.ErrorAsync(DocumentMethods.OffsetToPosition, Of(doc, $"\"revision\":{revision},\"offset\":{o}"), DocumentErrorCodes.PositionUnknown);
                    // bytes are not text: any cut is a plain read
                    Assert.Equal(bytes[(int)bom..(int)o], FromB64(await rig.CallAsync(DocumentMethods.ReadBytes, Read(bom, o))));

                    long up = bounds.First(b => b.Offset >= o).Offset, down = bounds.Last(b => b.Offset <= o).Offset;
                    var grownEnd = await rig.CallAsync(DocumentMethods.ReadText, Read(bom, o, snap: true));
                    Assert.True(up == grownEnd.GetProperty("end").GetInt64(), at);
                    Assert.True(sample[..indexAt[up]] == grownEnd.GetProperty("text").GetString(), at);
                    var grownStart = await rig.CallAsync(DocumentMethods.ReadText, Read(o, bytes.Length, snap: true));
                    Assert.True(down == grownStart.GetProperty("start").GetInt64(), at);
                    Assert.True(sample[indexAt[down]..] == grownStart.GetProperty("text").GetString(), at);
                }
            }
            finally { rig.Owner.Run(() => { rig.Api.Withdraw(doc); return 0; }); }
        }
    }

    /// <summary>Starts of every overlapping occurrence of <paramref name="needle"/> in <paramref name="haystack"/>, restricted to <paramref name="alignment"/> from <paramref name="origin"/>.</summary>
    static List<long> Occurrences(byte[] haystack, byte[] needle, int origin = 0, int alignment = 1)
    {
        var result = new List<long>();
        for (int i = origin; i + needle.Length <= haystack.Length; i++)
            if ((i - origin) % alignment == 0 && haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) result.Add(i);
        return result;
    }

    [Fact]
    public async Task FollowingNextOrTheLastMatchVisitsEveryMatchOnceInUtf8AndUtf16WhateverTheScanLimit()
    {
        const string line = "aaaa ab aaa Příliš aa\r\n";
        string text = string.Concat(Enumerable.Repeat(line, 40));
        foreach (var codec in new[] { TextCodec.Utf8, TextCodec.Utf8Bom, TextCodec.Utf16LEBom, TextCodec.Utf16BE })
            foreach (long scan in new long[] { 7, 37, 1000 })
            {
                string at = $"{codec.Name}, scan {scan}";
                byte[] bytes = [.. codec.Bom, .. codec.Encode(text)];
                int bom = codec.Bom.Length;
                var needle = codec.Encode("aa");
                var expected = Occurrences(bytes, needle, bom, codec.Alignment);
                Assert.True(expected.Count > 100);
                await using var rig = await Rig.CreateAsync(bytes, tune: o => o with { MaxScanBytes = scan, MaxFindMatches = 5 }, codec: codec);
                var revision = rig.Revision;

                foreach (var (method, query) in new[] { (DocumentMethods.Find, "\"text\":\"aa\""), (DocumentMethods.FindBytes, $"\"pattern\":\"{string.Join(' ', needle.Select(b => b.ToString("X2")))}\"") })
                {
                    var visited = new List<long>();
                    string? start = null;
                    int pages = 0;
                    while (true)
                    {
                        Assert.True(++pages < 20_000, at);
                        var page = await rig.CallAsync(method, rig.D($"\"revision\":{revision},{query}{(start is null ? "" : ",\"start\":" + start)}"));
                        var found = Starts(page);
                        Assert.True(found.Length <= 5, $"{at}, {method}: {found.Length} matches on a page from {start}");
                        visited.AddRange(found);
                        if (!page.GetProperty("truncated").GetBoolean()) break;
                        start = page.GetProperty("next").ValueKind == JsonValueKind.Number
                            ? page.GetProperty("next").GetInt64().ToString()
                            : (found[^1] + 1).ToString();
                    }
                    Assert.True(expected.SequenceEqual(visited), $"{at}, {method}: expected {expected.Count} matches, got {visited.Count}");
                }
            }
    }

    [Fact]
    public async Task ATextSearchSkipsAMatchThatStraddlesTwoUtf16CharactersAndABytePatternDoesNot()
    {
        // "aa" is 61 00 61 00 in UTF-16 LE; the character U+6100 is 00 61, which also appears across the two letters
        var codec = TextCodec.Utf16LEBom;
        byte[] bytes = [.. codec.Bom, .. codec.Encode("aa愀")];
        await using var rig = await Rig.CreateAsync(bytes, codec: codec);
        var rev = rig.Revision;
        var text = await rig.CallAsync(DocumentMethods.Find, rig.D($"\"revision\":{rev},\"text\":\"愀\""));
        Assert.Equal([6L], Starts(text));
        var hex = await rig.CallAsync(DocumentMethods.FindBytes, rig.D($"\"revision\":{rev},\"pattern\":\"00 61\""));
        Assert.Equal([3L, 6L], Starts(hex));
    }

    [Fact]
    public async Task ASearchWithExactlyAsManyMatchesAsTheCapIsNotTruncated()
    {
        await using var rig = await Rig.CreateAsync(Utf8("ab cd ab cd ab"), o => o with { MaxFindMatches = 3 });
        var rev = rig.Revision;
        var text = await rig.CallAsync(DocumentMethods.Find, rig.D($"\"revision\":{rev},\"text\":\"ab\""));
        Assert.Equal([0L, 6L, 12L], Starts(text));
        Assert.False(text.GetProperty("truncated").GetBoolean());
        var hex = await rig.CallAsync(DocumentMethods.FindBytes, rig.D($"\"revision\":{rev},\"pattern\":\"61 62\""));
        Assert.Equal([0L, 6L, 12L], Starts(hex));
        Assert.False(hex.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task HiddenValuesDoNotUseUpTheSearchLimitOfABytePatternSearch()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"), o => o with { MaxFindMatches = 2 });
        var (doc, path) = await ExposeEnvAsync(rig, ".env", Utf8("K=aa\nL=aa\nM=aa\nN=aa\n# aa note\n"));
        try
        {
            var found = new List<long>();
            string? start = null;
            for (int pages = 0; ; pages++)
            {
                Assert.True(pages < 20, "the search does not go on");
                var page = await rig.CallAsync(DocumentMethods.FindBytes, $"{{\"document\":\"{doc.Id}\",\"revision\":{doc.Revision},\"pattern\":\"61 61\"{(start is null ? "" : ",\"start\":" + start)}}}");
                found.AddRange(Starts(page));
                if (!page.GetProperty("truncated").GetBoolean()) break;
                start = page.GetProperty("next").ValueKind == JsonValueKind.Number ? page.GetProperty("next").GetInt64().ToString() : (Starts(page)[^1] + 1).ToString();
            }
            Assert.Equal(new[] { 22L }, found);
        }
        finally { rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; }); Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFileWithManyKindsOfInvalidUtf8KeepsEveryByteAroundEditsNextToThem(bool withBom)
    {
        var dir = T.TempDir();
        try
        {
            var codec = withBom ? TextCodec.Utf8Bom : TextCodec.Utf8;
            // (bytes, true when the bytes are not valid UTF-8); U+FFFD written as EF BF BD is valid and must not be reported
            (byte[] Bytes, bool Invalid)[] original =
            [
                ([0x80], true), (Utf8("Příliš "), false), ([0xFF], true), ([0xEF, 0xBF, 0xBD], false), (Utf8(" žluť "), false),
                ([0xC0, 0x80], true), ([0xED, 0xA0, 0x80], true), ([0xF4, 0x90, 0x80, 0x80], true), ([0xF0, 0x9F, 0x98], true), (Utf8(" kůň😀\r\n"), false), ([0xC3], true),
            ];
            (byte[] Bytes, bool Invalid)[] edited = [.. original];
            edited[1] = (Utf8("Hotovo "), false);
            edited[4] = (Utf8(" X "), false);
            edited[9] = (Utf8(" pes😀\r\n"), false);
            byte[] Join((byte[] Bytes, bool Invalid)[] parts) => [.. codec.Bom, .. parts.SelectMany(p => p.Bytes)];
            (string Text, int[] Invalid) Expected((byte[] Bytes, bool Invalid)[] parts)
            {
                var text = new System.Text.StringBuilder();
                var invalid = new List<int>();
                foreach (var (bytes, bad) in parts)
                {
                    var piece = System.Text.Encoding.UTF8.GetString(bytes);
                    if (bad) invalid.AddRange(Enumerable.Range(text.Length, piece.Length));
                    text.Append(piece);
                }
                return (text.ToString(), [.. invalid]);
            }

            var path = Path.Combine(dir, "invalid.txt");
            File.WriteAllBytes(path, Join(original));
            await using var rig = await Rig.CreateAsync(Utf8("x"), o => o with { MaxScanBytes = 1 << 20 });
            var doc = Document.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = codec });
            try
            {
                rig.Owner.Run(() => { rig.Api.Expose(doc); return 0; });
                int bom = codec.Bom.Length;
                var rev = rig.Owner.Run(() => doc.Revision);
                var snapshot = (await rig.CallAsync(DocumentMethods.SnapshotCreate, Of(doc, $"\"revision\":{rev}"))).GetProperty("snapshot").GetString()!;
                async Task AssertReadAsync(string revision, (byte[] Bytes, bool Invalid)[] parts)
                {
                    var bytes = Join(parts);
                    var (text, invalid) = Expected(parts);
                    var read = await rig.CallAsync(DocumentMethods.ReadText, Of(doc, $"{revision},\"start\":{bom},\"end\":{bytes.Length}"));
                    Assert.Equal(text, read.GetProperty("text").GetString());
                    Assert.Equal(invalid, read.GetProperty("invalid").EnumerateArray().Select(x => x.GetInt32()));
                    Assert.Equal(bytes, FromB64(await rig.CallAsync(DocumentMethods.ReadBytes, Of(doc, $"{revision},\"start\":0,\"end\":{bytes.Length}"))));
                }
                await AssertReadAsync($"\"revision\":{rev}", original);
                var (decoded, flagged) = Expected(original);
                Assert.Equal(flagged.Length + 1, decoded.Count(c => c == '�'));

                // words next to unreadable bytes change, nothing else does
                long[] starts = new long[original.Length + 1];
                starts[0] = bom;
                for (int i = 0; i < original.Length; i++) starts[i + 1] = starts[i] + original[i].Bytes.Length;
                var edits = $"[{{\"start\":{starts[1]},\"end\":{starts[2]},\"text\":\"Hotovo \"}},"
                    + $"{{\"start\":{starts[4]},\"end\":{starts[5]},\"text\":\" X \"}},"
                    + $"{{\"start\":{starts[9]},\"end\":{starts[10]},\"text\":\" pes😀\\r\\n\"}}]";
                var apply = await rig.CallAsync(DocumentMethods.EditApply, Of(doc, $"\"revision\":{rev},\"batchId\":\"inv\",\"edits\":{edits}"));
                Assert.True(apply.GetProperty("applied").GetBoolean(), apply.GetRawText());
                var after = apply.GetProperty("revision").GetInt64();

                await AssertReadAsync($"\"revision\":{after}", edited);
                await AssertReadAsync($"\"snapshot\":\"{snapshot}\"", original);
                await AssertReadAsync($"\"revision\":{rev}", original);

                // a search sees byte offsets, whatever invalid bytes lie before the hit
                var now = Join(edited);
                var pes = await rig.CallAsync(DocumentMethods.Find, Of(doc, $"\"revision\":{after},\"text\":\"pes\""));
                Assert.Equal([(long)now.AsSpan().IndexOf(Utf8("pes"))], Starts(pes));
                var bad = await rig.CallAsync(DocumentMethods.FindBytes, Of(doc, $"\"revision\":{after},\"pattern\":\"F4 90 80 80\""));
                Assert.Equal([(long)now.AsSpan().IndexOf<byte>([0xF4, 0x90, 0x80, 0x80])], Starts(bad));

                var saved = await doc.SaveAsync();
                Assert.True(saved.Saved);
                Assert.Equal(now, File.ReadAllBytes(path));
            }
            finally { rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; }); }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task SnapshotBytesArePiecesForAMappedFileAndTheWholeContentForACopyAndEachPluginHasItsOwnBudget()
    {
        var dir = T.TempDir();
        try
        {
            const int target = 4 << 20;
            var row = Utf8("Příliš žluťoučký kůň úpěl ďábelské ódy\n");
            var content = new byte[target - target % row.Length];
            for (int i = 0; i < content.Length; i += row.Length) row.CopyTo(content, i);
            int size = content.Length;
            var path = Path.Combine(dir, "mapped.txt");
            File.WriteAllBytes(path, content);
            await using var rig = await Rig.CreateAsync(Utf8("x"), tune: o => o with { MaxSnapshotsPerPlugin = 20, MaxSnapshotBytesPerPlugin = 6 << 20 }, plugins: ["p", "q"]);
            var mapped = Document.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
            using var copied = Document.FromBytes(content, new CopyingFactory(), new DocumentOptions { Format = TextCodec.Utf8 });
            try
            {
                rig.Owner.Run(() => { rig.Api.Expose(mapped); rig.Api.Expose(copied); return 0; });
                async Task<JsonElement> Pin(Document d, string plugin = "p") => await rig.CallAsync(DocumentMethods.SnapshotCreate, Of(d), plugin);

                var one = await Pin(mapped);
                Assert.Equal(size, one.GetProperty("byteLength").GetInt64());
                long piece = one.GetProperty("retainedBytes").GetInt64();
                Assert.InRange(piece, 1, 1024);

                // every edit splits or adds a piece, and the snapshot counts the pieces, not the 4 MiB underneath
                rig.Owner.Run(() => { mapped.Insert(row.Length * 20, Utf8("<1>")); mapped.Insert(row.Length * 2000, Utf8("<2>")); mapped.Insert(row.Length * 40000, Utf8("<3>")); return 0; });
                var edited = await Pin(mapped);
                Assert.Equal(7 * piece, edited.GetProperty("retainedBytes").GetInt64());
                Assert.Equal(size + 9, edited.GetProperty("byteLength").GetInt64());

                // a copy counts all of it: it fits next to the two snapshots of the file, a second copy does not
                var copy = await Pin(copied);
                Assert.Equal(size, copy.GetProperty("byteLength").GetInt64());
                Assert.Equal(size, copy.GetProperty("retainedBytes").GetInt64());
                var refused = await rig.ErrorAsync(DocumentMethods.SnapshotCreate, Of(copied), DocumentErrorCodes.SnapshotQuotaExceeded);
                var data = refused.GetProperty("data");
                Assert.Equal("Bytes", data.GetProperty("reason").GetString());
                Assert.Equal(6 << 20, data.GetProperty("limitBytes").GetInt64());
                Assert.Equal(size, data.GetProperty("neededBytes").GetInt64());
                Assert.Equal(8 * piece + size, data.GetProperty("retainedBytes").GetInt64());

                // the budget belongs to the plugin: another one takes its own copy, and the end of the first does not touch it
                Assert.Equal(size, (await Pin(copied, "q")).GetProperty("retainedBytes").GetInt64());
                rig.Owner.Run(() => { rig.Api.PluginEnded("p"); return 0; });
                Assert.Equal(0, rig.Api.PinnedSnapshots("p"));
                Assert.Equal(1, rig.Api.PinnedSnapshots("q"));
                Assert.Equal(size, (await Pin(copied)).GetProperty("retainedBytes").GetInt64());

                var held = (await Pin(mapped, "q")).GetProperty("snapshot").GetString();
                Assert.True((await rig.CallAsync(DocumentMethods.SnapshotRelease, $"{{\"snapshot\":\"{held}\"}}", "q")).GetProperty("released").GetBoolean());
            }
            finally { rig.Owner.Run(() => { rig.Api.Withdraw(mapped); rig.Api.Withdraw(copied); mapped.Dispose(); return 0; }); }
        }
        finally { Directory.Delete(dir, true); }
    }
}

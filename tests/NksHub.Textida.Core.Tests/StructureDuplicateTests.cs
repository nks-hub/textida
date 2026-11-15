using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tests;

public sealed class StructureDuplicateTests
{
    private static StructureIndex Parse(string text, TextCodec? codec = null, bool jsonc = false)
    {
        codec ??= TextCodec.Utf8;
        var source = new DocumentSnapshot(Guid.NewGuid(), 3, new ArraySnapshot([.. codec.Bom, .. codec.Encode(text)]), codec);
        var index = new StructureIndex(source, codec.Bom.Length, jsonc ? StructureFormat.Jsonc : StructureFormat.Json);
        try
        {
            JsonStructureReader.Parse(new(source, codec, codec.Bom.Length, index, CancellationToken.None), jsonc);
            return index;
        }
        catch
        {
            index.Dispose();
            throw;
        }
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16-be")]
    [InlineData("utf32-le")]
    public void DecodedUnicodeNamesMatchAndIssuesKeepOriginalByteSpans(string encoding)
    {
        TextCodec codec = encoding switch
        {
            "utf16-be" => TextCodec.Utf16BEBom,
            "utf32-le" => TextCodec.Utf32LEBom,
            _ => TextCodec.Utf8,
        };
        const string text = "{\"a\":0,\"\\u0061\":1,\"😀\":2,\"\\uD83D\\uDE00\":3,\"é\":4,\"e\\u0301\":5}";
        using var index = Parse(text, codec);

        Assert.Equal(2, StructureDuplicates.Analyze(index, CancellationToken.None));
        Assert.Equal(2, index.IssueCount);
        var issues = index.Issues.OrderBy(issue => issue.Start).ToArray();
        string[] names = ["\"\\u0061\"", "\"\\uD83D\\uDE00\""];
        for (int i = 0; i < names.Length; i++)
        {
            long start = codec.Bom.Length + codec.Encode(text[..text.IndexOf(names[i], StringComparison.Ordinal)]).LongLength;
            Assert.Equal(StructureIssueKind.DuplicateKey, issues[i].Kind);
            Assert.Equal(start, issues[i].Start);
            Assert.Equal(start + codec.Encode(names[i]).LongLength, issues[i].End);
        }
    }

    [Fact]
    public void DuplicateScopeIsOneObjectIncludingNestedObjectsAndArrayItems()
    {
        using var index = Parse("{\"a\":1,\"nested\":{\"a\":2,\"a\":3},\"other\":{\"a\":4},\"arr\":[{\"a\":5},{\"a\":6}],\"a\":7}");

        Assert.Equal(2, StructureDuplicates.Analyze(index, CancellationToken.None));
        Assert.Equal(2, index.Issues.Count);
    }

    [Fact]
    public void EveryOccurrenceAfterTheFirstIsReported()
    {
        const string text = "{\"a\":0,\"a\":1,\"a\":2}";
        using var index = Parse(text);

        Assert.Equal(2, StructureDuplicates.Analyze(index, CancellationToken.None));
        Assert.Equal([7L, 13L], index.Issues.Select(issue => issue.Start));
        Assert.Equal([10L, 16L], index.Issues.Select(issue => issue.End));
    }

    [Fact]
    public void CaseWhitespaceAndUnicodeNormalizationDoNotMakeNamesEqual()
    {
        using var index = Parse("{\"\":0,\"a\":1,\"A\":2,\"a \":3,\"é\":4,\"e\\u0301\":5}");

        Assert.Equal(0, StructureDuplicates.Analyze(index, CancellationToken.None));
        Assert.Empty(index.Issues);
    }

    [Fact]
    public void JsoncCommentsAndTrailingCommasKeepTheSameNameRules()
    {
        using var index = Parse("{/*member*/\"a\":0,\"\\u0061\":1,//tail\n}", jsonc: true);

        Assert.Equal(1, StructureDuplicates.Analyze(index, CancellationToken.None));
        Assert.Equal(StructureIssueKind.DuplicateKey, Assert.Single(index.Issues).Kind);
    }

    [Fact]
    public void TotalDuplicatesContinueBeyondTheRetainedIssueLimit()
    {
        using var index = Parse("{" + string.Join(',', Enumerable.Range(0, 1003).Select(i => $"\"a\":{i}")) + "}");

        Assert.Equal(1002, StructureDuplicates.Analyze(index, CancellationToken.None));
        Assert.Equal(1002, index.IssueCount);
        Assert.Equal(1000, index.Issues.Count);
    }

    [Fact]
    public void SmallChunksAndFanInRequireSeveralMergePassesAndCleanOnlyOwnedFiles()
    {
        using var temp = new TestTemp();
        using var index = Parse("{" + string.Join(',', Enumerable.Range(0, 1000).Select(i => $"\"key-{i % 137}\":{i}")) + "}");
        int mergedRecords = 0;

        long count = StructureDuplicates.Analyze(index, CancellationToken.None, 7, 3, temp.Root, mergedRecord: () => mergedRecords++);

        Assert.Equal(863, count);
        Assert.True(mergedRecords > 2000);
        Assert.Equal(863, index.IssueCount);
        temp.AssertClean();
    }

    [Fact]
    public void HashCollisionsAreResolvedByExactDecodedNameComparison()
    {
        using var temp = new TestTemp();
        using var index = Parse("{\"alpha\":0,\"beta\":1,\"\\u0061lpha\":2,\"gamma\":3,\"beta\":4}");

        Assert.Equal(2, StructureDuplicates.Analyze(index, CancellationToken.None, 2, 2, temp.Root, _ => (0UL, 0UL)));
        Assert.Equal(2, index.Issues.Count);
        temp.AssertClean();
    }

    [Fact]
    public void CancellationDuringMergeRemovesThePartialOutputAndAllOwnedScratchFiles()
    {
        using var temp = new TestTemp();
        using var cancellation = new CancellationTokenSource();
        using var index = Parse("{\"a\":0,\"b\":1,\"c\":2,\"d\":3,\"e\":4}");
        bool observedRuns = false;

        Assert.Throws<OperationCanceledException>(() => StructureDuplicates.Analyze(index, cancellation.Token, 1, 2, temp.Root,
            mergedRecord: () =>
            {
                observedRuns = Directory.GetFiles(temp.Root, "*.run", SearchOption.AllDirectories).Length == 6;
                cancellation.Cancel();
            }));

        Assert.True(observedRuns);
        Assert.Empty(index.Issues);
        temp.AssertClean();
    }

    [Fact]
    public void HashingCancelsInsideAVirtualOneGiBNameWithoutMaterializingIt()
    {
        using var temp = new TestTemp();
        using var cancellation = new CancellationTokenSource();
        var bytes = new LongNamesSnapshot(1024L * 1024 * 1024, false, cancellation);
        using var index = IndexLongNames(bytes);

        Assert.Throws<OperationCanceledException>(() => StructureDuplicates.Analyze(index, cancellation.Token, 2, 2, temp.Root));

        Assert.InRange(bytes.BytesRead, 128 * 1024, 1024 * 1024);
        Assert.Empty(index.Issues);
        temp.AssertClean();
    }

    [Fact]
    public void ExactCollisionComparisonCancelsInsideAVirtualOneGiBName()
    {
        using var temp = new TestTemp();
        using var cancellation = new CancellationTokenSource();
        var bytes = new LongNamesSnapshot(1024L * 1024 * 1024, true, cancellation);
        using var index = IndexLongNames(bytes);

        Assert.Throws<OperationCanceledException>(() => StructureDuplicates.Analyze(index, cancellation.Token, 2, 2, temp.Root,
            _ => (0UL, 0UL)));

        Assert.InRange(bytes.BytesRead, 128 * 1024, 1024 * 1024);
        Assert.Empty(index.Issues);
        temp.AssertClean();
    }

    private static StructureIndex IndexLongNames(LongNamesSnapshot bytes)
    {
        var source = new DocumentSnapshot(Guid.NewGuid(), 1, bytes, TextCodec.Utf8);
        var index = new StructureIndex(source, 0, StructureFormat.Json);
        long document = index.AddNode(StructureKind.Document, -1, 0, -1, -1, 0, 0, 0);
        long root = index.AddNode(StructureKind.JsonObject, document, 0, -1, -1, 0, 0, 1);
        long size = bytes.NameLength;
        long first = index.AddNode(StructureKind.JsonNumber, root, 1, 1, size + 3, size + 4, 0, 2);
        index.CompleteNode(first, size + 4, size + 5, size + 5, 0);
        if (bytes.TwoNames)
        {
            long second = index.AddNode(StructureKind.JsonNumber, root, size + 6, size + 6, 2 * size + 8, 2 * size + 9, 1, 2);
            index.CompleteNode(second, 2 * size + 9, 2 * size + 10, 2 * size + 10, 0);
        }
        index.CompleteNode(root, 0, bytes.ByteLength, bytes.ByteLength, bytes.TwoNames ? 2 : 1);
        index.CompleteNode(document, 0, bytes.ByteLength, bytes.ByteLength, 1);
        return index;
    }

    private sealed class LongNamesSnapshot(long nameLength, bool twoNames, CancellationTokenSource cancellation) : IByteSnapshot
    {
        public long NameLength => nameLength;
        public bool TwoNames => twoNames;
        public long ByteLength => twoNames ? 2 * nameLength + 11 : nameLength + 6;
        public long RetainedBytes => 0;
        public long BytesRead { get; private set; }

        public int Read(long offset, Span<byte> destination)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (offset >= ByteLength) return 0;
            int count = (int)Math.Min(destination.Length, ByteLength - offset);
            destination = destination[..count];
            destination.Fill((byte)'a');
            Put(destination, offset, 0, '{');
            Put(destination, offset, 1, '"');
            Put(destination, offset, nameLength + 2, '"');
            Put(destination, offset, nameLength + 3, ':');
            Put(destination, offset, nameLength + 4, '0');
            if (twoNames)
            {
                Put(destination, offset, nameLength + 5, ',');
                Put(destination, offset, nameLength + 6, '"');
                Put(destination, offset, 2 * nameLength + 7, '"');
                Put(destination, offset, 2 * nameLength + 8, ':');
                Put(destination, offset, 2 * nameLength + 9, '1');
            }
            Put(destination, offset, ByteLength - 1, '}');
            BytesRead += count;
            if (BytesRead >= 128 * 1024) cancellation.Cancel();
            return count;
        }

        private static void Put(Span<byte> destination, long offset, long position, char value)
        {
            if (position >= offset && position - offset < destination.Length) destination[(int)(position - offset)] = (byte)value;
        }
    }

    private sealed class TestTemp : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("textida-duplicate-test-").FullName;
        private readonly string marker;

        public TestTemp()
        {
            marker = Path.Combine(Root, "unrelated.txt");
            File.WriteAllText(marker, "Keep this file.");
        }

        public void AssertClean()
        {
            Assert.Equal([marker], Directory.GetFileSystemEntries(Root));
            Assert.Equal("Keep this file.", File.ReadAllText(marker));
        }

        public void Dispose()
        {
            File.Delete(marker);
            Directory.Delete(Root, false);
        }
    }
}

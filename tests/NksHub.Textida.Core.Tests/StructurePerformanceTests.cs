using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Xunit.Abstractions;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

[Collection("Process memory")]
public sealed class StructurePerformanceTests(ITestOutputHelper output)
{
    private const long MiB = 1024 * 1024;
    private const long ExtraMemoryLimit = 64 * MiB;
    private static readonly object warmupGate = new();
    private static bool warmed;

    [HugeJsonFact]
    [Trait("Category", "Perf")]
    [Trait("Category", "Large")]
    public Task ARealOneGiBJsonFileCompletesWithinTheExtraMemoryBudgetAndCancelsItsGiantString() =>
        MeasureHuge(StructureFormat.Json);

    [HugeJsonFact]
    [Trait("Category", "Perf")]
    [Trait("Category", "Large")]
    public Task ARealOneGiBXmlFileCompletesWithinTheExtraMemoryBudgetAndCancelsItsGiantText() =>
        MeasureHuge(StructureFormat.Xml);

    private async Task MeasureHuge(StructureFormat format)
    {
        WarmUp();
        using var fixture = StructurePerformanceFixture.Giant(format, 1024 * MiB);
        using var document = Open(fixture);
        var observed = new ObservedSnapshot(document.GetSnapshot());
        var source = new DocumentSnapshot(document.Id, document.Revision, observed, document.Codec);
        LogSource(fixture, document, source);
        using var index = await MeasureCompleted(fixture, source, observed);

        for (int repetition = 0; repetition < 3; repetition++)
            await MeasureParseCancellation(fixture, source, repetition);
        if (format == StructureFormat.Json)
            await MeasureDuplicateCancellation(fixture, index, observed);
    }

    [Theory]
    [Trait("Category", "Perf")]
    [InlineData(StructureFormat.Json, 1)]
    [InlineData(StructureFormat.Json, 8)]
    [InlineData(StructureFormat.Xml, 1)]
    [InlineData(StructureFormat.Xml, 8)]
    public async Task ManyNodeFilesKeepTheMemoryBoundWhileTheDiskIndexGrows(StructureFormat format, int mebibytes)
    {
        WarmUp();
        using var fixture = StructurePerformanceFixture.ManyNodes(format, mebibytes * MiB);
        using var document = Open(fixture);
        var observed = new ObservedSnapshot(document.GetSnapshot());
        var source = new DocumentSnapshot(document.Id, document.Revision, observed, document.Codec);
        LogSource(fixture, document, source);
        using var index = await MeasureCompleted(fixture, source, observed);

        long readsBefore = observed.ReadCalls;
        var clock = Stopwatch.StartNew();
        long after = -1;
        int returned = 0;
        for (int page = 0; page < 8; page++)
        {
            var children = index.ReadChildren(1, after, 64, CancellationToken.None);
            Assert.Equal(64, children.Count);
            returned += children.Count;
            after = children[^1].Id;
        }
        for (int point = 1; point < 16; point++)
        {
            long at = source.ByteLength * point / 16;
            var node = index.FindContaining(at, CancellationToken.None);
            Assert.True(node.HasValue && node.Value.Contains(at));
        }
        output.WriteLine($"index-read bytes={source.ByteLength} nodes={index.Count} child-pages=8 children={returned} " +
            $"lookups=15 ms={clock.Elapsed.TotalMilliseconds:F3} source-read-calls={observed.ReadCalls - readsBefore}");
        Assert.Equal(readsBefore, observed.ReadCalls);
    }

    private async Task<StructureIndex> MeasureCompleted(StructurePerformanceFixture fixture, DocumentSnapshot source, ObservedSnapshot observed)
    {
        await using var memory = new MemoryProbe();
        var index = new StructureIndex(source, 0, fixture.Format);
        try
        {
            var clock = Stopwatch.StartNew();
            await Task.Run(() => Parse(fixture.Format, new(source, source.Codec, 0, index, CancellationToken.None)));
            double parseMilliseconds = clock.Elapsed.TotalMilliseconds;
            Assert.Equal(source.ByteLength, index.ReadNode(0).End);
            Assert.Equal(1, index.ReadNode(0).ChildCount);
            Assert.Equal(fixture.ExpectedNodes, index.Count);
            Assert.Equal(0, index.IssueCount);

            memory.Sample();
            clock.Restart();
            long duplicates = await Task.Run(() => StructureDuplicates.Analyze(index, CancellationToken.None));
            double duplicateMilliseconds = clock.Elapsed.TotalMilliseconds;
            Assert.Equal(fixture.ExpectedDuplicates, duplicates);
            Assert.Equal(duplicates, index.IssueCount);
            Assert.Equal(Math.Min(1000L, duplicates), index.Issues.Count);
            Assert.Same(source, index.Source);
            Assert.Equal(source.RetainedBytes, index.SourceRetainedBytes);
            MemoryResult measured = await memory.Finish();
            output.WriteLine($"completed format={fixture.Format} bytes={source.ByteLength} sha256={fixture.Sha256} " +
                $"codec={source.Codec.Name} nodes={index.Count} parse-ms={parseMilliseconds:F3} duplicates={duplicates} " +
                $"duplicates-ms={duplicateMilliseconds:F3} source-retained={index.SourceRetainedBytes} cache-bytes={index.CachedBytes} " +
                $"source-read-calls={observed.ReadCalls} source-read-bytes={observed.ReadBytes}");
            output.WriteLine($"memory baseline-managed={measured.BaselineManaged} peak-managed={measured.PeakManaged} " +
                $"retained-managed={measured.RetainedManaged} extra-peak-managed={measured.ExtraManaged} " +
                $"baseline-private={measured.BaselinePrivate} peak-private={measured.PeakPrivate} " +
                $"final-private={measured.FinalPrivate} extra-peak-private={measured.ExtraPrivate} samples={measured.Samples} sample-ms=10");
            Assert.InRange(index.CachedBytes, 0, 4 * MiB);
            Assert.InRange(index.SourceRetainedBytes, 0, MiB);
            Assert.True(measured.ExtraManaged <= ExtraMemoryLimit, $"Managed heap grew by {measured.ExtraManaged} bytes.");
            Assert.True(measured.ExtraPrivate <= ExtraMemoryLimit, $"Private memory grew by {measured.ExtraPrivate} bytes.");
            return index;
        }
        catch
        {
            index.Dispose();
            throw;
        }
    }

    private async Task MeasureParseCancellation(StructurePerformanceFixture fixture, DocumentSnapshot source, int repetition)
    {
        using var cancellation = new CancellationTokenSource();
        var enteredToken = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var work = Task.Run(() =>
        {
            bool canceled = false;
            long nodes;
            using (var index = new StructureIndex(source, 0, fixture.Format))
            {
                var context = new StructureParseContext(source, source.Codec, 0, index, cancellation.Token,
                    at => { if (at >= MiB) enteredToken.TrySetResult(at); });
                try { Parse(fixture.Format, context); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { canceled = true; }
                nodes = index.Count;
            }
            return (Canceled: canceled, StoppedAt: Stopwatch.GetTimestamp(), Nodes: nodes);
        });

        try
        {
            await Task.WhenAny(enteredToken.Task, work).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(enteredToken.Task.IsCompletedSuccessfully, "Parser ended before entering the giant token.");
            Assert.False(work.IsCompleted, "Parser was no longer running when cancellation was requested.");
            Assert.InRange(await enteredToken.Task, fixture.LargeTokenContentStart, fixture.LargeTokenContentEnd - 1);
            long requestedAt = Stopwatch.GetTimestamp();
            cancellation.Cancel();
            var result = await work;
            double milliseconds = Stopwatch.GetElapsedTime(requestedAt, result.StoppedAt).TotalMilliseconds;
            output.WriteLine($"cancel phase=parse format={fixture.Format} repetition={repetition} bytes={source.ByteLength} " +
                $"sha256={fixture.Sha256} codec={source.Codec.Name} nodes={result.Nodes} completed=false " +
                $"entered-byte={await enteredToken.Task} canceled={result.Canceled} " +
                $"token-content=[{fixture.LargeTokenContentStart},{fixture.LargeTokenContentEnd}) " +
                $"latency-ms={milliseconds:F3} includes-index-dispose=true");
            Assert.True(result.Canceled);
            Assert.InRange(milliseconds, 0, 100);
        }
        finally
        {
            cancellation.Cancel();
            await work;
        }
    }

    private async Task MeasureDuplicateCancellation(StructurePerformanceFixture fixture, StructureIndex index, ObservedSnapshot observed)
    {
        using var cancellation = new CancellationTokenSource();
        var readingName = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        observed.ReadCompleted = offset => readingName.TrySetResult(offset);
        var work = Task.Run(() =>
        {
            bool canceled = false;
            try { StructureDuplicates.Analyze(index, cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { canceled = true; }
            return (Canceled: canceled, StoppedAt: Stopwatch.GetTimestamp());
        });
        try
        {
            await Task.WhenAny(readingName.Task, work).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(readingName.Task.IsCompletedSuccessfully, "Duplicate analysis ended before reading a name.");
            Assert.False(work.IsCompleted, "Duplicate analysis was no longer running when cancellation was requested.");
            long requestedAt = Stopwatch.GetTimestamp();
            cancellation.Cancel();
            var result = await work;
            double milliseconds = Stopwatch.GetElapsedTime(requestedAt, result.StoppedAt).TotalMilliseconds;
            output.WriteLine($"cancel phase=duplicates format={fixture.Format} bytes={index.Source.ByteLength} " +
                $"sha256={fixture.Sha256} codec={index.Codec.Name} nodes={index.Count} reading-byte={await readingName.Task} canceled={result.Canceled} " +
                $"latency-ms={milliseconds:F3} includes-scratch-cleanup=true");
            Assert.True(result.Canceled);
            Assert.InRange(milliseconds, 0, 100);
        }
        finally
        {
            cancellation.Cancel();
            try { await work; }
            finally { observed.ReadCompleted = null; }
        }
    }

    [Theory]
    [Trait("Category", "Perf")]
    [InlineData("json-whitespace")]
    [InlineData("json-number")]
    [InlineData("json-string")]
    [InlineData("xml-text")]
    [InlineData("xml-cdata")]
    public void RepeatedCancellationInsideEightMiBTokensStopsBeforeTheTokenEnds(string token)
    {
        WarmUp();
        byte[] bytes = TokenBytes(token, 8 * 1024 * 1024);
        string sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        LogRuntime();
        StructureFormat format = token.StartsWith("xml", StringComparison.Ordinal) ? StructureFormat.Xml : StructureFormat.Json;
        for (int repetition = 0; repetition < 3; repetition++)
        {
            using var cancellation = new CancellationTokenSource();
            var source = new DocumentSnapshot(Guid.NewGuid(), 0, new ArraySnapshot(bytes), TextCodec.Utf8);
            using var index = new StructureIndex(source, 0, format);
            long canceledAt = -1;
            var context = new StructureParseContext(source, TextCodec.Utf8, 0, index, cancellation.Token, at =>
            {
                if (at < 64 * 1024) return;
                canceledAt = at;
                cancellation.Cancel();
            });
            Assert.Throws<OperationCanceledException>(() => Parse(format, context));
            Assert.InRange(canceledAt, 64 * 1024, 128 * 1024);
            output.WriteLine($"cancel deterministic token={token} repetition={repetition} bytes={bytes.Length} " +
                $"sha256={sha256} codec={source.Codec.Name} nodes={index.Count} progress-byte={canceledAt} " +
                $"completed=false wall-clock-limit=none");
        }
    }

    private static Doc Open(StructurePerformanceFixture fixture) => Doc.Open(fixture.Path,
        new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = TextCodec.Utf8 });

    private void LogSource(StructurePerformanceFixture fixture, Doc document, DocumentSnapshot source)
    {
        Assert.Equal(fixture.Size, source.ByteLength);
        Assert.Equal(fixture.Sha256, document.Fingerprint!.Sha256, ignoreCase: true);
        Assert.IsType<PagedBackend>(document.Backend);
        LogRuntime();
        output.WriteLine($"fixture format={fixture.Format} bytes={source.ByteLength} sha256={fixture.Sha256} " +
            $"codec={source.Codec.Name} backend={document.Backend.Capabilities.Name} source-retained={source.RetainedBytes} " +
            $"rows={fixture.Rows} generation-ms={fixture.GenerationMilliseconds:F3} baseline=opened-paged-document-and-snapshot");
    }

    private void LogRuntime()
    {
        string build = typeof(StructureIndex).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        output.WriteLine($"runtime framework={RuntimeInformation.FrameworkDescription} os={RuntimeInformation.OSDescription} " +
            $"architecture={RuntimeInformation.ProcessArchitecture} pid={Environment.ProcessId} core-build={build}");
    }

    private static void Parse(StructureFormat format, StructureParseContext context)
    {
        if (format == StructureFormat.Xml) XmlStructureReader.Parse(context);
        else JsonStructureReader.Parse(context);
    }

    private static byte[] TokenBytes(string token, int size)
    {
        var (prefix, fill, suffix) = token switch
        {
            "json-whitespace" => ("", (byte)' ', "0"),
            "json-number" => ("", (byte)'1', ""),
            "json-string" => ("\"", (byte)'a', "\""),
            "xml-text" => ("<r>", (byte)'a', "</r>"),
            "xml-cdata" => ("<r><![CDATA[", (byte)'a', "]]></r>"),
            _ => throw new ArgumentOutOfRangeException(nameof(token)),
        };
        byte[] bytes = new byte[size];
        bytes.AsSpan().Fill(fill);
        Encoding.UTF8.GetBytes(prefix).CopyTo(bytes, 0);
        byte[] tail = Encoding.UTF8.GetBytes(suffix);
        tail.CopyTo(bytes, bytes.Length - tail.Length);
        return bytes;
    }

    private static void WarmUp()
    {
        lock (warmupGate)
        {
            if (warmed) return;
            foreach (string token in new[] { "json-whitespace", "json-number", "json-string", "xml-text", "xml-cdata" })
            {
                var source = new DocumentSnapshot(Guid.NewGuid(), 0, new ArraySnapshot(TokenBytes(token, 16 * 1024)), TextCodec.Utf8);
                StructureFormat format = token.StartsWith("xml", StringComparison.Ordinal) ? StructureFormat.Xml : StructureFormat.Json;
                using var index = new StructureIndex(source, 0, format);
                Parse(format, new(source, TextCodec.Utf8, 0, index, CancellationToken.None));
                index.ReadChildren(0, -1, 1, CancellationToken.None);
                index.FindContaining(source.ByteLength / 2, CancellationToken.None);
                using var cancellation = new CancellationTokenSource();
                using var canceledIndex = new StructureIndex(source, 0, format);
                var context = new StructureParseContext(source, TextCodec.Utf8, 0, canceledIndex, cancellation.Token,
                    at => { if (at >= 4096) cancellation.Cancel(); });
                Assert.Throws<OperationCanceledException>(() => Parse(format, context));
            }
            byte[] json = Encoding.UTF8.GetBytes("{" + string.Join(',', Enumerable.Repeat("\"a\":0", 100)) + "}");
            var names = new DocumentSnapshot(Guid.NewGuid(), 0, new ArraySnapshot(json), TextCodec.Utf8);
            using var duplicates = new StructureIndex(names, 0, StructureFormat.Json);
            JsonStructureReader.Parse(new(names, TextCodec.Utf8, 0, duplicates, CancellationToken.None));
            Assert.Equal(99, StructureDuplicates.Analyze(duplicates, CancellationToken.None, 8, 2, System.IO.Path.GetTempPath()));
            warmed = true;
        }
    }

    private sealed class ObservedSnapshot(IByteSnapshot source) : IByteSnapshot
    {
        private long reads;
        private long bytes;
        public long ByteLength => source.ByteLength;
        public long RetainedBytes => source.RetainedBytes;
        public long ReadCalls => Interlocked.Read(ref reads);
        public long ReadBytes => Interlocked.Read(ref bytes);
        public Action<long>? ReadCompleted { get; set; }

        public int Read(long offset, Span<byte> destination)
        {
            int count = source.Read(offset, destination);
            Interlocked.Increment(ref reads);
            Interlocked.Add(ref bytes, count);
            ReadCompleted?.Invoke(offset);
            return count;
        }
    }

    private sealed record MemoryResult(long BaselineManaged, long PeakManaged, long RetainedManaged,
        long BaselinePrivate, long PeakPrivate, long FinalPrivate, int Samples)
    {
        public long ExtraManaged => Math.Max(0, PeakManaged - BaselineManaged);
        public long ExtraPrivate => Math.Max(0, PeakPrivate - BaselinePrivate);
    }

    private sealed class MemoryProbe : IAsyncDisposable
    {
        private readonly Process process = Process.GetCurrentProcess();
        private readonly CancellationTokenSource stop = new();
        private readonly object gate = new();
        private readonly long baselineManaged;
        private readonly long baselinePrivate;
        private readonly Task sampler;
        private long peakManaged;
        private long peakPrivate;
        private int samples;
        private MemoryResult? result;

        public MemoryProbe()
        {
            baselineManaged = GC.GetTotalMemory(true);
            process.Refresh();
            baselinePrivate = process.PrivateMemorySize64;
            peakManaged = baselineManaged;
            peakPrivate = baselinePrivate;
            sampler = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
                try
                {
                    while (await timer.WaitForNextTickAsync(stop.Token)) Sample();
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            });
        }

        public void Sample()
        {
            lock (gate)
            {
                peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
                process.Refresh();
                peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
                samples++;
            }
        }

        public async Task<MemoryResult> Finish()
        {
            if (result is not null) return result;
            stop.Cancel();
            await sampler;
            Sample();
            long retained = GC.GetTotalMemory(true);
            process.Refresh();
            long finalPrivate = process.PrivateMemorySize64;
            result = new(baselineManaged, Math.Max(peakManaged, retained), retained, baselinePrivate,
                Math.Max(peakPrivate, finalPrivate), finalPrivate, samples);
            return result;
        }

        public async ValueTask DisposeAsync()
        {
            try { await Finish(); }
            finally
            {
                stop.Dispose();
                process.Dispose();
            }
        }
    }
}

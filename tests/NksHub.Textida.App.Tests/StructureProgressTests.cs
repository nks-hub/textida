using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class StructureProgressTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [Trait("Category", "Perf")]
    [InlineData(StructureFormat.Json)]
    [InlineData(StructureFormat.Xml)]
    public async Task Throttled_parse_progress_keeps_worker_allocations_bounded(StructureFormat format)
    {
        var bytes = new LongTokenSnapshot(format);
        Guid documentId = Guid.NewGuid();
        var source = new StructureSource(bytes, documentId, 0, TextCodec.Utf8, 0, format, 0, true,
            () => new DocumentSnapshot(documentId, 0, bytes, TextCodec.Utf8),
            _ => { }, (_, _, _) => Task.FromResult(false));
        var view = new StructureView(() => source, _ => Task.CompletedTask,
            (_, _, _) => Task.FromResult<string?>(null));
        var window = new Window { Width = 900, Height = 700, Content = view };
        try
        {
            window.Show(); window.UpdateLayout();
            view.Sync();
            await Drain(view);
            output.WriteLine($"{format}: allocated={bytes.MeasuredAllocatedBytes} B, measured={bytes.MeasuredBytes} B, "
                + $"parsed={bytes.ParsedBytes} B, read={bytes.ReadBytes} B, forwardReads={bytes.ForwardReads}, worker={bytes.WorkerThreadId}, UI={bytes.UiThreadId}");

            var document = Assert.Single(DataItems(view.Tree));
            Assert.Equal(StructureKind.Document, Node(document).Kind);
            Assert.Equal(bytes.ByteLength, Node(document).End);
            var root = Assert.Single(document.Items, item => item.Tag is StructureNode);
            Assert.Equal(format == StructureFormat.Json ? StructureKind.JsonString : StructureKind.XmlElement, Node(root).Kind);
            Assert.Equal(Node(document).Id, Node(root).ParentId);
            Assert.Equal(bytes.ByteLength, Node(root).End);
            StructureTreeNode leaf = root;
            if (format == StructureFormat.Xml)
            {
                root.IsExpanded = true;
                await Drain(view);
                leaf = Assert.Single(root.Items, item => item.Tag is StructureNode);
                Assert.Equal(StructureKind.XmlText, Node(leaf).Kind);
                Assert.Equal(Node(root).Id, Node(leaf).ParentId);
            }
            Assert.Equal(format == StructureFormat.Json ? 0 : bytes.ContentStart, Node(leaf).ValueStart);
            Assert.Equal(format == StructureFormat.Json ? bytes.ByteLength : bytes.ContentEnd, Node(leaf).ValueEnd);
            Assert.Contains("xxxxxxxx", leaf.DisplayName, StringComparison.Ordinal);
            Assert.Equal(bytes.ByteLength, bytes.ParsedBytes);
            Assert.True(bytes.ReadBytes >= bytes.ByteLength);
            Assert.True(bytes.WorkerThreadId > 0);
            Assert.NotEqual(bytes.UiThreadId, bytes.WorkerThreadId);
            Assert.True(bytes.MeasurementComplete);
            Assert.InRange(bytes.MeasuredBytes, 29L << 20, bytes.ByteLength);
            Assert.InRange(bytes.MeasuredAllocatedBytes, 0, 64L << 10);
        }
        finally
        {
            view.Dispose();
            window.Close();
        }
    }

    private static StructureNode Node(StructureTreeNode item) => Assert.IsType<StructureNode>(item.Tag);
    private static IEnumerable<StructureTreeNode> DataItems(StructureTree tree)
        => tree.Items.OfType<StructureTreeNode>().Where(item => item.Tag is StructureNode);

    private static async Task Drain(StructureView view)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until)
        {
            Dispatcher.UIThread.RunJobs();
            Task work = view.Working;
            if (work.IsCompleted)
            {
                await work;
                Dispatcher.UIThread.RunJobs();
                if (ReferenceEquals(work, view.Working)) return;
            }
            await Task.Delay(10);
        }
        Assert.Fail("The structure worker did not finish.");
    }

    private sealed class LongTokenSnapshot : IByteSnapshot
    {
        public const long MeasureStart = 1L << 20;
        public const long MeasureEnd = 31L << 20;
        private readonly byte[] prefix, suffix;
        private long allocatedStart = -1, allocatedEnd = -1;
        private long measuredStart, measuredEnd, readBytes;
        public long ByteLength => 32L << 20;
        public long RetainedBytes => prefix.Length + suffix.Length;
        public long ContentStart => prefix.Length;
        public long ContentEnd => ByteLength - suffix.Length;
        public int UiThreadId { get; } = Environment.CurrentManagedThreadId;
        public int WorkerThreadId { get; private set; }
        public long ParsedBytes { get; private set; }
        public long ForwardReads { get; private set; }
        public long ReadBytes => Interlocked.Read(ref readBytes);
        public bool MeasurementComplete => allocatedStart >= 0 && allocatedEnd >= allocatedStart;
        public long MeasuredBytes => measuredEnd - measuredStart;
        public long MeasuredAllocatedBytes => allocatedEnd - allocatedStart;

        public LongTokenSnapshot(StructureFormat format)
        {
            prefix = Encoding.ASCII.GetBytes(format == StructureFormat.Json ? "\"" : "<root>");
            suffix = Encoding.ASCII.GetBytes(format == StructureFormat.Json ? "\"" : "</root>");
        }

        public int Read(long offset, Span<byte> destination)
        {
            if (offset < 0 || offset > ByteLength) throw new ArgumentOutOfRangeException(nameof(offset));
            int count = (int)Math.Min(destination.Length, ByteLength - offset);
            if (count == 0) return 0;
            Interlocked.Add(ref readBytes, count);
            long readEnd = offset + count;
            if (readEnd > ParsedBytes)
            {
                int thread = Environment.CurrentManagedThreadId;
                if (offset >= MeasureStart && allocatedStart < 0)
                {
                    WorkerThreadId = thread;
                    measuredStart = offset;
                    allocatedStart = GC.GetAllocatedBytesForCurrentThread();
                }
                if (allocatedStart >= 0 && allocatedEnd < 0)
                {
                    if (thread != WorkerThreadId || thread == UiThreadId)
                        throw new InvalidOperationException("The measured parser reads changed worker thread or ran on the UI thread.");
                    if (offset >= MeasureEnd)
                    {
                        measuredEnd = offset;
                        allocatedEnd = GC.GetAllocatedBytesForCurrentThread();
                    }
                }
                ParsedBytes = readEnd;
                ForwardReads++;
            }
            var target = destination[..count];
            target.Fill((byte)'x');
            Overlay(target, offset, prefix, 0);
            Overlay(target, offset, suffix, ContentEnd);
            return count;
        }

        private static void Overlay(Span<byte> target, long offset, byte[] value, long start)
        {
            long from = Math.Max(offset, start), to = Math.Min(offset + target.Length, start + value.Length);
            if (to > from) value.AsSpan((int)(from - start), (int)(to - from)).CopyTo(target[(int)(from - offset)..]);
        }
    }
}

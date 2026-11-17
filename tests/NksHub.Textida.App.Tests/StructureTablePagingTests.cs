using NksHub.Textida.App.Shell;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Tests;

public sealed class StructureTablePagingTests
{
    [Theory]
    [InlineData(64)]
    [InlineData(4)]
    public void A_thousand_forward_and_reverse_pages_have_bounded_metadata_reads(int size)
    {
        var index = new CountingIndex();
        var parent = index.ReadNode(0);
        var pages = new StructureTablePages();
        for (int page = 0; page < 1000; page++) Check(page);
        for (int page = 998; page >= 0; page--) Check(page);

        void Check(int page)
        {
            index.Reads = 0;
            var rows = pages.Read(index, parent, page, size, CancellationToken.None);
            Assert.Equal(size, rows.Count); Assert.Equal((long)page * size, rows[0].Ordinal);
            Assert.InRange(index.Reads, size, size * 2 + 4);
            Assert.InRange(pages.Count, 1, StructureView.MaximumItems);
        }
    }

    [Fact]
    public void A_cancelled_page_does_not_publish_its_cursor_or_scan_the_remaining_page()
    {
        var index = new CountingIndex();
        var parent = index.ReadNode(0);
        var pages = new StructureTablePages();
        _ = pages.Read(index, parent, 800, 64, CancellationToken.None);
        var pending = pages.Clone();
        using var cancellation = new CancellationTokenSource();
        index.Reads = 0; index.OnRead = () => { if (index.Reads == 12) cancellation.Cancel(); };
        Assert.Throws<OperationCanceledException>(() => pending.Read(index, parent, 801, 64, cancellation.Token));
        Assert.Equal(12, index.Reads); Assert.Equal(1, pages.Count); Assert.Equal(1, pending.Count);
        index.OnRead = null; index.Reads = 0;
        var next = pages.Read(index, parent, 801, 64, CancellationToken.None);
        Assert.Equal(801L * 64, next[0].Ordinal);
        Assert.InRange(index.Reads, 64, 68);
    }

    private sealed class CountingIndex : IStructureIndex
    {
        public int Reads;
        public Action? OnRead;
        public long Count => 100001;
        public IByteSnapshot Source => throw new NotSupportedException();
        public TextCodec Codec => TextCodec.Utf8;
        public int BomLength => 0;
        public StructureFormat Format => StructureFormat.Json;
        public Guid DocumentId => Guid.Empty;
        public long Revision => 0;

        public StructureNode ReadNode(long id)
        {
            Reads++; OnRead?.Invoke();
            if (id < 0 || id >= Count) throw new ArgumentOutOfRangeException(nameof(id));
            return id == 0 ? new(0, -1, StructureKind.JsonArray, 0, Count, -1, -1, 0, Count, 0, Count - 1, Count - 1, 0)
                : new(id, 0, StructureKind.JsonNumber, id, id + 1, -1, -1, id, id + 1, id - 1, 0, id, 1);
        }
    }
}

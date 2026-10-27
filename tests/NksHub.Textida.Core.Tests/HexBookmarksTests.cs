using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.Core.Journal;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>P5.6.3: named, coloured regions of the hex view that follow the bytes through every change, and their export and import.</summary>
public class HexBookmarksTests
{
    private static Doc Make(int length = 100) => Doc.FromBytes(Enumerable.Range(0, length).Select(i => (byte)i).ToArray(), new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });

    private static ByteRange R(long a, long b) => new(a, b);

    [Fact]
    public void A_region_survives_bytes_inserted_before_it_and_follows_undo_and_redo()
    {
        using var d = Make();
        using var marks = new HexBookmarks(d);
        var b = marks.Add(R(40, 44), "length", "#FF8800", "four bytes", "uint32 LE");

        Assert.True(d.Insert(10, new byte[] { 1, 2, 3 }, new() { RawBytes = true }).Applied);
        Assert.Equal(R(43, 47), b.Range);
        Assert.True(d.Insert(0, new byte[] { 9 }, new() { RawBytes = true }).Applied);
        Assert.Equal(R(44, 48), b.Range);
        Assert.NotNull(d.Undo());
        Assert.NotNull(d.Undo());
        Assert.Equal(R(40, 44), b.Range);
        Assert.NotNull(d.Redo());
        Assert.Equal(R(43, 47), b.Range);

        Assert.True(d.Delete(R(0, 5), new() { RawBytes = true }).Applied);
        Assert.Equal(R(38, 42), b.Range);
        Assert.Equal(new byte[] { 40, 41, 42, 43 }, d.ReadBytes(b.Range)); // still the same four bytes
    }

    [Fact]
    public void Bytes_at_the_edges_stay_outside_and_bytes_inside_make_the_region_longer()
    {
        using var d = Make();
        using var marks = new HexBookmarks(d);
        var b = marks.Add(R(20, 30));

        d.Insert(20, new byte[] { 0xAA }, new() { RawBytes = true }); // exactly at the start: before the region
        Assert.Equal(R(21, 31), b.Range);
        d.Insert(31, new byte[] { 0xBB }, new() { RawBytes = true }); // exactly at the end: after it
        Assert.Equal(R(21, 31), b.Range);
        d.Insert(25, new byte[] { 0xCC, 0xDD }, new() { RawBytes = true }); // inside
        Assert.Equal(R(21, 33), b.Range);
    }

    [Fact]
    public void An_overwrite_of_the_bytes_of_a_region_keeps_it_and_an_edit_over_an_edge_is_taken_in()
    {
        using var d = Make();
        using var marks = new HexBookmarks(d);
        var b = marks.Add(R(20, 24));
        var raw = new EditOptions { RawBytes = true };

        d.Replace(R(20, 24), new byte[] { 1, 1, 1, 1 }, raw);
        Assert.Equal(R(20, 24), b.Range);
        d.Replace(R(18, 22), new byte[] { 7, 7, 7, 7 }, raw);   // over the start: the new bytes belong to the region
        Assert.Equal(R(18, 24), b.Range);
        d.Replace(R(23, 27), new byte[] { 8, 8 }, raw);          // over the end, shorter than before
        Assert.Equal(R(18, 25), b.Range);
        d.Delete(R(10, 20), new() { RawBytes = true });          // a deletion that cuts into the start
        Assert.Equal(R(10, 15), b.Range);
    }

    [Fact]
    public void A_region_whose_bytes_are_all_deleted_is_dropped()
    {
        using var d = Make();
        using var marks = new HexBookmarks(d);
        marks.Add(R(20, 24), "gone");
        var kept = marks.Add(R(50, 52), "kept");
        int changes = 0;
        marks.Changed += () => changes++;

        d.Delete(R(18, 28), new() { RawBytes = true });

        Assert.Equal([kept], marks.Items);
        Assert.Equal(R(40, 42), kept.Range);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Regions_are_listed_in_order_and_found_by_byte_and_text()
    {
        using var d = Make();
        using var marks = new HexBookmarks(d);
        marks.Add(R(60, 70), "tail", "#00FF00", "last part");
        marks.Add(R(10, 90), "whole", null, "the body", "bytes");
        marks.Add(R(20, 24), "header", "#FF0000", "Magic NUMBER", "uint32 BE");

        Assert.Equal(["whole", "header", "tail"], marks.Items.Select(b => b.Name));
        Assert.Equal([10L, 20L, 60L], marks.Items.Select(b => b.Range.Start));
        Assert.Equal(["header", "whole"], marks.Containing(21).Select(b => b.Name));
        Assert.Equal(["header"], marks.Filter("magic").Select(b => b.Name));
        Assert.Equal(["header"], marks.Filter("uint32").Select(b => b.Name));
        Assert.Equal(["tail"], marks.Filter("", "#00ff00").Select(b => b.Name));
        Assert.Empty(marks.Filter("zzz"));
        Assert.Equal(20, marks.Step(10, forward: true)!.Range.Start);
        Assert.Equal(10, marks.Step(60, forward: true)!.Range.Start); // wraps
        Assert.Equal(60, marks.Step(10, forward: false)!.Range.Start);
    }

    [Fact]
    public void A_region_needs_bytes_inside_the_document_and_a_valid_colour()
    {
        using var d = Make(10);
        using var marks = new HexBookmarks(d);
        Assert.Throws<ArgumentException>(() => marks.Add(R(3, 3)));
        Assert.Throws<ArgumentException>(() => marks.Add(R(5, 11)));
        Assert.Throws<ArgumentException>(() => marks.Add(R(1, 2), color: "red"));
        Assert.Throws<ArgumentException>(() => marks.Add(R(1, 2), color: "#12345G"));
        Assert.Equal("#AABBCC", marks.Add(R(1, 2), color: " #aabbcc ").Color);
    }

    [Fact]
    public void Export_and_import_give_back_the_same_regions_as_csv_and_as_json()
    {
        using var d = Make(500);
        using var marks = new HexBookmarks(d);
        marks.Add(R(0, 4), "magic, \"quoted\"", "#112233", "line one\r\nline two", "uint32 LE");
        marks.Add(R(100, 164), "plain");
        marks.Add(R(200, 201), "čeština ✓", "#FFFFFF", null, "uint8");
        var expected = marks.Capture();

        foreach (var export in new Func<string>[] { marks.ToCsv, marks.ToJson })
        {
            string text = export();
            marks.Clear();
            Assert.Empty(marks.Items);
            var result = marks.Import(text);
            Assert.Equal(new HexBookmarkImport(3, 0), result);
            Assert.Equal(expected, marks.Capture());
            Assert.Equal(new HexBookmarkImport(0, 3), marks.Import(text)); // the same regions again are skipped
        }
    }

    [Fact]
    public void Import_skips_what_does_not_fit_and_refuses_what_it_cannot_read()
    {
        using var d = Make(50);
        using var marks = new HexBookmarks(d);
        var result = marks.Import("name,color,comment,type,start,end\r\nok,,,,1,5\r\nlate,,,,40,90\r\nempty,,,,7,7\r\n,#BAD,,,10,12\r\n");
        Assert.Equal(new HexBookmarkImport(2, 2), result);
        Assert.Null(marks.Items.Single(b => b.Range.Start == 10).Color); // a bad colour is dropped, the region is kept
        Assert.Throws<FormatException>(() => marks.Import("hello"));
        Assert.Throws<FormatException>(() => marks.Import("name,color,comment,type,start,end\r\na,,,,x,2\r\n"));
        Assert.Throws<FormatException>(() => marks.Import("{\"nothing\": 1}"));
        Assert.Throws<FormatException>(() => marks.Import("name,color,comment,type,start,end\r\n\"open,,,,1,2\r\n"));
    }

    [Fact]
    public void Stored_regions_of_an_older_revision_are_mapped_through_the_changes_since()
    {
        using var d = Make();
        using var marks = new HexBookmarks(d);
        marks.Add(R(40, 44), "a");
        marks.Add(R(10, 12), "b");
        long revision = d.Revision;
        var stored = marks.Capture();
        d.Insert(0, new byte[] { 1, 1 }, new() { RawBytes = true });
        d.Delete(R(9, 15), new() { RawBytes = true }); // swallows region b

        using var other = new HexBookmarks(d);
        other.Restore(stored, revision);

        var a = Assert.Single(other.Items);
        Assert.Equal(("a", R(36, 40)), (a.Name, a.Range));
        Assert.Equal(new byte[] { 40, 41, 42, 43 }, d.ReadBytes(a.Range));
    }

    [Fact]
    public void A_listener_is_removed_with_the_set()
    {
        using var d = Make();
        var marks = new HexBookmarks(d);
        marks.Add(R(1, 2));
        marks.Dispose();
        d.Insert(0, new byte[] { 1 }, new() { RawBytes = true });
        Assert.Equal(R(1, 2), marks.Items[0].Range); // no longer followed
    }

    [Fact]
    public void The_regions_travel_with_the_session_and_are_mapped_by_a_recovery()
    {
        using var dir = new JournalTempDir();
        string journalDir = dir.Combine("journal");
        Func<string, IBackendFactory?> backends = JournalFormatTests.ReferenceResolver;
        var first = JournalSession.Start(journalDir, backends, new JournalOptions { Fsync = FsyncPolicy.Manual });
        var doc = Doc.FromBytes(Enumerable.Range(0, 64).Select(i => (byte)i).ToArray(), ReferenceBackendFactory.Instance);
        var id = first.Track(doc);
        var marks = new HexBookmarks(doc);
        marks.Add(R(30, 34), "field", "#336699", "kept in the session", "uint32 BE");

        doc.Insert(0, new byte[] { 0xEE, 0xEE }, new() { RawBytes = true });
        first.UpdateSession(new SessionModel([new([new SessionTab(id, "x", doc.Revision, [], [], marks.Capture())], id)], 0));
        doc.Insert(0, new byte[] { 0xFF }, new() { RawBytes = true }); // after the session was written
        first.Dispose();

        using var second = JournalSession.Start(journalDir, backends);
        var restored = second.Recovery.Documents.Single().Document!;
        var tab = second.Recovery.Session!.AllTabs.Single();
        using var back = new HexBookmarks(restored);
        back.Restore(tab.HexBookmarks!, restored.Revision);

        var b = Assert.Single(back.Items);
        Assert.Equal(("field", "#336699", "kept in the session", "uint32 BE"), (b.Name, b.Color, b.Comment, b.Type));
        Assert.Equal(R(33, 37), b.Range);
        Assert.Equal(new byte[] { 30, 31, 32, 33 }, restored.ReadBytes(b.Range));
        restored.Dispose();
        marks.Dispose();
        doc.Dispose();
    }
}

using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>P9.5.2: the edits of a paged document against the file it maps, which a named session keeps for a file too large to keep as text.</summary>
public class PagedFileEditsTests
{
    private static byte[] Apply(byte[] file, IReadOnlyList<FileEdit> edits)
    {
        var result = new List<byte>(file.Length);
        long at = 0;
        foreach (var edit in edits)
        {
            Assert.True(edit.Start >= at, "the edits overlap or are out of order");
            result.AddRange(file.AsSpan((int)at, (int)(edit.Start - at)).ToArray());
            result.AddRange(edit.Inserted);
            at = edit.Start + edit.Deleted;
        }
        result.AddRange(file.AsSpan((int)at).ToArray());
        return [.. result];
    }

    private static byte[] Content(PagedDocument doc)
    {
        var bytes = new byte[doc.ByteLength];
        Assert.Equal(bytes.Length, doc.Read(0, bytes));
        return bytes;
    }

    [Fact]
    public void An_unedited_document_has_no_edits_and_the_edits_of_a_changed_one_turn_the_file_into_it()
    {
        byte[] file = Gen.RandomBytes(new Random(1), 5000);
        using var doc = PagedDocument.OpenMemory(file);
        Assert.Empty(doc.EditsAgainstFile(long.MaxValue)!);

        doc.Replace(100, 10, [1, 2, 3]);
        doc.Insert(0, [9]);
        doc.Delete(4984, 10); // the last ten bytes of the file, six bytes earlier in the document
        doc.Insert(2500, [5, 5, 5, 5]); // byte 2506 of the file
        var edits = doc.EditsAgainstFile(long.MaxValue)!;

        Assert.Equal(4, edits.Count);
        Assert.Equal((0L, 0L), (edits[0].Start, edits[0].Deleted));
        Assert.Equal(new byte[] { 9 }, edits[0].Inserted);
        Assert.Equal((100L, 10L), (edits[1].Start, edits[1].Deleted));
        Assert.Equal(new byte[] { 1, 2, 3 }, edits[1].Inserted);
        Assert.Equal((2506L, 0L), (edits[2].Start, edits[2].Deleted));
        Assert.Equal(new byte[] { 5, 5, 5, 5 }, edits[2].Inserted);
        Assert.Equal((4990L, 10L), (edits[3].Start, edits[3].Deleted));
        Assert.Empty(edits[3].Inserted);
        Assert.Equal(Content(doc), Apply(file, edits));
    }

    [Fact]
    public void Random_edits_with_undo_and_redo_always_leave_edits_that_rebuild_the_content()
    {
        var random = new Random(77);
        byte[] file = Gen.RandomBytes(random, 3000);
        using var doc = PagedDocument.OpenMemory(file);
        for (int step = 0; step < 600; step++)
        {
            switch (random.Next(6))
            {
                case 0: doc.Insert(random.NextInt64(0, doc.ByteLength + 1), Gen.RandomBytes(random, random.Next(1, 40))); break;
                case 1 when doc.ByteLength > 0:
                    long at = random.NextInt64(0, doc.ByteLength);
                    doc.Delete(at, random.NextInt64(0, Math.Min(60, doc.ByteLength - at) + 1));
                    break;
                case 2 when doc.ByteLength > 0:
                    long from = random.NextInt64(0, doc.ByteLength);
                    doc.Replace(from, random.NextInt64(0, Math.Min(30, doc.ByteLength - from) + 1), Gen.RandomBytes(random, random.Next(0, 30)));
                    break;
                case 3: doc.Undo(); break;
                case 4: doc.Redo(); break;
                default: doc.Insert(0, Gen.RandomBytes(random, 3)); break;
            }
            var edits = doc.EditsAgainstFile(long.MaxValue)!;
            Assert.Equal(Content(doc), Apply(file, edits));
            for (int i = 1; i < edits.Count; i++) Assert.True(edits[i - 1].Start + edits[i - 1].Deleted < edits[i].Start, "two edits touch each other");
        }
    }

    [Fact]
    public void Deleting_everything_and_typing_over_it_is_one_edit_and_a_cap_on_the_inserted_bytes_gives_no_edits()
    {
        byte[] file = Gen.RandomBytes(new Random(2), 1000);
        using var doc = PagedDocument.OpenMemory(file);
        doc.Delete(0, 1000);
        var gone = Assert.Single(doc.EditsAgainstFile(0)!);
        Assert.Equal((0L, 1000L), (gone.Start, gone.Deleted));
        Assert.Empty(gone.Inserted);
        doc.Insert(0, new byte[600]);
        doc.Insert(300, new byte[10]);

        Assert.Single(doc.EditsAgainstFile(610)!);
        Assert.Null(doc.EditsAgainstFile(609));
        Assert.Equal(new byte[610], Content(doc));
    }

    [Fact]
    public void After_a_streamed_save_the_edits_are_those_made_on_the_saved_file_and_a_repeated_fill_is_counted_by_its_bytes()
    {
        string dir = Directory.CreateTempSubdirectory("textida-file-edits-").FullName;
        try
        {
            string path = Path.Combine(dir, "doc.txt");
            byte[] file = Gen.RandomBytes(new Random(3), 200_000);
            File.WriteAllBytes(path, file);
            using var doc = Doc.Open(path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252 });
            PagedDocument Paged() => ((PagedBackend)doc.Backend).Paged; // a save that is not streamed maps the file anew
            Assert.True(doc.Insert(50, new byte[] { 7, 7, 7 }).Applied);
            Assert.True(doc.Delete(new ByteRange(1000, 1100)).Applied);
            Assert.True(doc.Save().Saved);
            byte[] saved = File.ReadAllBytes(path);
            Assert.Empty(Paged().EditsAgainstFile(long.MaxValue)!);

            Assert.True(doc.Replace(new ByteRange(10, 12), new byte[] { 1 }).Applied);
            Assert.Equal(Content(Paged()), Apply(saved, Paged().EditsAgainstFile(long.MaxValue)!));

            Assert.True(doc.Fill(new ByteRange(5000, 5010), new byte[] { 0xAB }, 100_000).Applied);
            Assert.Null(Paged().EditsAgainstFile(99_999));
            Assert.Equal(Content(Paged()), Apply(saved, Paged().EditsAgainstFile(100_001)!));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}

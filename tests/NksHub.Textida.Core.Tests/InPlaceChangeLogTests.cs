using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P3.1.13: an in-place write is planned from the change log, so it may happen only while the log still holds every change since the last save. At exactly the
/// capacity of the log it does, with one change more the oldest change is gone and the save must write a new file, or that change would be missing on the disk.
/// </summary>
public class InPlaceChangeLogTests
{
    private static EditOptions Raw => new() { RawBytes = true };

    /// <summary>Overwrites two bytes at <paramref name="index"/> * 41 with their complement, in the document and in the model.</summary>
    private static void Flip(Doc doc, byte[] model, int index)
    {
        long at = index * 41L;
        byte[] bytes = [(byte)~model[at], (byte)~model[at + 1]];
        Assert.True(doc.Replace(new(at, at + 2), bytes, Raw).Applied);
        bytes.CopyTo(model, at);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(8192)]
    public void An_in_place_write_covers_every_change_the_log_holds_and_one_change_more_makes_the_save_a_new_file_that_still_has_all_of_them(int capacity)
    {
        byte[] model = Gen.RandomBytes(new Random(capacity), 41 * (3 * capacity + 10));
        using var file = new TempFile(model);
        using var d = Doc.Open(file.Path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Cp1252, ChangeLogCapacity = capacity });

        // exactly as many changes as the log holds: the first one is the oldest the log can still name
        for (int i = 0; i < capacity; i++) Flip(d, model, i);
        var plan = d.PlanSave();
        Assert.Equal(SaveMode.InPlace, plan.Mode);
        Assert.Equal(2L * capacity, plan.ChangedBytes);
        Assert.True(d.Save().Saved);
        Assert.Equal(model, File.ReadAllBytes(file.Path));

        // one more than the log holds: the oldest change has left it
        for (int i = 0; i <= capacity; i++) Flip(d, model, capacity + i);
        Assert.Equal(SaveMode.Stream, d.PlanSave().Mode);
        Assert.True(d.Save().Saved);
        Assert.Equal(model, File.ReadAllBytes(file.Path));

        // the streamed save made a new baseline, and the next run of changes is written in place again
        for (int i = 0; i < capacity; i++) Flip(d, model, 2 * capacity + 1 + i);
        Assert.Equal(SaveMode.InPlace, d.PlanSave().Mode);
        Assert.True(d.Save().Saved);
        Assert.Equal(model, File.ReadAllBytes(file.Path));
        Assert.False(d.IsModified);
    }
}

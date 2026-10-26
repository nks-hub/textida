using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tests;

public class PagedSnapshotSaveTests
{
    [Fact]
    public async Task Saving_then_editing_does_not_overwrite_an_older_snapshots_add_buffer()
    {
        using var file = new TempFile();
        using var document = PagedDocument.Open(file.Path, new() { StartIndexing = false, Codec = TextCodec.Cp1252 });
        document.Insert(0, "X"u8);
        var before = document.CreateSnapshot();
        await document.SaveAsync();
        document.Insert(0, "Y"u8);
        byte[] oldBytes = new byte[1];
        Assert.Equal(1, before.Read(0, oldBytes));
        Assert.Equal("X"u8.ToArray(), oldBytes);
        byte[] current = new byte[2];
        Assert.Equal(2, document.Read(0, current));
        Assert.Equal("YX"u8.ToArray(), current);
        Assert.Equal("X"u8.ToArray(), File.ReadAllBytes(file.Path));
    }
}

using NksHub.Textida.Core.Documents;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Document.Contract;

/// <summary>P1.2.10: an unnamed unsaved document moves between backends in memory and keeps everything.</summary>
public static class BackendSwitch
{
    public static void RoundTrip(params IBackendFactory[] chain)
    {
        byte[] original = [0xEF, 0xBB, 0xBF, .. "a\r\nb\nc\rd"u8, 0xE2, 0x82, (byte)'x', 0xFF, 0xC3, 0xA9, 0x80, .. "\r\n\r"u8];
        var format = TestFormat.Utf8Bom.Format;
        int tempBefore = Directory.GetFiles(Path.GetTempPath(), "*.nkspad-*.tmp").Length;

        using var d = Doc.FromBytes(original, chain[0], new DocumentOptions { Format = format });
        Assert.True(d.Insert(3, "Z"u8.ToArray()).Applied);
        Assert.True(d.Delete(new(5, 7)).Applied);
        var anchor = d.CreateAnchor(10);
        var snapshot = d.GetSnapshot();
        var id = d.Id;
        long revision = d.Revision;
        var bytes = d.ReadBytes(new(0, d.ByteLength));
        var positions = Positions(d);

        foreach (var next in chain.Skip(1).Concat([chain[0]]))
        {
            d.SwitchBackend(next);
            Assert.Equal(next.Capabilities.Name, d.Capabilities.Name);
            Assert.Equal(id, d.Id);
            Assert.Equal(revision, d.Revision);
            Assert.True(d.IsModified);
            Assert.Null(d.FilePath);
            Assert.Null(d.Fingerprint);
            Assert.Null(d.Backend.BackingPath);
            Assert.True(anchor.IsValid);
            Assert.Equal(10, anchor.Offset);
            Assert.Equal(bytes, d.ReadBytes(new(0, d.ByteLength)));
            Assert.Equal(positions, Positions(d));
            Assert.Equal(bytes, snapshot.ReadBytes(new(0, snapshot.ByteLength)));
        }

        Assert.True(d.Insert(d.ByteLength, "!"u8.ToArray()).Applied);
        d.SwitchBackend(chain[^1]);
        d.Undo();
        d.Undo();
        d.Undo();
        Assert.Equal(original, d.ReadBytes(new(0, d.ByteLength)));
        Assert.False(d.CanUndo);
        d.Redo();
        d.Redo();
        d.Redo();
        Assert.Equal([.. bytes, (byte)'!'], d.ReadBytes(new(0, d.ByteLength)));
        Assert.True(d.IsModified);
        Assert.Equal(tempBefore, Directory.GetFiles(Path.GetTempPath(), "*.nkspad-*.tmp").Length);
    }

    private static List<(long, bool, LinePosition)> Positions(Doc d)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!d.LineIndexState.Complete && DateTime.UtcNow < until) Thread.Sleep(1);
        var list = new List<(long, bool, LinePosition)>();
        for (long o = 0; o <= d.ByteLength; o++) list.Add((o, d.TryGetPosition(o, out var p), p));
        return list;
    }
}

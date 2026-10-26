using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Document.Contract;

/// <summary>Seeded random edits, undo, redo and position queries against a naive byte-array model and across backends.</summary>
public static class RandomizedParity
{
    private static readonly byte[][] Pieces =
    [
        "a"u8.ToArray(), "b"u8.ToArray(), "é"u8.ToArray(), "漢"u8.ToArray(), "😀"u8.ToArray(), "\r\n"u8.ToArray(), "\n"u8.ToArray(), "\r"u8.ToArray(), " "u8.ToArray(),
        [0xE2, 0x82], [0xFF], [0x80], [0xF0, 0x9F], [0xAC], [0xC3],
    ];

    private static byte[] RandomBytes(Random r, int pieces)
    {
        var list = new List<byte>();
        for (int i = 0; i < pieces; i++) list.AddRange(Pieces[r.Next(Pieces.Length)]);
        return [.. list];
    }

    private static void WaitIndex(Doc d)
    {
        var until = DateTime.UtcNow.AddSeconds(20);
        while (!d.LineIndexState.Complete)
        {
            Assert.True(DateTime.UtcNow < until, "line index did not complete");
            Thread.Sleep(1);
        }
    }

    public static void Run(int seed, int steps, params IBackendFactory[] factories)
    {
        var r = new Random(seed);
        byte[] initial = [.. (seed % 2 == 0 ? new byte[] { 0xEF, 0xBB, 0xBF } : []), .. RandomBytes(r, 40)];
        var format = TestFormat.Utf8Bom.Format;
        var docs = factories.Select(f => Doc.FromBytes(initial, f, new DocumentOptions { Format = format, ChangeLogCapacity = 64 })).ToArray();
        try
        {
            var model = new NaiveModel(initial);
            int applied = 0, rejected = 0, undone = 0;
            for (int step = 0; step < steps; step++)
            {
                int op = r.Next(20);
                if (op < 14) { if (Edit(r, model, docs, step)) applied++; else rejected++; }
                else if (op < 17)
                {
                    bool expected = model.Undo();
                    if (expected) undone++;
                    foreach (var d in docs) Assert.Equal(expected, d.Undo() is not null);
                }
                else if (op < 19)
                {
                    bool expected = model.Redo();
                    foreach (var d in docs) Assert.Equal(expected, d.Redo() is not null);
                }
                else Positions(r, model, docs);

                foreach (var d in docs)
                {
                    Assert.Equal(model.Bytes.Length, d.ByteLength);
                    if (step % 25 == 0 || step == steps - 1) Assert.Equal(model.Bytes, d.ReadBytes(new(0, d.ByteLength)));
                }
            }
            Positions(r, model, docs, all: true);
            Assert.True(applied > steps / 6 && rejected > steps / 60 && undone > steps / 40, $"applied {applied} rejected {rejected} undone {undone}");
            Assert.Equal(docs[0].Revision, docs[^1].Revision);
        }
        finally
        {
            foreach (var d in docs) d.Dispose();
        }
    }

    private static bool Edit(Random r, NaiveModel model, Doc[] docs, int step)
    {
        var bounds = model.Boundaries().Where(b => b >= model.Bom).Order().ToList();
        int Pick() => r.Next(8) == 0 ? r.Next(model.Bytes.Length + 1) : bounds[r.Next(bounds.Count)];

        var edits = new List<(int Start, int Deleted, byte[] Insert)>();
        int count = r.Next(5) == 0 ? r.Next(2, 4) : 1;
        int floor = 0;
        for (int k = 0; k < count; k++)
        {
            int start = Math.Max(Pick(), floor);
            if (start > model.Bytes.Length) break;
            var after = bounds.Where(b => b >= start && b <= Math.Min(model.Bytes.Length, start + 12)).ToList();
            int end = r.Next(4) == 0 || after.Count == 0 ? start : after[r.Next(after.Count)];
            var insert = r.Next(3) == 0 ? [] : RandomBytes(r, r.Next(1, 4));
            if (start == end && insert.Length == 0) continue;
            edits.Add((start, end - start, insert));
            floor = end + (count > 1 ? 8 : 0);
        }
        if (edits.Count == 0) return false;

        // valid when every edit alone starts and ends on boundaries and leaves its two joins on boundaries (gaps keep edits independent)
        bool valid = edits.All(e => e.Start >= model.Bom && Independent(model, e));
        byte[]? preview = valid ? model.Preview(edits) : null;
        var batch = edits.Select(e => new ByteEdit(new(e.Start, e.Start + e.Deleted), e.Insert)).ToArray();
        foreach (var d in docs)
        {
            var result = d.Apply(batch);
            Assert.True(result.Applied == valid, $"step {step}: applied={result.Applied} expected={valid} edits={string.Join(";", edits.Select(e => $"{e.Start}+{e.Deleted}:{BitConverter.ToString(e.Insert)}"))} doc={BitConverter.ToString(model.Bytes)}");
            if (!valid) Assert.Equal(model.Bytes, d.ReadBytes(new(0, d.ByteLength)));
        }
        if (valid)
        {
            Assert.NotNull(preview);
            model.Apply(edits);
        }
        return valid;
    }

    private static bool Independent(NaiveModel model, (int Start, int Deleted, byte[] Insert) e)
    {
        var before = model.Boundaries();
        if (!before.Contains(e.Start) || !before.Contains(e.Start + e.Deleted)) return false;
        var single = model.Preview([e]);
        var after = NaiveModel.BoundariesOf(single);
        return after.Contains(e.Start) && after.Contains(e.Start + e.Insert.Length);
    }

    private static void Positions(Random r, NaiveModel model, Doc[] docs, bool all = false)
    {
        foreach (var d in docs) WaitIndex(d);
        var offsets = all ? Enumerable.Range(0, model.Bytes.Length + 1).ToList() : Enumerable.Range(0, 12).Select(_ => r.Next(model.Bytes.Length + 1)).ToList();
        var starts = model.LineStarts();
        foreach (var d in docs)
        {
            Assert.Equal(starts.Count, d.LineIndexState.Lines);
            foreach (int off in offsets)
            {
                bool ok = model.TryPosition(off, out var expected);
                Assert.True(ok == d.TryGetPosition(off, out var got), $"offset {off} doc={BitConverter.ToString(model.Bytes)}");
                if (!ok) continue;
                Assert.Equal(expected, got);
                Assert.True(d.TryGetOffset(got, out long back));
                Assert.Equal(off, back);
            }
        }
    }
}

public abstract partial class DocumentContractSuite
{
    [Theory]
    [InlineData(101)]
    [InlineData(102)]
    [InlineData(103)]
    [InlineData(104)]
    public void RandomByteEditsUndoAndPositionsMatchTheNaiveModel(int seed) => RandomizedParity.Run(seed, 2500, Factory);
}

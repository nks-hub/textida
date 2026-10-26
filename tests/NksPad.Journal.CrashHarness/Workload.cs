using System.Security.Cryptography;
using System.Text;
using NksPad.Core.Documents;
using NksPad.Core.Journal;
using NksPad.Document.Contract;
using Document = NksPad.Core.Documents.Document;

namespace CrashHarness;

/// <summary>
/// Three documents (unnamed reference, named reference, named paged) and a deterministic stream of random operations.
/// The child process runs the stream with a journal; the parent runs the same stream without one and uses the states it passes through as the oracle.
/// </summary>
public sealed class Scene : IDisposable
{
    public const int DocCount = 3;
    public static readonly string[] Backends = ["reference", "reference", "paged"];

    public string Dir { get; }
    public Document[] Docs { get; } = new Document[DocCount];
    public int[] Floor { get; } = new int[DocCount];
    /// <summary>True when undo/redo may have gone where a journal replay cannot follow exactly; bytes still match, undo steps may differ.</summary>
    public bool[] Loose { get; } = new bool[DocCount];
    public bool InUndoRedo { get; set; }
    /// <summary>Revision at the last save of each document; the journal's base revision after a save.</summary>
    public long[] SavedAt { get; } = new long[DocCount];
    public List<(int Op, string Hash)>[] FileStates { get; } = [[], [], []];
    public event Action<int>? DocChanged;

    private readonly string?[] _hashCache = new string?[DocCount];
    private readonly Random _text;

    private Scene(string dir, int seed)
    {
        Dir = dir;
        _text = new Random(seed ^ 0x5eed);
    }

    public string UnnamedTarget => Path.Combine(Dir, "unnamed-saved.txt");
    public string FilePathOf(int d) => Path.Combine(Dir, d == 1 ? "a.txt" : "b.txt");

    public static IBackendFactory FactoryOf(string name) => name == "paged" ? PagedBackendFactory.Instance : ReferenceBackendFactory.Instance;
    public static IBackendFactory? Resolve(string name) => name is "paged" or "reference" ? FactoryOf(name) : null;

    public static Scene Create(string dir, int seed)
    {
        Directory.CreateDirectory(dir);
        var scene = new Scene(dir, seed);
        File.WriteAllBytes(scene.FilePathOf(1), Gen.Text(scene._text, 20_000 + scene._text.Next(40_000)));
        File.WriteAllBytes(scene.FilePathOf(2), Gen.Text(scene._text, 100_000 + scene._text.Next(200_000)));
        scene.Docs[0] = Document.FromBytes(Gen.Text(scene._text, 2_000 + scene._text.Next(18_000)), ReferenceBackendFactory.Instance);
        scene.Docs[1] = Document.Open(scene.FilePathOf(1), ReferenceBackendFactory.Instance);
        scene.Docs[2] = Document.Open(scene.FilePathOf(2), PagedBackendFactory.Instance);
        for (int d = 0; d < DocCount; d++)
        {
            int idx = d;
            scene.Docs[d].Changed += (_, _) =>
            {
                scene._hashCache[idx] = null;
                if (!scene.InUndoRedo) scene.DocChanged?.Invoke(idx);
            };
            if (d > 0) scene.FileStates[d].Add((-1, Hex(File.ReadAllBytes(scene.FilePathOf(d)))));
        }
        return scene;
    }

    public static string Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public string StateHash()
    {
        var sb = new StringBuilder();
        for (int d = 0; d < DocCount; d++) sb.Append(DocHash(d)).Append('|');
        return sb.ToString();
    }

    private string DocHash(int d) => _hashCache[d] ??= $"{Docs[d].Revision}:{SavedAt[d]}:{Docs[d].Codec.Name}:{Hex(Docs[d].ReadBytes(new ByteRange(0, Docs[d].ByteLength)))}";

    /// <summary>The same hash for documents rebuilt by recovery (journal revision = base + document revision).</summary>
    public static string StateHash(IEnumerable<RecoveredDocument> docs) =>
        string.Concat(docs.Select(r => $"{r.BaseRevision + r.Document!.Revision}:{r.BaseRevision}:{r.Document.Codec.Name}:{Hex(r.Document.ReadBytes(new ByteRange(0, r.Document.ByteLength)))}|"));

    public void Dispose()
    {
        foreach (var d in Docs) d?.Dispose();
    }

    // ---- operations ----

    /// <summary>Runs operation <paramref name="op"/>; <paramref name="onSession"/> is the child's hook that stores the tab layout.</summary>
    public void RunOp(Random rng, int op, Action<int>? onSession)
    {
        int d = rng.Next(DocCount);
        var doc = Docs[d];
        int r = rng.Next(100);
        if (r < 30)
        {
            long at = Boundary(doc, rng);
            for (int i = 0, n = 1 + rng.Next(5); i < n; i++)
            {
                var bytes = Gen.Char(rng);
                if (!doc.Insert(at, bytes, new EditOptions { CoalesceKey = "type", Source = "user" }).Applied) break;
                at += bytes.Length;
            }
        }
        else if (r < 42) DeleteSome(doc, rng);
        else if (r < 52) Replace(doc, rng);
        else if (r < 58) MultiEdit(doc, rng);
        else if (r < 64)
        {
            using var group = doc.BeginUndoGroup();
            for (int i = 0, n = 2 + rng.Next(3); i < n; i++) doc.Insert(Boundary(doc, rng), Gen.Text(rng, 1 + rng.Next(30)));
        }
        else if (r < 76)
        {
            bool below = rng.Next(12) == 0;
            if (doc.CanUndo && (doc.UndoDepth > Floor[d] || below))
            {
                if (doc.UndoDepth <= Floor[d]) Loose[d] = true;
                InUndoRedo = true;
                try { doc.Undo(); }
                finally { InUndoRedo = false; }
                Notify(d);
            }
        }
        else if (r < 83)
        {
            if (doc.CanRedo)
            {
                InUndoRedo = true;
                try { doc.Redo(); }
                finally { InUndoRedo = false; }
                Notify(d);
            }
        }
        else if (r < 88) Save(d, op);
        else if (r < 91) onSession?.Invoke(op);
        else if (r < 93) SwitchCodec(doc, rng);
        else doc.Insert(Boundary(doc, rng), Gen.Text(rng, 20_000 + rng.Next(130_000)), new EditOptions { Source = "paste" });
    }

    /// <summary>Re-reads the bytes as another encoding (a view change), or converts UTF-8 to and from its BOM variant (an undoable edit).</summary>
    private void SwitchCodec(Document doc, Random rng)
    {
        var current = doc.Codec;
        if (rng.Next(2) == 0) doc.ReinterpretAs(current.Kind == NksPad.Core.Encodings.CodecKind.SingleByte ? NksPad.Core.Encodings.TextCodec.Utf8 : NksPad.Core.Encodings.TextCodec.Cp1250);
        else if (current.Kind == NksPad.Core.Encodings.CodecKind.Utf8) doc.ConvertTo(current.WithBom(current.Bom.Length == 0));
    }

    private void Notify(int d) => DocChanged?.Invoke(d);

    private void Save(int d, int op)
    {
        var doc = Docs[d];
        var result = d == 0 && doc.FilePath is null ? doc.Save(UnnamedTarget) : doc.Save();
        if (!result.Saved) throw new InvalidOperationException("Save of the oracle document conflicted.");
        Floor[d] = doc.UndoDepth;
        Loose[d] = doc.RedoDepth > 0;
        FileStates[d].Add((op, Hex(File.ReadAllBytes(doc.FilePath!))));
        SavedAt[d] = doc.Revision;
        _hashCache[d] = null;
        DocChanged?.Invoke(d);
    }

    private static long Boundary(Document doc, Random rng)
    {
        long at = doc.ByteLength == 0 ? 0 : rng.NextInt64(0, doc.ByteLength + 1);
        if (at < doc.BomLength) at = doc.BomLength;
        while (!doc.IsCharBoundary(at)) at++;
        return at;
    }

    private static ByteRange Range(Document doc, Random rng, int max)
    {
        long a = Boundary(doc, rng);
        long b = Math.Min(doc.ByteLength, a + rng.Next(max));
        while (!doc.IsCharBoundary(b)) b++;
        return new(a, b);
    }

    private static void DeleteSome(Document doc, Random rng)
    {
        var range = Range(doc, rng, 400);
        if (!range.IsEmpty) doc.Delete(range, new EditOptions { Source = "user" });
    }

    private static void Replace(Document doc, Random rng) => doc.Replace(Range(doc, rng, 200), Gen.Text(rng, 1 + rng.Next(80)));

    private static void MultiEdit(Document doc, Random rng)
    {
        var first = Range(doc, rng, 50);
        long at = first.End + rng.Next(100);
        if (at > doc.ByteLength) return;
        while (!doc.IsCharBoundary(at)) at++;
        var second = new ByteRange(at, at);
        doc.Apply([new ByteEdit(first, Gen.Text(rng, rng.Next(20))), new ByteEdit(second, Gen.Text(rng, 1 + rng.Next(20)))]);
    }
}

public static class Gen
{
    private static readonly string[] Pieces = ["alpha", " ", "beta", "\n", "\r\n", "ř", "č", "ž", "😀", "—", "x=1;", "\t", "Příliš žluťoučký kůň", "é", "漢字"];

    public static byte[] Char(Random rng) => Encoding.UTF8.GetBytes(Pieces[rng.Next(Pieces.Length)]);

    public static byte[] Text(Random rng, int approxBytes)
    {
        var bytes = new List<byte>(approxBytes + 16);
        while (bytes.Count < approxBytes) bytes.AddRange(Encoding.UTF8.GetBytes(Pieces[rng.Next(Pieces.Length)]));
        return [.. bytes];
    }
}

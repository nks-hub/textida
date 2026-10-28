using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P3.1.13: undo and redo survive a save over the open file of a 100 MiB document in the paged backend, for a stream save
/// (the length changed) and an in-place write (same length). The expected content comes from a separate chunk model, never from the document.
/// </summary>
[Trait("Category", "Large")]
public class PagedSaveUndoTests
{
    private const long FileSize = 100L << 20;
    private const int Edits = 5000;
    private const int Window = 500;

    /// <summary>Byte sequence kept as 64 KiB chunks so thousands of edits stay cheap.</summary>
    internal sealed class ChunkModel
    {
        private readonly List<byte[]> _chunks = [];

        public ChunkModel(string path)
        {
            using var fs = File.OpenRead(path);
            while (true)
            {
                var buf = new byte[1 << 16];
                int n = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
                if (n == 0) break;
                _chunks.Add(n == buf.Length ? buf : buf[..n]);
            }
        }

        private (int Chunk, int Offset) Locate(long pos)
        {
            for (int i = 0; i < _chunks.Count; i++)
            {
                if (pos < _chunks[i].Length || (pos == _chunks[i].Length && i == _chunks.Count - 1)) return (i, (int)pos);
                pos -= _chunks[i].Length;
            }
            throw new ArgumentOutOfRangeException(nameof(pos));
        }

        public void Replace(long pos, long deleteLength, byte[] insert)
        {
            var (ci, off) = Locate(pos);
            var tail = new List<byte>();
            long remaining = deleteLength;
            int last = ci;
            int cut = off;
            // collect what survives behind the deleted range, which may reach into following chunks
            while (true)
            {
                var c = _chunks[last];
                long take = Math.Min(remaining, c.Length - cut);
                remaining -= take;
                if (remaining == 0)
                {
                    tail.AddRange(c.AsSpan((int)(cut + take)).ToArray());
                    break;
                }
                last++;
                cut = 0;
            }
            var head = _chunks[ci].AsSpan(0, off).ToArray();
            _chunks.RemoveRange(ci, last - ci + 1);
            byte[] joined = [.. head, .. insert, .. tail];
            _chunks.InsertRange(ci, joined.Length == 0 ? [] : [joined]);
        }

        public string Sha256()
        {
            using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var c in _chunks) h.AppendData(c);
            return Convert.ToHexString(h.GetHashAndReset());
        }
    }

    internal static string DiskSha(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    internal static string DocSha(Doc d)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[1 << 20];
        for (long at = 0; at < d.ByteLength;)
        {
            int n = d.Read(at, buf);
            Assert.True(n > 0);
            h.AppendData(buf.AsSpan(0, n));
            at += n;
        }
        return Convert.ToHexString(h.GetHashAndReset());
    }

    internal static string Generate(string path, bool utf8Log)
    {
        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20))
        using (var w = new StreamWriter(fs, new UTF8Encoding(false), 1 << 20))
        {
            long written = 0;
            for (long n = 0; written < FileSize; n++)
            {
                string line = utf8Log
                    ? $"2026-10-26 12:{n % 60:D2}:{n * 7 % 60:D2} INFO příliš žluťoučký kůň #{n}{(n % 7 == 3 ? "\r\n" : "\n")}"
                    : $"line {n:D9} the quick brown fox jumps over the lazy dog {n * 2654435761 % 1000003}\n";
                w.Write(line);
                written += Encoding.UTF8.GetByteCount(line);
            }
        }
        return DiskSha(path);
    }

    /// <summary>
    /// One edit at a random line of the document, the same in the model: an insert of a tag (with a multi-byte character and a CR LF in the UTF-8 log), the deletion of a line, a
    /// replacement of four bytes by two, or, for <paramref name="sameLength"/>, four bytes over four that neither cut a character nor split a CR LF pair.
    /// </summary>
    internal static void Edit(Doc d, ChunkModel model, Random r, int i, bool sameLength, bool utf8Log)
    {
        long at, del;
        byte[] ins;
        byte[] none = [];
        while (true)
        {
            var head = new byte[400];
            long off = r.NextInt64(0, d.ByteLength - 800);
            int got = d.Read(off, head);
            long start = off + Array.IndexOf(head, (byte)'\n', 0, got) + 1;
            var line = new byte[400];
            int n = d.Read(start, line);
            int eol = Array.IndexOf(line, (byte)'\n', 0, n);
            Assert.True(eol >= 0);
            // a 4 byte overwrite must neither cut a character nor split a CR LF pair
            bool plain4 = line.Take(4).All(b => b is >= 0x20 and < 0x80);
            if (sameLength && !plain4) continue;
            string tag = utf8Log ? $"Ž{i}ě—\r\n" : $"X{i}\r\n";
            int kind = sameLength ? 3 : i % 3 == 2 && !plain4 ? 0 : i % 3;
            (at, del, ins) = kind switch
            {
                0 => (start, 0L, Encoding.UTF8.GetBytes(tag)),
                1 => (start, (long)eol + 1, none),
                2 => (start, 4L, Encoding.ASCII.GetBytes($"Q{i % 10}")),
                _ => (start, 4L, Encoding.ASCII.GetBytes($"{i % 10000:D4}")),
            };
            break;
        }
        Assert.True(d.Replace(new(at, at + del), ins).Applied);
        model.Replace(at, del, ins);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Undo_and_redo_survive_a_stream_save_and_an_in_place_write_of_a_100_MiB_file(bool utf8Log)
    {
        string dir = Directory.CreateTempSubdirectory("textida-p3113-").FullName;
        try
        {
            string path = Path.Combine(dir, "big.txt");
            string originalSha = Generate(path, utf8Log);
            var model = new ChunkModel(path);
            using var d = Doc.Open(path, new PagedBackendFactory(startIndexing: false), new() { Format = TextCodec.Utf8, ChangeLogCapacity = 8192 });
            var r = new Random(5);
            var checkpoints = new Dictionary<int, string>();

            void One(int i, bool sameLength) => Edit(d, model, r, i, sameLength, utf8Log);

            for (int i = 0; i < Edits; i++)
            {
                One(i, sameLength: false);
                if (i + 1 >= Edits - Window && (i + 1) % 50 == 0) checkpoints[i + 1] = model.Sha256();
            }
            Assert.Equal(Edits, d.UndoDepth);
            string revisionSha = model.Sha256();
            Assert.Equal(revisionSha, DocSha(d));

            // stream save over the open file
            GC.Collect();
            long before = Process.GetCurrentProcess().PrivateMemorySize64;
            Assert.Equal(SaveMode.Stream, d.PlanSave().Mode);
            Assert.True(d.Save().Saved);
            long grown = Process.GetCurrentProcess().PrivateMemorySize64 - before;
            if (!utf8Log) Assert.True(grown <= 350L << 20, $"private commit grew by {grown >> 20} MiB");
            Assert.Equal(revisionSha, DiskSha(path));
            Assert.False(d.IsModified);

            for (int k = 1; k <= Window; k++)
            {
                Assert.NotNull(d.Undo());
                if ((Edits - k) % 50 == 0) Assert.Equal(checkpoints[Edits - k], DocSha(d));
            }
            for (int k = 1; k <= Window; k++)
            {
                Assert.NotNull(d.Redo());
                if ((Edits - Window + k) % 50 == 0) Assert.Equal(checkpoints[Edits - Window + k], DocSha(d));
            }
            Assert.Equal(revisionSha, DocSha(d));
            while (d.CanUndo) d.Undo();
            Assert.Equal(originalSha, DocSha(d));
            Assert.True(d.IsModified);

            Assert.True(d.Save().Saved);
            Assert.Equal(originalSha, DiskSha(path));

            // same-length changes go in place, and undo still reaches the original afterwards
            model = new ChunkModel(path);
            for (int i = 0; i < Edits; i++) One(i, sameLength: true);
            string patchedSha = model.Sha256();
            Assert.Equal(SaveMode.InPlace, d.PlanSave().Mode);
            Assert.True(d.Save().Saved);
            Assert.Equal(patchedSha, DiskSha(path));
            for (int k = 0; k < Window; k++) d.Undo();
            for (int k = 0; k < Window; k++) d.Redo();
            Assert.Equal(patchedSha, DocSha(d));
            while (d.CanUndo) d.Undo();
            Assert.Equal(originalSha, DocSha(d));
            Assert.True(d.Save().Saved);
            Assert.Equal(originalSha, DiskSha(path));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}

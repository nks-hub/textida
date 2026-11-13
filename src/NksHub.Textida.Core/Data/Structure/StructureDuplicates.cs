using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace NksHub.Textida.Core.Data.Structure;

public static class StructureDuplicates
{
    private const int RecordSize = 32;
    private const int ChunkRecords = 4 * 1024 * 1024 / RecordSize;
    private const int StreamBufferSize = 64 * 1024;

    public static long Analyze(StructureIndex index, CancellationToken ct) =>
        Analyze(index, ct, ChunkRecords, 32, Path.GetTempPath());

    internal static long Analyze(StructureIndex index, CancellationToken ct, int recordsPerChunk, int mergeFanIn,
        string tempRoot, Func<StructureNode, (ulong, ulong)>? hashOverride = null, Action? mergedRecord = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (recordsPerChunk is < 1 or > ChunkRecords) throw new ArgumentOutOfRangeException(nameof(recordsPerChunk));
        if (mergeFanIn is < 2 or > 32) throw new ArgumentOutOfRangeException(nameof(mergeFanIn));
        ct.ThrowIfCancellationRequested();
        if (index.Format is not (StructureFormat.Json or StructureFormat.Jsonc)) return 0;

        long duplicates;
        using (var scratch = new ScratchFiles(tempRoot))
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var names = new StructureQuery.ValueReader(index, ct);
            var leftNames = new StructureQuery.ValueReader(index, ct);
            var rightNames = new StructureQuery.ValueReader(index, ct);
            var hashBuffer = new byte[StreamBufferSize];
            string queue = scratch.NewFile("queue");
            long runs = WriteRuns(index, ct, scratch, queue, recordsPerChunk, names, hash, hashBuffer, hashOverride);
            while (runs > 1)
            {
                ct.ThrowIfCancellationRequested();
                (queue, runs) = MergePass(scratch, queue, mergeFanIn, mergedRecord, ct);
            }

            duplicates = 0;
            if (runs != 0)
            {
                using var runNames = OpenQueue(scratch, queue);
                string run = runNames.ReadLine() ?? throw new InvalidDataException("Missing sorted name run.");
                duplicates = ReportDuplicates(index, scratch, run, leftNames, rightNames, ct);
            }
        }
        ct.ThrowIfCancellationRequested();
        return duplicates;
    }

    private readonly record struct Record(long Parent, ulong Hash0, ulong Hash1, long Node) : IComparable<Record>
    {
        public int CompareTo(Record other)
        {
            int order = Parent.CompareTo(other.Parent);
            if (order == 0) order = Hash0.CompareTo(other.Hash0);
            if (order == 0) order = Hash1.CompareTo(other.Hash1);
            return order == 0 ? Node.CompareTo(other.Node) : order;
        }

        public bool SameGroup(Record other) => Parent == other.Parent && Hash0 == other.Hash0 && Hash1 == other.Hash1;
    }

    private sealed class RecordComparer(CancellationToken ct) : IComparer<Record>
    {
        private int comparisons;

        public int Compare(Record left, Record right)
        {
            if ((++comparisons & 1023) == 0) ct.ThrowIfCancellationRequested();
            return left.CompareTo(right);
        }
    }

    private static long WriteRuns(StructureIndex index, CancellationToken ct, ScratchFiles scratch, string queue,
        int recordsPerChunk, StructureQuery.ValueReader names, IncrementalHash hash, byte[] buffer,
        Func<StructureNode, (ulong, ulong)>? hashOverride)
    {
        var records = new Record[recordsPerChunk];
        var comparer = new RecordComparer(ct);
        using var queueWriter = CreateQueue(scratch, queue);
        int count = 0;
        long runs = 0;
        for (long id = 0; id < index.Count; id++)
        {
            ct.ThrowIfCancellationRequested();
            StructureNode node = index.ReadNode(id);
            if (!node.HasName || node.ParentId < 0 || index.ReadNode(node.ParentId).Kind != StructureKind.JsonObject) continue;
            var (hash0, hash1) = hashOverride is null ? HashName(node, names, hash, buffer, ct) : hashOverride(node);
            records[count++] = new(node.ParentId, hash0, hash1, node.Id);
            if (count != records.Length) continue;
            WriteRun(scratch, records, count, comparer, queueWriter, ct);
            runs++;
            count = 0;
        }
        if (count != 0)
        {
            WriteRun(scratch, records, count, comparer, queueWriter, ct);
            runs++;
        }
        return runs;
    }

    private static (ulong, ulong) HashName(StructureNode node, StructureQuery.ValueReader names,
        IncrementalHash hash, byte[] buffer, CancellationToken ct)
    {
        int used = 0;
        foreach (int scalar in names.ReadName(node))
        {
            ct.ThrowIfCancellationRequested();
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(used, 4), scalar);
            used += 4;
            if (used != buffer.Length) continue;
            hash.AppendData(buffer);
            used = 0;
        }
        if (used != 0) hash.AppendData(buffer, 0, used);
        Span<byte> digest = stackalloc byte[32];
        if (!hash.TryGetHashAndReset(digest, out int written) || written != digest.Length)
            throw new CryptographicException("Could not hash a JSON member name.");
        return (BinaryPrimitives.ReadUInt64LittleEndian(digest), BinaryPrimitives.ReadUInt64LittleEndian(digest[8..]));
    }

    private static void WriteRun(ScratchFiles scratch, Record[] records, int count, RecordComparer comparer,
        StreamWriter queue, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try { Array.Sort(records, 0, count, comparer); }
        catch (InvalidOperationException error) when (error.InnerException is OperationCanceledException)
        {
            throw new OperationCanceledException(ct);
        }
        ct.ThrowIfCancellationRequested();
        string run = scratch.NewFile("run");
        using (var output = CreateRun(scratch, run))
            for (int i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                WriteRecord(output, records[i]);
            }
        queue.WriteLine(run);
    }

    private static (string Queue, long Runs) MergePass(ScratchFiles scratch, string queue, int fanIn,
        Action? mergedRecord, CancellationToken ct)
    {
        string nextQueue = scratch.NewFile("queue");
        long runs = 0;
        using (var input = OpenQueue(scratch, queue))
        using (var output = CreateQueue(scratch, nextQueue))
        {
            var group = new List<string>(fanIn);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                group.Clear();
                while (group.Count < fanIn && input.ReadLine() is { } run) group.Add(run);
                if (group.Count == 0) break;
                if (group.Count == 1) output.WriteLine(group[0]);
                else
                {
                    string merged = scratch.NewFile("run");
                    MergeRuns(scratch, group, merged, mergedRecord, ct);
                    output.WriteLine(merged);
                    foreach (string run in group) File.Delete(scratch.PathOf(run));
                }
                runs++;
            }
        }
        File.Delete(scratch.PathOf(queue));
        return (nextQueue, runs);
    }

    private static void MergeRuns(ScratchFiles scratch, List<string> group, string outputName,
        Action? mergedRecord, CancellationToken ct)
    {
        var readers = new List<RunReader>(group.Count);
        try
        {
            var pending = new PriorityQueue<int, Record>();
            foreach (string run in group)
            {
                ct.ThrowIfCancellationRequested();
                var reader = new RunReader(scratch.PathOf(run));
                readers.Add(reader);
                if (reader.MoveNext()) pending.Enqueue(readers.Count - 1, reader.Current);
            }
            using var output = CreateRun(scratch, outputName);
            while (pending.TryDequeue(out int readerId, out Record record))
            {
                ct.ThrowIfCancellationRequested();
                WriteRecord(output, record);
                mergedRecord?.Invoke();
                ct.ThrowIfCancellationRequested();
                RunReader reader = readers[readerId];
                if (reader.MoveNext()) pending.Enqueue(readerId, reader.Current);
            }
        }
        finally
        {
            foreach (RunReader reader in readers) reader.Dispose();
        }
    }

    private static long ReportDuplicates(StructureIndex index, ScratchFiles scratch, string run,
        StructureQuery.ValueReader leftNames, StructureQuery.ValueReader rightNames, CancellationToken ct)
    {
        using var input = new RunReader(scratch.PathOf(run));
        using var representatives = new FileStream(scratch.PathOf(scratch.NewFile("rep")), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None, StreamBufferSize, FileOptions.RandomAccess);
        Record? group = null;
        StructureNode? firstName = null;
        bool hasRepresentatives = false;
        long duplicates = 0;
        var idBuffer = new byte[8];
        while (input.MoveNext())
        {
            ct.ThrowIfCancellationRequested();
            Record record = input.Current;
            if (group is not { } previous || !record.SameGroup(previous))
            {
                if (hasRepresentatives) representatives.SetLength(0);
                hasRepresentatives = false;
                group = record;
                firstName = null;
                continue;
            }
            firstName ??= index.ReadNode(previous.Node);
            StructureNode node = index.ReadNode(record.Node);
            bool duplicate = NamesEqual(firstName.Value, node, leftNames, rightNames, ct);
            if (!duplicate && hasRepresentatives)
            {
                representatives.Position = 0;
                while (representatives.Position < representatives.Length)
                {
                    ct.ThrowIfCancellationRequested();
                    representatives.ReadExactly(idBuffer);
                    long representative = BinaryPrimitives.ReadInt64LittleEndian(idBuffer);
                    if (!NamesEqual(index.ReadNode(representative), node, leftNames, rightNames, ct)) continue;
                    duplicate = true;
                    break;
                }
            }
            if (duplicate)
            {
                duplicates++;
                index.Report(new(StructureIssueKind.DuplicateKey, node.NameStart, node.NameEnd, "Duplicate JSON member name."));
            }
            else
            {
                representatives.Position = representatives.Length;
                BinaryPrimitives.WriteInt64LittleEndian(idBuffer, record.Node);
                representatives.Write(idBuffer);
                hasRepresentatives = true;
            }
        }
        return duplicates;
    }

    private static bool NamesEqual(StructureNode left, StructureNode right, StructureQuery.ValueReader leftNames,
        StructureQuery.ValueReader rightNames, CancellationToken ct)
    {
        using var leftScalars = leftNames.ReadName(left).GetEnumerator();
        using var rightScalars = rightNames.ReadName(right).GetEnumerator();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            bool hasLeft = leftScalars.MoveNext();
            bool hasRight = rightScalars.MoveNext();
            if (!hasLeft || !hasRight) return hasLeft == hasRight;
            if (leftScalars.Current != rightScalars.Current) return false;
        }
    }

    private static void WriteRecord(FileStream output, Record record)
    {
        Span<byte> bytes = stackalloc byte[RecordSize];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, record.Parent);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[8..], record.Hash0);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[16..], record.Hash1);
        BinaryPrimitives.WriteInt64LittleEndian(bytes[24..], record.Node);
        output.Write(bytes);
    }

    private sealed class RunReader(string path) : IDisposable
    {
        private readonly FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            StreamBufferSize, FileOptions.SequentialScan);
        private readonly byte[] buffer = new byte[RecordSize];
        public Record Current { get; private set; }

        public bool MoveNext()
        {
            int read = stream.Read(buffer);
            if (read == 0) return false;
            stream.ReadExactly(buffer.AsSpan(read));
            Current = new(BinaryPrimitives.ReadInt64LittleEndian(buffer), BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(8)),
                BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(16)), BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(24)));
            return true;
        }

        public void Dispose() => stream.Dispose();
    }

    private static FileStream CreateRun(ScratchFiles scratch, string name) => new(scratch.PathOf(name), FileMode.CreateNew,
        FileAccess.Write, FileShare.None, StreamBufferSize, FileOptions.SequentialScan);

    private static StreamWriter CreateQueue(ScratchFiles scratch, string name) => new(CreateRun(scratch, name), Encoding.UTF8, 4096);

    private static StreamReader OpenQueue(ScratchFiles scratch, string name) => new(new FileStream(scratch.PathOf(name),
        FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, FileOptions.SequentialScan), Encoding.UTF8, true, 4096);

    private sealed class ScratchFiles : IDisposable
    {
        private readonly string directory;
        private readonly string manifestPath;
        private readonly StreamWriter manifest;
        private long sequence;

        public ScratchFiles(string tempRoot)
        {
            directory = Path.Combine(Path.GetFullPath(tempRoot), "textida-duplicates-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            manifestPath = Path.Combine(directory, "manifest.txt");
            FileStream? file = null;
            try
            {
                file = new(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                manifest = new(file, Encoding.UTF8);
            }
            catch
            {
                file?.Dispose();
                if (file is not null) File.Delete(manifestPath);
                Directory.Delete(directory, false);
                throw;
            }
        }

        public string NewFile(string extension)
        {
            string name = $"f{sequence++:D12}.{extension}";
            // Persist ownership before creating a file, including on cancellation paths.
            manifest.WriteLine(name);
            manifest.Flush();
            return name;
        }

        public string PathOf(string name)
        {
            if (name != Path.GetFileName(name) || name.IndexOfAny(['/', '\\']) >= 0 || name is "." or "..")
                throw new InvalidDataException("Invalid duplicate analysis scratch file name.");
            return Path.Combine(directory, name);
        }

        public void Dispose()
        {
            manifest.Dispose();
            using (var names = new StreamReader(manifestPath, Encoding.UTF8))
                while (names.ReadLine() is { } name) File.Delete(PathOf(name));
            File.Delete(manifestPath);
            Directory.Delete(directory, false);
        }
    }
}

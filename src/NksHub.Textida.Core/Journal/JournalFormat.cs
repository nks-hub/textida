using System.Buffers.Binary;

namespace NksHub.Textida.Core.Journal;

public enum RecordType : byte
{
    SessionState = 1,
    DocOpen = 2,
    DocSeed = 3,
    DocEdit = 4,
    DocUndo = 5,
    DocRedo = 6,
    DocSave = 7,
    DocClose = 8,
    DocSaveIntent = 9,
    DocCodec = 10,
    BatchBegin = 11,
    BatchFilePlan = 12,
    BatchFileIntent = 13,
    BatchFileDone = 14,
    BatchFileSkipped = 15,
    BatchEnd = 16,
    DocMove = 17,
    BatchOperationPlan = 18,
}

/// <summary>One record read from or written to a journal; <see cref="TimestampMs"/> is Unix time in milliseconds (0 in version 1 files).</summary>
public readonly record struct JournalRecord(long Seq, long TimestampMs, RecordType Type, byte[] Payload);

/// <summary>CRC-32 (IEEE 802.3, reflected), the checksum of every journal record.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = Build();

    private static uint[] Build()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    public const uint Initial = 0xFFFFFFFFu;
    public static uint Finish(uint crc) => ~crc;
}

/// <summary>
/// Binary layout of a journal file (see FORMAT.md next to this file). All integers are little endian.
/// File header: magic "NKSJRNL\0", u16 version, u16 flags, u32 CRC of the first 12 bytes.
/// Record: u32 payload length, u32 CRC, u8 type, u64 sequence, [i64 timestamp, version 2+], payload.
/// Version 3 differs from 2 only in the payload layout of edit records (a large inserted block may be a reference to a payload file).
/// The CRC covers every record byte except the CRC field itself.
/// </summary>
public static class JournalFormat
{
    public const int CurrentVersion = 3;
    public const int OldestReadableVersion = 1;
    public const int FileHeaderSize = 16;
    private static readonly byte[] Magic = "NKSJRNL\0"u8.ToArray();

    public static int RecordHeaderSize(int version) => version >= 2 ? 25 : 17;

    public static byte[] FileHeader(int version = CurrentVersion)
    {
        var h = new byte[FileHeaderSize];
        Magic.CopyTo(h, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(8), (ushort)version);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(12), Crc32.Finish(Crc32.Update(Crc32.Initial, h.AsSpan(0, 12))));
        return h;
    }

    /// <summary>Encodes one record; <paramref name="version"/> other than the current one exists for migration tests.</summary>
    public static byte[] EncodeRecord(long seq, long timestampMs, RecordType type, ReadOnlySpan<byte> payload, int version = CurrentVersion)
    {
        int header = RecordHeaderSize(version);
        var buf = new byte[header + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, (uint)payload.Length);
        buf[8] = (byte)type;
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(9), seq);
        if (version >= 2) BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(17), timestampMs);
        payload.CopyTo(buf.AsSpan(header));
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4), RecordCrc(buf));
        return buf;
    }

    internal static uint RecordCrc(ReadOnlySpan<byte> record)
    {
        uint crc = Crc32.Update(Crc32.Initial, record[..4]);
        return Crc32.Finish(Crc32.Update(crc, record[8..]));
    }

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

public enum JournalTail
{
    /// <summary>The file ends exactly after a valid record.</summary>
    Clean,
    /// <summary>The last record is cut short (power loss or kill during a write).</summary>
    Truncated,
    /// <summary>A record fails its checksum, has a wrong sequence number or an unknown type; it and everything after it is ignored.</summary>
    Corrupt,
}

public sealed class JournalFormatException(string message) : Exception(message);

/// <summary>Reads records in order and stops at the first one that is not intact. Never throws for damaged records, only for an unusable file header.</summary>
public sealed class JournalReader : IDisposable
{
    private readonly FileStream _file;
    private readonly byte[] _header = new byte[25];
    private long _expectedSeq = 1;

    public JournalReader(string path)
    {
        _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
        TotalLength = _file.Length;
        var h = new byte[JournalFormat.FileHeaderSize];
        if (_file.Read(h) < h.Length) throw new JournalFormatException("The journal file header is incomplete.");
        if (!h.AsSpan(0, 8).SequenceEqual("NKSJRNL\0"u8)) throw new JournalFormatException("Not a journal file.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(12)) != Crc32.Finish(Crc32.Update(Crc32.Initial, h.AsSpan(0, 12))))
            throw new JournalFormatException("The journal file header is damaged.");
        Version = BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(8));
        if (Version is < JournalFormat.OldestReadableVersion or > JournalFormat.CurrentVersion)
            throw new JournalFormatException($"Journal format version {Version} is not supported (readable: {JournalFormat.OldestReadableVersion}-{JournalFormat.CurrentVersion}).");
        ValidLength = JournalFormat.FileHeaderSize;
    }

    public int Version { get; }
    public long TotalLength { get; }
    /// <summary>Length of the intact prefix read so far.</summary>
    public long ValidLength { get; private set; }
    public JournalTail Tail { get; private set; } = JournalTail.Clean;
    public long RecordCount { get; private set; }

    public bool TryRead(out JournalRecord record)
    {
        record = default;
        if (Tail != JournalTail.Clean) return false;
        int headerSize = JournalFormat.RecordHeaderSize(Version);
        long remaining = TotalLength - ValidLength;
        if (remaining == 0) return false;
        if (remaining < headerSize) return Stop(JournalTail.Truncated);

        _file.ReadExactly(_header.AsSpan(0, headerSize));
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(_header);
        if (length > remaining - headerSize) return Stop(JournalTail.Truncated);

        var buf = new byte[headerSize + length];
        _header.AsSpan(0, headerSize).CopyTo(buf);
        _file.ReadExactly(buf.AsSpan(headerSize));
        var type = (RecordType)buf[8];
        long seq = BinaryPrimitives.ReadInt64LittleEndian(buf.AsSpan(9));
        if (BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(4)) != JournalFormat.RecordCrc(buf) || seq != _expectedSeq || type is < RecordType.SessionState or > RecordType.BatchOperationPlan)
            return Stop(JournalTail.Corrupt);

        long ts = Version >= 2 ? BinaryPrimitives.ReadInt64LittleEndian(buf.AsSpan(17)) : 0;
        record = new(seq, ts, type, buf[headerSize..]);
        _expectedSeq++;
        RecordCount++;
        ValidLength += buf.Length;
        return true;
    }

    private bool Stop(JournalTail tail)
    {
        Tail = tail;
        return false;
    }

    public void Dispose() => _file.Dispose();
}

public sealed class JournalOptions
{
    /// <summary>How the writer makes records durable; see <see cref="FsyncPolicy"/>.</summary>
    public FsyncPolicy Fsync { get; init; } = FsyncPolicy.Interval;
    /// <summary>Longest time a record waits in memory. Must not exceed <see cref="MaxFlushInterval"/> so the 2 s loss limit holds.</summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    /// <summary>A flush happens at once when this much is waiting.</summary>
    public int MaxBufferedBytes { get; init; } = 1 << 20;
    /// <summary>The journal is compacted when it grows past this size (and past twice its size after the last compaction).</summary>
    public long CompactThresholdBytes { get; init; } = 16L << 20;

    public static readonly TimeSpan MaxFlushInterval = TimeSpan.FromSeconds(1);
}

public enum FsyncPolicy
{
    /// <summary>Every record is written and fsynced before the call returns.</summary>
    EveryRecord,
    /// <summary>Records are batched and written with one fsync every <see cref="JournalOptions.FlushInterval"/>.</summary>
    Interval,
    /// <summary>Only <see cref="JournalWriter.Flush"/> and disposal write; for tests.</summary>
    Manual,
}

/// <summary>Append-only writer of one journal file. Thread safe. A failed write stops the writer for good (<see cref="Fault"/>): appending after a torn write would hide later records.</summary>
public sealed class JournalWriter : IDisposable
{
    private readonly JournalOptions _options;
    private readonly object _gate = new();
    private readonly object _io = new();
    private readonly FileStream _file;
    private readonly Thread? _flusher;
    private readonly ManualResetEventSlim _stop = new();
    private MemoryStream _pending = new();
    private long _seq;
    private bool _disposed;

    public JournalWriter(string path, JournalOptions? options = null)
    {
        _options = options ?? new();
        if (_options.FlushInterval > JournalOptions.MaxFlushInterval || _options.FlushInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), $"The flush interval must be in (0, {JournalOptions.MaxFlushInterval.TotalSeconds} s] to keep the loss limit.");
        Path = path;
        _file = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.Read | FileShare.Delete,
            BufferSize = 0,
        });
        _file.Write(JournalFormat.FileHeader());
        _file.Flush(true);
        Length = JournalFormat.FileHeaderSize;
        if (_options.Fsync == FsyncPolicy.Interval)
        {
            // a thread of its own: a busy or starved thread pool (parallel scans, map builds) must not hold back the 2 s loss limit
            _flusher = new Thread(FlushLoop) { IsBackground = true, Name = "Journal flush", Priority = ThreadPriority.AboveNormal };
            _flusher.Start();
        }
    }

    private void FlushLoop()
    {
        while (!_stop.Wait(_options.FlushInterval)) Flush();
    }

    public string Path { get; internal set; }

    /// <summary>
    /// Runs before every write of records, inside the same failure handling: what the records refer to (payload files) is made durable first.
    /// A record that refers to data is therefore never on disk before the data is.
    /// </summary>
    public Action? BeforeWrite { get; set; }
    /// <summary>Bytes durably written so far.</summary>
    public long Length { get; private set; }
    /// <summary>Set when a write failed; the journal no longer protects the session.</summary>
    public Exception? Fault { get; private set; }
    public event Action<Exception>? Faulted;

    public void Append(RecordType type, ReadOnlySpan<byte> payload, long? timestampMs = null)
    {
        lock (_gate)
        {
            if (_disposed || Fault is not null) return;
            _pending.Write(JournalFormat.EncodeRecord(++_seq, timestampMs ?? JournalFormat.NowMs(), type, payload));
            if (_options.Fsync == FsyncPolicy.Manual || (_options.Fsync == FsyncPolicy.Interval && _pending.Length < _options.MaxBufferedBytes)) return;
        }
        Flush();
    }

    /// <summary>Writes everything appended so far and fsyncs it.</summary>
    public void Flush()
    {
        lock (_io)
        {
            MemoryStream batch;
            lock (_gate)
            {
                if (_disposed || _pending.Length == 0 || Fault is not null) return;
                batch = _pending;
                _pending = new();
            }
            try
            {
                BeforeWrite?.Invoke();
                _file.Write(batch.GetBuffer(), 0, (int)batch.Length);
                _file.Flush(true);
                Length += batch.Length;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ObjectDisposedException)
            {
                Fault = e;
                Faulted?.Invoke(e);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _stop.Set();
        if (_flusher is not null && _flusher != Thread.CurrentThread) _flusher.Join();
        lock (_io)
        {
            MemoryStream batch;
            lock (_gate) batch = _pending;
            try
            {
                if (Fault is null && batch.Length > 0)
                {
                    BeforeWrite?.Invoke();
                    _file.Write(batch.GetBuffer(), 0, (int)batch.Length);
                    _file.Flush(true);
                    Length += batch.Length;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Fault = e;
            }
            _file.Dispose();
        }
    }
}

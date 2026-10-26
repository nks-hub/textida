using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Documents;

/// <summary>
/// A document with an identity, a revision number and byte-exact content kept by an <see cref="IByteBackend"/>.
/// Positions are byte offsets in a given revision, ranges are half-open. Every change (edit, undo, redo, typing in a
/// text control) creates a new revision and a change log entry, so ranges of older revisions can be mapped forward or rejected.
/// Not thread safe, except snapshots and <see cref="CheckExternalChange"/>.
/// </summary>
public sealed partial class Document : IDisposable
{
    private readonly int _logCapacity;
    private readonly List<ChangeSet> _log = [];
    private readonly List<WeakReference<Anchor>> _anchors = [];
    private IByteBackend _backend;
    private TextCodec _codec;
    private int _bomLength;
    private long _revision;
    private DocumentSnapshot? _snapshot;
    private bool _disposed;

    private Document(IByteBackend backend, DocumentOptions? options, string? path, FileFingerprint? fingerprint, bool unsaved)
    {
        _backend = backend;
        _codec = backend.Codec;
        _bomLength = BomOf(backend, _codec);
        _logCapacity = Math.Max(1, options?.ChangeLogCapacity ?? new DocumentOptions().ChangeLogCapacity);
        FilePath = path;
        Fingerprint = fingerprint;
        _savedUnreachable = unsaved;
        _backend.EditedByBackend += OnBackendEdit;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public long Revision => _revision;
    public long ByteLength => _backend.ByteLength;
    /// <summary>How the bytes are read as text. Changes through <see cref="ReinterpretAs"/>, <see cref="ConvertTo"/> and their undo.</summary>
    public TextCodec Codec => _codec;
    /// <summary>Bytes at the start that are a byte order mark of <see cref="Codec"/> and not text.</summary>
    public int BomLength => _bomLength;
    public BackendCapabilities Capabilities => _backend.Capabilities;
    public IByteBackend Backend => _backend;
    public event EventHandler<DocumentChangedEventArgs>? Changed;

    /// <summary>Creates an unnamed document over a copy of the bytes. It counts as unsaved until it is saved.</summary>
    public static Document FromBytes(ReadOnlyMemory<byte> bytes, IBackendFactory factory, DocumentOptions? options = null)
    {
        var format = options?.Format ?? EncodingGuesser.Guess(bytes.Span[..Math.Min(bytes.Length, SampleSize)], bytes.Length <= SampleSize).Codec;
        return new Document(factory.Create(bytes, format), options, null, null, unsaved: true);
    }

    /// <summary>Opens a file and records its fingerprint; the backend decides whether the file is mapped or read.</summary>
    public static Document Open(string path, IBackendFactory factory, DocumentOptions? options = null)
    {
        path = Path.GetFullPath(path);
        FileSwap.RecoverAbandoned(path);
        InPlacePatch.Recover(path);
        var format = options?.Format ?? DetectFile(path);
        var time = File.GetLastWriteTimeUtc(path);
        var backend = factory.OpenFile(path, format);
        try
        {
            var print = new FileFingerprint(backend.ByteLength, time, HashBackend(backend));
            return new Document(backend, options, path, print, unsaved: false);
        }
        catch
        {
            backend.Dispose();
            throw;
        }
    }

    private const int SampleSize = 1 << 20;

    private static TextCodec DetectFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var sample = new byte[(int)Math.Min(fs.Length, SampleSize)];
        fs.ReadExactly(sample);
        return EncodingGuesser.Guess(sample, fs.Length <= SampleSize).Codec;
    }

    private static string HashBackend(IByteBackend backend)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var buf = new byte[1 << 20];
        for (long at = 0; at < backend.ByteLength;)
        {
            int n = backend.Read(at, buf);
            hash.AppendData(buf, 0, n);
            at += n;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // ---- reading ----

    public int Read(long offset, Span<byte> destination)
    {
        if (offset < 0 || offset > ByteLength) throw new ArgumentOutOfRangeException(nameof(offset));
        return _backend.Read(offset, destination);
    }

    public byte[] ReadBytes(ByteRange range)
    {
        if (range.End > ByteLength) throw new ArgumentOutOfRangeException(nameof(range));
        if (range.Length > Array.MaxLength) throw new NotSupportedException("A range over 2 GiB cannot be read into one array.");
        var bytes = new byte[range.Length];
        _backend.Read(range.Start, bytes);
        return bytes;
    }

    /// <summary>Text view of a range; both ends must be character boundaries inside the text (not in the BOM).</summary>
    public string ReadText(ByteRange range)
    {
        if (range.End > ByteLength || range.Start < _bomLength) throw new ArgumentOutOfRangeException(nameof(range));
        if (!IsCharBoundary(range.Start) || !IsCharBoundary(range.End)) throw new ArgumentException("The range does not fall on character boundaries.", nameof(range));
        return _backend.ReadText(range);
    }

    public bool IsCharBoundary(long offset)
    {
        if (offset < _bomLength || offset > ByteLength) return false;
        if (offset == _bomLength || offset == ByteLength) return true;
        var (window, pos) = Window(offset, 4, 4);
        return _codec.IsBoundary(window, pos);
    }

    private (byte[] Window, int Pos) Window(long offset, int before, int after)
    {
        long start = Math.Max(_bomLength, offset - before);
        start -= (start - _bomLength) % _codec.Alignment;
        long end = Math.Min(ByteLength, offset + after);
        var window = new byte[end - start];
        _backend.Read(start, window);
        return (window, (int)(offset - start));
    }

    // ---- positions ----

    public (long Lines, bool Complete) LineIndexState => _backend.LineIndexState;

    /// <summary>Line and UTF-16 column of an offset; false when the offset is not a position (not a boundary, inside CRLF, in the BOM) or the line index does not know it yet.</summary>
    public bool TryGetPosition(long offset, out LinePosition position)
    {
        position = default;
        if (!IsCharBoundary(offset)) return false;
        var (window, pos) = Window(offset, 4, 4);
        return !_codec.IsInsideCrLf(window, pos) && _backend.TryGetPosition(offset, out position);
    }

    public bool TryGetOffset(LinePosition position, out long offset) => _backend.TryGetOffset(position, out offset);

    /// <summary>
    /// True when the bytes between two offsets hold no CR or LF byte, so both are on one line. Only a short stretch is read, and false means
    /// "unknown" (a line break, or too far apart): the caller then asks <see cref="TryGetPosition"/>, which can cost a scan of the line index.
    /// </summary>
    public bool NoLineBreakBetween(long a, long b)
    {
        long from = Math.Min(a, b), to = Math.Max(a, b);
        if (from < 0 || to > ByteLength || to - from > 4096) return false;
        Span<byte> bytes = stackalloc byte[(int)(to - from)];
        return Read(from, bytes) == bytes.Length && bytes.IndexOfAny((byte)10, (byte)13) < 0;
    }

    // ---- snapshots ----

    /// <summary>Immutable bytes of the current revision for reading on other threads.</summary>
    public DocumentSnapshot GetSnapshot()
    {
        if (_snapshot is { } s && s.Revision == _revision) return s;
        return _snapshot = new DocumentSnapshot(Id, _revision, _backend.CreateSnapshot(), _codec);
    }

    // ---- backend switch ----

    /// <summary>
    /// Moves the content to another backend in memory. Identity, revision, change log, undo, anchors and the saved state stay;
    /// nothing is written to disk. Snapshots taken before stay readable. <paramref name="backendCodec"/> is how the new backend reads
    /// newlines when the document codec is one it cannot hold (the hex view of a UTF-16 file); the document keeps its own codec.
    /// </summary>
    public void SwitchBackend(IBackendFactory factory, TextCodec? backendCodec = null)
    {
        if (_saving) throw new InvalidOperationException("The document cannot change its backend while it is being saved.");
        if (ByteLength > Array.MaxLength) throw new NotSupportedException("Switching a document over 2 GiB is not supported.");
        var bytes = new byte[ByteLength];
        _backend.Read(0, bytes);
        var next = factory.Create(bytes, backendCodec ?? _codec);
        _backend.EditedByBackend -= OnBackendEdit;
        var old = _backend;
        _backend = next;
        _bomLength = BomOf(next, _codec);
        _backend.EditedByBackend += OnBackendEdit;
        old.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatching();
        _backend.EditedByBackend -= OnBackendEdit;
        _backend.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

/// <summary>
/// Content of one revision, unchanged by document edits and saves; reads are safe from any thread.
/// See <see cref="IByteSnapshot"/> for external-file invalidation and its detection limits.
/// </summary>
public sealed class DocumentSnapshot
{
    private readonly IByteSnapshot _bytes;
    private readonly TextCodec _codec;

    internal DocumentSnapshot(Guid documentId, long revision, IByteSnapshot bytes, TextCodec codec)
    {
        DocumentId = documentId;
        Revision = revision;
        _bytes = bytes;
        _codec = codec;
    }

    public Guid DocumentId { get; }
    public long Revision { get; }
    public long ByteLength => _bytes.ByteLength;
    /// <summary>How the bytes of this revision are read as text.</summary>
    public TextCodec Codec => _codec;
    public int Read(long offset, Span<byte> destination) => _bytes.Read(offset, destination);

    public byte[] ReadBytes(ByteRange range)
    {
        if (range.End > ByteLength) throw new ArgumentOutOfRangeException(nameof(range));
        var bytes = new byte[checked((int)range.Length)];
        _bytes.Read(range.Start, bytes);
        return bytes;
    }

    /// <summary>Decodes a range whose ends are character boundaries of this revision.</summary>
    public string ReadText(ByteRange range) => _codec.GetString(ReadBytes(range));
}

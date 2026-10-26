using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Documents;

/// <summary>
/// Storage under a <see cref="Document"/>. The bytes are the authority; the text is only a view decoded by <see cref="TextCodec"/>.
/// <see cref="Document"/> validates ranges and character boundaries before it calls <see cref="Replace"/>, so a backend may trust its arguments.
/// There is deliberately no whole-text member: a paged backend must never build one.
/// </summary>
public interface IByteBackend : IDisposable
{
    BackendCapabilities Capabilities { get; }
    TextCodec Codec { get; }
    long ByteLength { get; }
    /// <summary>File the backend currently maps, or null.</summary>
    string? BackingPath { get; }
    /// <summary>Raised when the backend itself changed the bytes (the user typed into a text control); not raised by <see cref="Replace"/>.</summary>
    event Action<BackendEdit>? EditedByBackend;

    int Read(long offset, Span<byte> destination);
    void Replace(long offset, long deleteLength, ReadOnlySpan<byte> insert);
    /// <summary>Text of a range whose ends are character boundaries.</summary>
    string ReadText(ByteRange range);
    /// <summary>
    /// Shows the same bytes through another codec: nothing is written and the length is unchanged. A backend that cannot hold the
    /// codec throws <see cref="NotSupportedException"/> before it changes anything.
    /// </summary>
    void Reinterpret(TextCodec codec);
    /// <summary>False when <see cref="Reinterpret"/> would refuse the codec (the paged backend scans newlines per byte, so no UTF-16/32).</summary>
    bool Supports(TextCodec codec);

    /// <summary>Line count so far and whether the index covers the whole document.</summary>
    (long Lines, bool Complete) LineIndexState { get; }
    /// <summary>False when the position is unknown (line index incomplete) or invalid; never a guess.</summary>
    bool TryGetPosition(long offset, out LinePosition position);
    bool TryGetOffset(LinePosition position, out long offset);

    IByteSnapshot CreateSnapshot();
    void CopyTo(Stream destination);
    /// <summary>
    /// <paramref name="tempPath"/> holds exactly the current bytes. Move it over <paramref name="targetPath"/> atomically;
    /// a backend that maps the target must release the mapping first and map the result afterwards.
    /// </summary>
    void ReplaceBackingFile(string tempPath, string targetPath);
}

/// <summary>Immutable bytes of one revision; reads are safe from any thread.</summary>
public interface IByteSnapshot
{
    long ByteLength { get; }
    int Read(long offset, Span<byte> destination);
}

/// <summary>Creates backends over bytes, so a document can move between backends in memory.</summary>
public interface IBackendFactory
{
    BackendCapabilities Capabilities { get; }
    IByteBackend Create(ReadOnlyMemory<byte> bytes, TextCodec codec);
    /// <summary>Opens a file; the default reads it whole.</summary>
    IByteBackend OpenFile(string path, TextCodec codec) => Create(File.ReadAllBytes(path), codec);
    /// <summary>True when <see cref="OpenFile"/> maps the file instead of reading it.</summary>
    bool MapsFiles => false;
}

public static class FileSwap
{
    /// <summary>Atomic replace of <paramref name="target"/> by <paramref name="temp"/>.</summary>
    public static void Replace(string temp, string target)
    {
        // a virus scanner or an indexer may hold a file for a moment; the swap is atomic, so trying again is safe
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(target)) File.Replace(temp, target, null);
                else File.Move(temp, target);
                return;
            }
            catch (IOException) when (attempt < 5 && File.Exists(temp))
            {
                Thread.Sleep(20 << attempt);
            }
        }
    }
}

/// <summary>Copy of the bytes taken at snapshot time.</summary>
public sealed class ArraySnapshot(byte[] bytes) : IByteSnapshot
{
    public long ByteLength => bytes.Length;

    public int Read(long offset, Span<byte> destination)
    {
        if (offset < 0 || offset > bytes.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        int n = (int)Math.Min(destination.Length, bytes.Length - offset);
        bytes.AsSpan((int)offset, n).CopyTo(destination);
        return n;
    }
}

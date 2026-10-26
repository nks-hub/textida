namespace NksHub.Textida.Core;

/// <summary>Read-only paged view of a large text file addressed by byte offsets.</summary>
public interface IPagedTextSource : IDisposable
{
    long ByteLength { get; }
    Encodings.TextCodec Codec { get; }
    /// <summary>Known line count so far and whether indexing finished.</summary>
    (long Lines, bool Complete) LineIndexState { get; }
    event EventHandler? IndexProgress;
    /// <summary>Decoded text of up to <paramref name="maxLines"/> lines starting at a byte offset aligned to a line start.</summary>
    ValueTask<IReadOnlyList<string>> ReadLinesAtAsync(long byteOffset, int maxLines, CancellationToken ct = default);
    /// <summary>Byte offset of a line start; available only for lines already indexed.</summary>
    bool TryGetLineStart(long line, out long byteOffset);
}

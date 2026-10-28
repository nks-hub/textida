using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The byte backend over a file that is read whole into memory. The picture viewer opens files with it up to its size limit, so nothing keeps the file mapped and another program can overwrite or
/// truncate it while the picture is shown; the document sees the change through its fingerprint like any other file. A larger file is mapped by the paged backend, because it is never decoded.
/// </summary>
internal sealed class ResidentBytesFactory : IBackendFactory
{
    private static readonly PagedBackendFactory Inner = new(startIndexing: false);

    public static ResidentBytesFactory Instance { get; } = new();

    public BackendCapabilities Capabilities => Inner.Capabilities;

    public IByteBackend Create(ReadOnlyMemory<byte> bytes, TextCodec codec) => Inner.Create(bytes, codec);

    public IByteBackend OpenFile(string path, TextCodec codec)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return Inner.Create(bytes, codec);
    }
}

using System.Diagnostics;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Document.Contract;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// Paged backend whose original is a sparse file of 4.5 GiB: the test bytes are written at its start and the NUL filler behind
/// them is cut off, so the contract runs over a piece tree whose original is larger than 4 GiB.
/// </summary>
internal sealed class BigFileBackendFactory : IBackendFactory
{
    public const long FileSize = 4_500_000_000;
    private static readonly string Dir = Directory.CreateTempSubdirectory("textida-big-").FullName;
    private static int _counter;

    static BigFileBackendFactory()
    {
        // files of undisposed documents stay mapped at exit and cannot be deleted then: sweep the leftovers of earlier runs
        foreach (var old in Directory.GetDirectories(Path.GetTempPath(), "textida-big-*").Where(d => d != Dir))
            try { Directory.Delete(old, recursive: true); } catch (IOException) { }
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            // the finalizers of the undisposed documents unmap their files; without this the files of a whole run stay behind and fill the disk
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try { Directory.Delete(Dir, recursive: true); } catch (IOException) { }
        };
    }

    public static BigFileBackendFactory Instance { get; } = new();
    public BackendCapabilities Capabilities => PagedBackendFactory.Instance.Capabilities;

    /// <summary>Creates a sparse file of <see cref="FileSize"/> bytes and returns it open for writing.</summary>
    public static FileStream CreateSparse(string path)
    {
        File.Create(path).Dispose();
        if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo("fsutil", $"sparse setflag \"{path}\"") { CreateNoWindow = true, UseShellExecute = false })!.WaitForExit();
        var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        fs.SetLength(FileSize);
        return fs;
    }

    public static string NewPath() => Path.Combine(Dir, $"big{Interlocked.Increment(ref _counter)}.bin");

    public IByteBackend Create(ReadOnlyMemory<byte> bytes, TextCodec codec)
    {
        string path = NewPath();
        using (var fs = CreateSparse(path)) fs.Write(bytes.Span);
        var backend = PagedBackend.FromFile(path, codec);
        backend.Replace(bytes.Length, FileSize - bytes.Length, []);
        // the contract tests never dispose their documents, and every mapped 4.5 GiB file keeps its disk space charged: only the latest few stay open
        lock (_open)
        {
            _open.Enqueue((backend, path));
            while (_open.Count > 3)
            {
                var (old, oldPath) = _open.Dequeue();
                old.Dispose();
                try { File.Delete(oldPath); } catch (IOException) { }
            }
        }
        return backend;
    }

    private static readonly Queue<(IByteBackend Backend, string Path)> _open = new();
}

[Trait("Category", "Large")]
public class BigFileContractTests : DocumentContractSuite
{
    protected override IBackendFactory Factory => BigFileBackendFactory.Instance;
}

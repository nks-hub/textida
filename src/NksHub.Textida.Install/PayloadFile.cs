using System.Buffers.Binary;

namespace NksHub.Textida.Install;

/// <summary>What an appended ZIP holds: the package the setup installs, or the native libraries the setup window needs to start.</summary>
public enum PayloadKind { Package, Natives }

/// <summary>
/// The ZIPs inside TextidaSetup.exe. Each is appended to the program and followed by a trailer of 16 bytes: the marker of its kind (<c>TXSETUP1</c> for the package, <c>TXNATIVE</c> for the native libraries)
/// and the length of the ZIP, 64 bit little endian. The trailer of the last one ends the file, the one before it ends just in front of the last ZIP, and so on.
/// A program loader ignores bytes after the last section, so the program runs as it is. The copy that stays in the install folder as the uninstaller is the program without the package.
/// </summary>
public static class PayloadFile
{
    public const int TrailerLength = 16;

    private static ReadOnlySpan<byte> Marker(PayloadKind kind) => kind == PayloadKind.Package ? "TXSETUP1"u8 : "TXNATIVE"u8;

    private readonly record struct Blob(PayloadKind Kind, long Offset, long Length);

    /// <summary>Writes <paramref name="output"/>: the program, the ZIPs of other kinds it already carries, and <paramref name="zip"/> as the ZIP of <paramref name="kind"/>, which replaces one of that kind.</summary>
    public static void Append(string program, string zip, string output, PayloadKind kind = PayloadKind.Package)
    {
        using var source = new FileStream(program, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var target = new FileStream(output, FileMode.Create, FileAccess.Write);
        using var added = File.OpenRead(zip);
        Rewrite(source, target, keep => keep != kind, (kind, added));
    }

    /// <summary>The ZIP of that kind, positioned at its start, or null when the program carries none. The caller disposes it.</summary>
    public static Stream? Open(string program, PayloadKind kind = PayloadKind.Package)
    {
        var file = new FileStream(program, FileMode.Open, FileAccess.Read, FileShare.Read);
        foreach (var blob in Read(file))
            if (blob.Kind == kind) return new SliceStream(file, blob.Offset, blob.Length);
        file.Dispose();
        return null;
    }

    public static bool HasPayload(string program, PayloadKind kind = PayloadKind.Package)
    {
        using var file = new FileStream(program, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(file).Any(b => b.Kind == kind);
    }

    /// <summary>Copies the program without its package; the native libraries it carries stay, because the uninstaller needs them to show its window. A program that carries none is copied whole.</summary>
    public static void CopyWithoutPayload(string program, string destination)
    {
        using var source = new FileStream(program, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var target = new FileStream(destination, FileMode.Create, FileAccess.Write);
        Rewrite(source, target, kind => kind != PayloadKind.Package, null);
    }

    private static void Rewrite(FileStream source, Stream target, Func<PayloadKind, bool> keep, (PayloadKind Kind, Stream Zip)? add)
    {
        var blobs = Read(source);
        var programLength = blobs.Count == 0 ? source.Length : blobs.Min(b => b.Offset);
        Copy(source, target, 0, programLength);
        foreach (var blob in blobs.OrderBy(b => b.Offset).Where(b => keep(b.Kind)))
        {
            Copy(source, target, blob.Offset, blob.Length);
            WriteTrailer(target, blob.Kind, blob.Length);
        }
        if (add is { } extra)
        {
            extra.Zip.CopyTo(target);
            WriteTrailer(target, extra.Kind, extra.Zip.Length);
        }
    }

    /// <summary>The ZIPs in the file, from the last to the first; stops at the first trailer that is missing or does not fit.</summary>
    private static List<Blob> Read(FileStream file)
    {
        var blobs = new List<Blob>();
        var end = file.Length;
        Span<byte> trailer = stackalloc byte[TrailerLength];
        while (end >= TrailerLength)
        {
            file.Seek(end - TrailerLength, SeekOrigin.Begin);
            file.ReadExactly(trailer);
            PayloadKind? kind = trailer[..8].SequenceEqual(Marker(PayloadKind.Package)) ? PayloadKind.Package : trailer[..8].SequenceEqual(Marker(PayloadKind.Natives)) ? PayloadKind.Natives : null;
            var length = BinaryPrimitives.ReadInt64LittleEndian(trailer[8..]);
            var offset = end - TrailerLength - length;
            if (kind is null || length <= 0 || offset <= 0) break;
            blobs.Add(new Blob(kind.Value, offset, length));
            end = offset;
        }
        file.Seek(0, SeekOrigin.Begin);
        return blobs;
    }

    private static void WriteTrailer(Stream target, PayloadKind kind, long length)
    {
        target.Write(Marker(kind));
        Span<byte> size = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(size, length);
        target.Write(size);
    }

    private static void Copy(Stream source, Stream target, long offset, long count)
    {
        source.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[81920];
        while (count > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read == 0) throw new EndOfStreamException();
            target.Write(buffer, 0, read);
            count -= read;
        }
    }

    /// <summary>A read-only window on a part of a file, so a ZIP reader sees the package as a file of its own.</summary>
    private sealed class SliceStream(Stream inner, long offset, long length) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

        public override int Read(byte[] buffer, int start, int count) => Read(buffer.AsSpan(start, count));

        public override int Read(Span<byte> buffer)
        {
            var remaining = length - _position;
            if (remaining <= 0 || buffer.Length == 0) return 0;
            inner.Position = offset + _position;
            var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
            _position += read;
            return read;
        }

        public override long Seek(long distance, SeekOrigin origin)
        {
            var target = origin switch { SeekOrigin.Begin => distance, SeekOrigin.Current => _position + distance, _ => length + distance };
            if (target < 0) throw new IOException("Seek before the start of the package.");
            return _position = target;
        }

        public override void Flush() { }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int start, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}

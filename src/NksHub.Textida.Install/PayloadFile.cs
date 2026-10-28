using System.Buffers.Binary;

namespace NksHub.Textida.Install;

/// <summary>
/// The package inside TextidaSetup.exe: the ZIP of the Windows package is appended to the program, followed by a trailer of 16 bytes (the marker <c>TXSETUP1</c> and the length of the ZIP, 64 bit little endian).
/// A program loader ignores bytes after the last section, so the program runs as it is. The copy that stays in the install folder as the uninstaller is the program without the package.
/// </summary>
public static class PayloadFile
{
    public const int TrailerLength = 16;

    private static ReadOnlySpan<byte> Marker => "TXSETUP1"u8;

    /// <summary>Writes <paramref name="output"/>: the program (without a package it may already carry), the ZIP and the trailer.</summary>
    public static void Append(string program, string package, string output)
    {
        using var target = new FileStream(output, FileMode.Create, FileAccess.Write);
        using (var source = new FileStream(program, FileMode.Open, FileAccess.Read, FileShare.Read)) CopyHead(source, target, ProgramLength(source));
        long length;
        using (var zip = File.OpenRead(package))
        {
            zip.CopyTo(target);
            length = zip.Length;
        }
        target.Write(Marker);
        Span<byte> size = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(size, length);
        target.Write(size);
    }

    /// <summary>The ZIP of the program, positioned at its start, or null when the program carries none. The caller disposes it.</summary>
    public static Stream? Open(string program)
    {
        var file = new FileStream(program, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Locate(file) is not var (offset, length)) { file.Dispose(); return null; }
        return new SliceStream(file, offset, length);
    }

    public static bool HasPayload(string program)
    {
        using var file = new FileStream(program, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Locate(file) is not null;
    }

    /// <summary>Copies the program without the package; a program that carries none is copied whole.</summary>
    public static void CopyWithoutPayload(string program, string destination)
    {
        using var source = new FileStream(program, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var target = new FileStream(destination, FileMode.Create, FileAccess.Write);
        CopyHead(source, target, ProgramLength(source));
    }

    private static long ProgramLength(FileStream file) => Locate(file) is var (offset, _) ? offset : file.Length;

    private static (long Offset, long Length)? Locate(Stream file)
    {
        if (file.Length < TrailerLength) return null;
        file.Seek(-TrailerLength, SeekOrigin.End);
        Span<byte> trailer = stackalloc byte[TrailerLength];
        file.ReadExactly(trailer);
        file.Seek(0, SeekOrigin.Begin);
        if (!trailer[..8].SequenceEqual(Marker)) return null;
        var length = BinaryPrimitives.ReadInt64LittleEndian(trailer[8..]);
        var offset = file.Length - TrailerLength - length;
        return length > 0 && offset > 0 ? (offset, length) : null;
    }

    private static void CopyHead(Stream source, Stream target, long count)
    {
        source.Seek(0, SeekOrigin.Begin);
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

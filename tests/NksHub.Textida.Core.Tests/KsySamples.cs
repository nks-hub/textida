using System.Buffers.Binary;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex.Ksy;

namespace NksHub.Textida.Core.Tests;

/// <summary>Files for the pinned templates: a valid one and a damaged one of each format, built byte by byte, and a ZIP of ten gigabytes that exists only as a formula.</summary>
internal static class KsySamples
{
    // ---- BMP ----

    /// <summary>A 54-byte BMP header: width 3, height -2, 24 bits, compression bitfields.</summary>
    public static byte[] Bmp()
    {
        var file = new byte[54];
        file[0] = (byte)'B'; file[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(2), 54);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(10), 54);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(18), 3);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22), -2);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(26), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(28), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(30), 3);
        return file;
    }

    // ---- PNG ----

    public static byte[] PngChunk(string type, byte[] body)
    {
        var bytes = new byte[12 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)body.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        body.CopyTo(bytes, 8);
        return bytes;
    }

    /// <summary>
    /// The signature (8 bytes) and five chunks: IHDR (13 bytes of data, 25 in all) at 8, gAMA (4, 16 in all) at 33, tEXt ("Title", a zero and "Hello", 11, 23 in all) at 49, IDAT (3, 15 in all) at 72 and IEND (12) at 87; 99 bytes.
    /// </summary>
    public static byte[] Png()
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, 640);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), 480);
        ihdr[8] = 8;
        ihdr[9] = 6;
        var gamma = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(gamma, 45455);
        return [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. PngChunk("IHDR", ihdr), .. PngChunk("gAMA", gamma), .. PngChunk("tEXt", "Title\0Hello"u8.ToArray()), .. PngChunk("IDAT", [1, 2, 3]), .. PngChunk("IEND", [])];
    }

    // ---- ZIP ----

    public static byte[] ZipLocal(string name, byte[] body, ushort flags = 0)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(new byte[] { (byte)'P', (byte)'K', 3, 4 });
        w.Write((ushort)20); w.Write(flags); w.Write((ushort)8); w.Write((ushort)0x6000); w.Write((ushort)0x5821);
        w.Write(0xDEADBEEFu); w.Write((uint)body.Length); w.Write((uint)body.Length);
        w.Write((ushort)name.Length); w.Write((ushort)0);
        w.Write(Encoding.UTF8.GetBytes(name));
        w.Write(body);
        return ms.ToArray();
    }

    /// <summary>A local entry whose two size fields are <paramref name="sizeField"/> and that carries <paramref name="extra"/>; with 0xFFFFFFFF the real sizes are in a Zip64 extra field.</summary>
    public static byte[] ZipLocalWithExtra(string name, byte[] body, byte[] extra, uint sizeField)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(new byte[] { (byte)'P', (byte)'K', 3, 4 });
        w.Write((ushort)45); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0x6000); w.Write((ushort)0x5821);
        w.Write(0xDEADBEEFu); w.Write(sizeField); w.Write(sizeField);
        w.Write((ushort)name.Length); w.Write((ushort)extra.Length);
        w.Write(Encoding.UTF8.GetBytes(name));
        w.Write(extra);
        w.Write(body);
        return ms.ToArray();
    }

    /// <summary>The Zip64 extended information field of a local header: id 1, 16 bytes, the uncompressed and the compressed size.</summary>
    public static byte[] Zip64Extra(ulong uncompressed, ulong compressed)
    {
        var extra = new byte[20];
        BinaryPrimitives.WriteUInt16LittleEndian(extra, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(extra.AsSpan(2), 16);
        BinaryPrimitives.WriteUInt64LittleEndian(extra.AsSpan(4), uncompressed);
        BinaryPrimitives.WriteUInt64LittleEndian(extra.AsSpan(12), compressed);
        return extra;
    }

    public static byte[] ZipCentral(string name, uint localOffset, uint size)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(new byte[] { (byte)'P', (byte)'K', 1, 2 });
        w.Write((ushort)63); w.Write((ushort)20); w.Write((ushort)0); w.Write((ushort)8); w.Write((ushort)0x6000); w.Write((ushort)0x5821);
        w.Write(0xDEADBEEFu); w.Write(size); w.Write(size);
        w.Write((ushort)name.Length); w.Write((ushort)0); w.Write((ushort)0);
        w.Write((ushort)0); w.Write((ushort)0); w.Write(0u); w.Write(localOffset);
        w.Write(Encoding.UTF8.GetBytes(name));
        return ms.ToArray();
    }

    public static byte[] ZipEnd(ushort entries, uint directorySize, uint directoryOffset)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(new byte[] { (byte)'P', (byte)'K', 5, 6 });
        w.Write((ushort)0); w.Write((ushort)0); w.Write(entries); w.Write(entries);
        w.Write(directorySize); w.Write(directoryOffset); w.Write((ushort)0);
        return ms.ToArray();
    }

    /// <summary>One entry "a.txt" with the five bytes "hello": local header 40 bytes, central entry 51 bytes, end record 22 bytes; 113 in all.</summary>
    public static byte[] Zip() => [.. ZipLocal("a.txt", "hello"u8.ToArray()), .. ZipCentral("a.txt", 0, 5), .. ZipEnd(1, 51, 40)];

    // ---- PE ----

    /// <summary>
    /// A PE32+ image for amd64 with two sections: the PE header at 64, the optional header (240 bytes, 16 data directories) at 88, the section table at 328 and the raw data at 512 (16 bytes) and 528 (8 bytes); 536 bytes.
    /// </summary>
    public static byte[] Pe()
    {
        var file = new byte[536];
        file[0] = (byte)'M'; file[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(60), 64);
        "PE\0\0"u8.CopyTo(file.AsSpan(64));
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(68), 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(70), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(72), 0x5F000000);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(84), 240);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(86), 0x0022); // executable image, large address aware
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(88), 0x020B);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(104), 0x1000);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(112), 0x140000000);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(120), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(124), 0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(156), 3);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(160), 0x100000);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(196), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(200 + 8), 0x2000); // data directory 1: virtual address
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(200 + 12), 0x50);
        ".text\0\0\0"u8.CopyTo(file.AsSpan(328));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(328 + 8), 0x10);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(328 + 12), 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(328 + 16), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(328 + 20), 512);
        ".data\0\0\0"u8.CopyTo(file.AsSpan(368));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(368 + 8), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(368 + 12), 0x2000);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(368 + 16), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(368 + 20), 528);
        return file;
    }

    // ---- ELF ----

    /// <summary>
    /// A 64-bit little-endian ELF for x86-64: header at 16, one program header at 64 (56 bytes), two section headers at 120 (64 bytes each, the first one unused) and the dynamic section of the second one at 248
    /// (two entries of 16 bytes, the second is the end mark); 280 bytes.
    /// </summary>
    public static byte[] Elf64()
    {
        var file = new byte[280];
        new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 3 }.CopyTo(file, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(16), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(18), 62);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(24), 0x401000);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(32), 64);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(40), 120);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(52), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(54), 56);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(56), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(58), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(60), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(64), 1);      // PT_LOAD
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(68), 5);      // flags
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(104), 256);   // memory size
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(184 + 4), 6); // section 1: DYNAMIC
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(184 + 24), 248);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(184 + 32), 32);
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(248), 1);     // DT_NEEDED
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(256), 5);
        return file;
    }

    /// <summary>A 32-bit big-endian ELF for PowerPC: header at 16 (52 bytes in all), one program header at 52 (32 bytes), no sections; 84 bytes.</summary>
    public static byte[] Elf32Be()
    {
        var file = new byte[84];
        new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 1, 2, 1 }.CopyTo(file, 0);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(16), 2);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(18), 20);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(24), 0x10000000);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(28), 52);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(40), 52);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(42), 32);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(44), 1);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(46), 40);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(52), 1);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(72), 84);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(76), 5);
        return file;
    }

    // ---- STL ----

    /// <summary>A binary STL with two triangles: header 80 bytes, the count at 80, triangles at 84 and 134 (50 bytes each); 184 bytes.</summary>
    public static byte[] Stl()
    {
        var file = new byte[184];
        "solid test"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(80), 2);
        BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(84 + 8), 1f);      // normal z of the first triangle
        BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(84 + 12 + 12), 1.5f); // x of the second vertex
        BinaryPrimitives.WriteSingleLittleEndian(file.AsSpan(134 + 12), -2.25f); // x of the first vertex of the second triangle
        return file;
    }

    /// <summary>A binary STL of <paramref name="triangles"/> triangles that only exists as a formula: the count in the header, zeros for the rest.</summary>
    public sealed class BigStl(uint triangles) : IByteSnapshot
    {
        public long ByteLength => 84 + 50L * triangles;

        public int Read(long offset, Span<byte> destination)
        {
            int n = (int)Math.Min(destination.Length, Math.Max(0, ByteLength - offset));
            destination[..n].Clear();
            if (offset < 84)
            {
                var header = new byte[84];
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(80), triangles);
                header.AsSpan((int)offset, (int)Math.Min(84 - offset, n)).CopyTo(destination);
            }
            return n;
        }
    }

    // ---- a ZIP of ten gigabytes ----

    /// <summary>
    /// A ZIP that has <c>entries</c> entries of equal size, a central directory, a Zip64 end record and its locator, and the end record, about ten gigabytes in all. The headers are computed when asked for and the
    /// bodies read as zeros, so it takes no memory and no disk.
    /// </summary>
    public sealed class BigZip : IByteSnapshot
    {
        private const int NameLength = 10;
        private readonly int _entries;
        private readonly long _bodyLength;
        private readonly long _localSpan;
        private readonly long _centralStart;
        private const int CentralSpan = 4 + 42 + NameLength;
        private const int Zip64Span = 4 + 8 + 44;
        private const int LocatorSpan = 4 + 4 + 8 + 4;
        private const int EndSpan = 22;

        public BigZip(int entries = 4_000, long totalBytes = 10L << 30)
        {
            _entries = entries;
            long overhead = (long)entries * CentralSpan + Zip64Span + LocatorSpan + EndSpan;
            _bodyLength = (totalBytes - overhead + entries - 1) / entries - (4 + 26 + NameLength);
            _localSpan = 4 + 26 + NameLength + _bodyLength;
            _centralStart = _localSpan * entries;
            ByteLength = _centralStart + overhead;
        }

        public long ByteLength { get; }

        public long BodyLength => _bodyLength;

        public int Entries => _entries;

        private static string NameOf(int i) => "f" + i.ToString("D5") + ".bin";

        public int Read(long offset, Span<byte> destination)
        {
            int n = (int)Math.Min(destination.Length, Math.Max(0, ByteLength - offset));
            destination[..n].Clear();
            if (n == 0) return 0;
            long end = offset + n;
            if (offset < _centralStart)
                for (long i = offset / _localSpan; i < _entries && i * _localSpan < end; i++)
                    Overlay(destination, offset, i * _localSpan, LocalHeader((int)i));
            if (end > _centralStart)
                for (long i = Math.Max(0, offset - _centralStart) / CentralSpan; i < _entries && _centralStart + i * CentralSpan < end; i++)
                    Overlay(destination, offset, _centralStart + i * CentralSpan, ZipCentral(NameOf((int)i), (uint)Math.Min(i * _localSpan, uint.MaxValue), (uint)Math.Min(_bodyLength, uint.MaxValue)));
            long tail = _centralStart + (long)_entries * CentralSpan;
            if (end > tail)
            {
                var zip64 = new byte[Zip64Span];
                "PK"u8.CopyTo(zip64);
                BinaryPrimitives.WriteUInt64LittleEndian(zip64.AsSpan(4), 44);
                Overlay(destination, offset, tail, zip64);
                var locator = new byte[LocatorSpan];
                "PK"u8.CopyTo(locator);
                BinaryPrimitives.WriteUInt64LittleEndian(locator.AsSpan(8), (ulong)tail);
                Overlay(destination, offset, tail + Zip64Span, locator);
                Overlay(destination, offset, tail + Zip64Span + LocatorSpan, ZipEnd(0xFFFF, 0xFFFFFFFF, 0xFFFFFFFF));
            }
            return n;
        }

        private byte[] LocalHeader(int i)
        {
            var header = ZipLocal(NameOf(i), []);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(18), (uint)Math.Min(_bodyLength, uint.MaxValue));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(22), (uint)Math.Min(_bodyLength, uint.MaxValue));
            return header;
        }

        private static void Overlay(Span<byte> destination, long offset, long at, byte[] piece)
        {
            long from = Math.Max(offset, at), to = Math.Min(offset + destination.Length, at + piece.Length);
            if (from < to) piece.AsSpan((int)(from - at), (int)(to - from)).CopyTo(destination[(int)(from - offset)..]);
        }
    }

    // ---- reading a tree ----

    /// <summary>One line per node, <c>name=value@start+length</c>, children indented, to the given depth (a node with an error shows its kind in place of the value).</summary>
    public static string Dump(KsyNode node, int depth = 1, string indent = "")
    {
        var sb = new StringBuilder();
        sb.Append(indent).Append(node.Name).Append('=').Append(node.Error is { } e ? "!" + e.Kind : node.Value).Append('@').Append(node.Start).Append('+').Append(node.Length).Append('\n');
        if (depth > 0 && node.HasChildren)
            foreach (var child in node.Children) sb.Append(Dump(child, depth - 1, indent + "  "));
        return sb.ToString();
    }

    public static KsyNode Read(string pinnedId, byte[] data, KsyLimits? limits = null) =>
        new KsyReader(new ArraySnapshot(data), KsyCatalog.All.Single(p => p.Id == pinnedId).Template, limits).Root;
}

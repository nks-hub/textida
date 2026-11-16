using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Data.Structure;

public sealed class StructureCharReader
{
    private readonly IByteSnapshot source;
    private readonly TextCodec codec;
    private readonly CancellationToken cancellationToken;
    private readonly Action<long>? progress;
    private readonly byte[] buffer = new byte[64 * 1024];
    private long bufferStart = -1;
    private int bufferLength;
    private int untilCancellationCheck;
    private LegacyWindow? legacyWindow;

    private sealed class LegacyWindow
    {
        private const int Size = 8192;
        public readonly int[] Scalars = new int[Size];
        public readonly ushort[] ByteEnds = new ushort[Size];
        public readonly char[] Chars = new char[Size];
        public long Start;
        public int Count;
        public int Index;

        public bool TryRead(long at, out int scalar, out int bytes)
        {
            scalar = bytes = 0;
            long relative = at - Start;
            if (Count == 0 || relative < 0 || relative >= ByteEnds[Count - 1]) return false;
            int start = Index == 0 ? 0 : ByteEnds[Index - 1];
            if (relative != start)
            {
                if (relative == 0) Index = 0;
                else if (relative == ByteEnds[Index] && Index + 1 < Count) Index++;
                else
                {
                    int previous = Array.BinarySearch(ByteEnds, 0, Count, (ushort)relative);
                    if (previous < 0 || previous + 1 == Count) return false;
                    Index = previous + 1;
                }
            }
            start = Index == 0 ? 0 : ByteEnds[Index - 1];
            scalar = Scalars[Index];
            bytes = ByteEnds[Index] - start;
            return true;
        }
    }

    public StructureCharReader(IByteSnapshot source, TextCodec codec, int bomLength, CancellationToken cancellationToken, Action<long>? progress = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bomLength);
        if (bomLength > source.ByteLength) throw new ArgumentOutOfRangeException(nameof(bomLength));
        this.source = source;
        this.codec = codec;
        this.cancellationToken = cancellationToken;
        this.progress = progress;
        Position = bomLength;
    }

    public long Position { get; private set; }
    public long Length => source.ByteLength;

    public void Seek(long byteOffset)
    {
        if (byteOffset < 0 || byteOffset > source.ByteLength) throw new ArgumentOutOfRangeException(nameof(byteOffset));
        cancellationToken.ThrowIfCancellationRequested();
        Position = byteOffset;
    }

    public int Peek(int lookAhead = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lookAhead);
        long at = Position;
        int value;
        do
        {
            value = Decode(at, out int bytes);
            if (value < 0) return -1;
            at += bytes;
        } while (lookAhead-- > 0);
        return value;
    }

    public int Read()
    {
        if (untilCancellationCheck-- <= 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            untilCancellationCheck = 4095;
            progress?.Invoke(Position);
        }
        int value = Decode(Position, out int bytes);
        Position += bytes;
        return value;
    }

    public bool Consume(char value)
    {
        if (Peek() != value) return false;
        Read();
        return true;
    }

    public bool StartsWith(string ascii)
    {
        for (int i = 0; i < ascii.Length; i++) if (Peek(i) != ascii[i]) return false;
        return true;
    }

    public void SkipWhitespace()
    {
        while (Peek() is ' ' or '\t' or '\r' or '\n') Read();
    }

    private int Decode(long at, out int bytes)
    {
        bytes = 0;
        if (at >= source.ByteLength) return -1;
        if (bufferStart < 0 || at < bufferStart || at + Math.Min(8L, source.ByteLength - at) > bufferStart + bufferLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A decoded tail depends on the source bytes available before this refill.
            if (legacyWindow is not null) legacyWindow.Count = 0;
            bufferStart = at;
            bufferLength = source.Read(at, buffer);
            if (bufferLength == 0) throw new EndOfStreamException("The source snapshot ended before its declared length.");
            int minimum = (int)Math.Min(8L, source.ByteLength - at);
            while (bufferLength < minimum)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = source.Read(at + bufferLength, buffer.AsSpan(bufferLength));
                if (read == 0) throw new EndOfStreamException("The source snapshot ended inside an encoded character.");
                bufferLength += read;
            }
        }
        var data = buffer.AsSpan(checked((int)(at - bufferStart)), bufferLength - checked((int)(at - bufferStart)));
        if (codec.Kind == CodecKind.Utf8)
        {
            if (data[0] < 128) { bytes = 1; return data[0]; }
            if (Rune.DecodeFromUtf8(data, out var rune, out bytes) == OperationStatus.Done) return rune.Value;
            throw BadEncoding(at);
        }
        if (codec.Kind == CodecKind.Utf16)
        {
            if (data.Length < 2) throw BadEncoding(at);
            ushort unit = codec.BigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data) : BinaryPrimitives.ReadUInt16LittleEndian(data);
            bytes = 2;
            if (!char.IsSurrogate((char)unit)) return unit;
            if (!char.IsHighSurrogate((char)unit) || data.Length < 4) throw BadEncoding(at);
            ushort low = codec.BigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data[2..]) : BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
            if (!char.IsLowSurrogate((char)low)) throw BadEncoding(at);
            bytes = 4;
            return char.ConvertToUtf32((char)unit, (char)low);
        }
        if (codec.Kind == CodecKind.Utf32)
        {
            if (data.Length < 4) throw BadEncoding(at);
            uint value = codec.BigEndian ? BinaryPrimitives.ReadUInt32BigEndian(data) : BinaryPrimitives.ReadUInt32LittleEndian(data);
            if (value > int.MaxValue || !Rune.IsValid((int)value)) throw BadEncoding(at);
            bytes = 4;
            return (int)value;
        }
        int scalar;
        if (codec.Kind == CodecKind.SingleByte || data[0] < 0x80)
            scalar = codec.ReadLegacyScalar(data, out bytes);
        else
        {
            legacyWindow ??= new LegacyWindow();
            if (!legacyWindow.TryRead(at, out scalar, out bytes))
            {
                cancellationToken.ThrowIfCancellationRequested();
                legacyWindow.Count = 0;
                legacyWindow.Start = at;
                legacyWindow.Count = codec.ReadLegacyBlock(data, legacyWindow.Scalars, legacyWindow.ByteEnds,
                    legacyWindow.Chars, cancellationToken);
                legacyWindow.Index = 0;
                if (!legacyWindow.TryRead(at, out scalar, out bytes)) throw BadEncoding(at);
            }
        }
        return scalar >= 0 ? scalar : throw BadEncoding(at);
    }

    private static StructureSyntaxException BadEncoding(long at) => new(StructureIssueKind.Encoding, at, at + 1, "Invalid encoded character.");
}

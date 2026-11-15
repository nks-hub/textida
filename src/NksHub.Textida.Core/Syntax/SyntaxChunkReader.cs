using System.Buffers.Binary;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Syntax;

internal readonly record struct SyntaxChunk(string Text, bool LineEnd, bool DocumentEnd);

internal sealed class SyntaxChunkReader(DocumentSnapshot snapshot, long offset, byte[]? borrowedBuffer = null)
{
    internal const int WindowBytes = 32 << 10;
    internal const int BufferBytes = WindowBytes + 8;
    private readonly byte[] buffer = borrowedBuffer is null ? new byte[BufferBytes]
        : borrowedBuffer.Length >= BufferBytes ? borrowedBuffer
        : throw new ArgumentException("The syntax byte buffer is too small.", nameof(borrowedBuffer));
    private long bufferStart = offset;
    private int position;
    private int length;

    public long Offset => bufferStart + position;

    public SyntaxChunk Read()
    {
        var bytes = Next(out bool lineEnd, out bool documentEnd);
        return new(snapshot.Codec.GetString(bytes), lineEnd, documentEnd);
    }

    public int ReadInto(Span<char> destination, out bool lineEnd, out bool documentEnd) =>
        snapshot.Codec.GetChars(Next(out lineEnd, out documentEnd), destination);

    private ReadOnlySpan<byte> Next(out bool lineEnd, out bool documentEnd)
    {
        lineEnd = documentEnd = false;
        if (length - position < 8 && bufferStart + length < snapshot.ByteLength) Fill();
        if (Offset == snapshot.ByteLength)
        {
            lineEnd = documentEnd = true;
            return [];
        }
        var available = buffer.AsSpan(position, length - position);
        var codec = snapshot.Codec;
        int width = codec.Kind == CodecKind.Utf16 ? 2 : codec.Kind == CodecKind.Utf32 ? 4 : 1;
        int end = FindTerminator(available, width, codec.BigEndian);
        if (end >= 0)
        {
            int terminator = width;
            if (Unit(available[end..], width, codec.BigEndian) == '\r')
            {
                if (end + 2 * width > available.Length && bufferStart + length < snapshot.ByteLength)
                {
                    position += end;
                    return available[..end];
                }
                if (end + 2 * width <= available.Length && Unit(available[(end + width)..], width, codec.BigEndian) == '\n')
                    terminator += width;
            }
            position += end + terminator;
            lineEnd = true;
            return available[..end];
        }

        bool final = bufferStart + length == snapshot.ByteLength;
        int take = final ? available.Length : codec.CompleteLength(available);
        if (take == 0)
        {
            Fill();
            return Next(out lineEnd, out documentEnd);
        }
        position += take;
        lineEnd = documentEnd = final;
        return available[..take];
    }

    private void Fill()
    {
        int kept = length - position;
        buffer.AsSpan(position, kept).CopyTo(buffer);
        bufferStart += position;
        position = 0;
        length = kept;
        int wanted = (int)Math.Min(BufferBytes - kept, snapshot.ByteLength - bufferStart - kept);
        while (wanted > 0)
        {
            int read = snapshot.Read(bufferStart + length, buffer.AsSpan(length, wanted));
            if (read <= 0) throw new InvalidOperationException("The syntax snapshot ended before its declared length.");
            length += read;
            wanted -= read;
        }
    }

    private static int FindTerminator(ReadOnlySpan<byte> bytes, int width, bool bigEndian)
    {
        if (width == 1) return bytes.IndexOfAny((byte)'\r', (byte)'\n');
        for (int i = 0; i + width <= bytes.Length; i += width)
            if (Unit(bytes[i..], width, bigEndian) is '\r' or '\n') return i;
        return -1;
    }

    private static uint Unit(ReadOnlySpan<byte> bytes, int width, bool bigEndian) => width switch
    {
        1 => bytes[0],
        2 => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes),
        _ => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes),
    };
}

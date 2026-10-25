using System.Text;
using System.Text.Unicode;

namespace NksPad.Core;

public enum EncodingKind { Ascii, Utf8, Utf16LE, Utf16BE, Utf32LE, Utf32BE, SingleByte }

/// <summary>Outcome of <see cref="EncodingDetector.Detect"/>.</summary>
public readonly record struct DetectedEncoding(Encoding Encoding, EncodingKind Kind, int BomLength);

/// <summary>Encoding detection: BOM, UTF-8 validity, UTF-16 without BOM, single-byte fallback.</summary>
public static class EncodingDetector
{
    /// <summary>Code page used when the data is neither valid UTF-8 nor UTF-16.</summary>
    public const int FallbackCodePage = 1250;

    /// <param name="sample">Leading bytes of the file.</param>
    /// <param name="wholeFile">True when <paramref name="sample"/> is the entire file; otherwise a truncated trailing UTF-8 sequence is ignored.</param>
    public static DetectedEncoding Detect(ReadOnlySpan<byte> sample, bool wholeFile)
    {
        if (sample.StartsWith("﻿"u8)) return new(new UTF8Encoding(true), EncodingKind.Utf8, 3);
        if (sample.Length >= 4 && sample[0] == 0xFF && sample[1] == 0xFE && sample[2] == 0 && sample[3] == 0) return new(new UTF32Encoding(false, true), EncodingKind.Utf32LE, 4);
        if (sample.Length >= 4 && sample[0] == 0 && sample[1] == 0 && sample[2] == 0xFE && sample[3] == 0xFF) return new(new UTF32Encoding(true, true), EncodingKind.Utf32BE, 4);
        if (sample.Length >= 2 && sample[0] == 0xFF && sample[1] == 0xFE) return new(new UnicodeEncoding(false, true), EncodingKind.Utf16LE, 2);
        if (sample.Length >= 2 && sample[0] == 0xFE && sample[1] == 0xFF) return new(new UnicodeEncoding(true, true), EncodingKind.Utf16BE, 2);

        var utf16 = GuessUtf16WithoutBom(sample);
        if (utf16 is { } k)
            return new(new UnicodeEncoding(k == EncodingKind.Utf16BE, false), k, 0);

        if (Ascii.IsValid(sample)) return new(new UTF8Encoding(false), EncodingKind.Ascii, 0);
        var checkSpan = wholeFile ? sample : sample[..Utf8Validator.CompleteLength(sample)];
        if (Utf8.IsValid(checkSpan)) return new(new UTF8Encoding(false), EncodingKind.Utf8, 0);
        return new(Encoding.GetEncoding(FallbackCodePage), EncodingKind.SingleByte, 0);
    }

    private static EncodingKind? GuessUtf16WithoutBom(ReadOnlySpan<byte> s)
    {
        int n = Math.Min(s.Length, 8192) & ~1;
        if (n < 16) return null;
        int zeroEven = 0, zeroOdd = 0;
        for (int i = 0; i < n; i += 2)
        {
            if (s[i] == 0) zeroEven++;
            if (s[i + 1] == 0) zeroOdd++;
        }
        int half = n / 2;
        if (zeroOdd > half * 0.3 && zeroEven == 0) return EncodingKind.Utf16LE;
        if (zeroEven > half * 0.3 && zeroOdd == 0) return EncodingKind.Utf16BE;
        return null;
    }
}

/// <summary>UTF-8 validation that is safe to split into blocks (a character is never cut between two blocks).</summary>
public static class Utf8Validator
{
    /// <summary>Length of the longest prefix that does not end inside a multi-byte sequence.</summary>
    public static int CompleteLength(ReadOnlySpan<byte> data)
    {
        int end = data.Length;
        for (int back = 1; back <= 3 && back <= end; back++)
        {
            byte b = data[end - back];
            if ((b & 0xC0) == 0x80) continue;
            int need = b >= 0xF0 ? 4 : b >= 0xE0 ? 3 : b >= 0xC0 ? 2 : 1;
            return need > back ? end - back : end;
        }
        return end;
    }

    /// <summary>Validates the whole file in parallel blocks; block cuts are moved back to a character start.</summary>
    public static bool IsValid(MappedFile file, int blockSize = 16 << 20, CancellationToken ct = default)
    {
        if (blockSize < 4) throw new ArgumentOutOfRangeException(nameof(blockSize));
        int blocks = (int)((file.Length + blockSize - 1) / blockSize);
        bool ok = true;
        Parallel.For(0, blocks, new ParallelOptions { CancellationToken = ct }, (b, state) =>
        {
            long start = BlockStart(file, b, blockSize);
            long end = b + 1 == blocks ? file.Length : BlockStart(file, b + 1, blockSize);
            if (start < 0 || end < 0 || !Utf8.IsValid(file.GetSpan(start, (int)(end - start))))
            {
                ok = false;
                state.Stop();
            }
        });
        return ok;
    }

    /// <summary>Block start moved back to a lead byte; -1 when four continuation bytes in a row prove invalid data.</summary>
    private static long BlockStart(MappedFile file, int block, int blockSize)
    {
        long pos = (long)block * blockSize;
        if (block == 0) return 0;
        for (int back = 0; back < 4; back++, pos--)
            if ((file.GetSpan(pos, 1)[0] & 0xC0) != 0x80) return pos;
        return -1;
    }
}

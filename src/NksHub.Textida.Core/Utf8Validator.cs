using System.Text.Unicode;

namespace NksHub.Textida.Core;

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

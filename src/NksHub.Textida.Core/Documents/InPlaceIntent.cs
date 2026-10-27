using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NksHub.Textida.Core.Documents;

/// <summary>
/// What a save intent says about an in-place save, which has no whole-file hash: the written ranges and one SHA-256 over their new bytes
/// (<c>inplace:start-end,...;HEX</c>). After a crash the file shows that the write landed when those ranges hold those bytes and the length is the intended one.
/// </summary>
internal static class InPlaceIntent
{
    public const string Prefix = "inplace:";

    public static bool IsInPlace(string intent) => intent.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Encode(IReadOnlyList<ByteRange> ranges, IByteSnapshot snapshot)
    {
        var text = new StringBuilder(Prefix);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[1 << 16];
        foreach (var r in ranges)
        {
            text.Append(r.Start).Append('-').Append(r.End).Append(',');
            for (long at = r.Start; at < r.End;)
            {
                int n = snapshot.Read(at, buf.AsSpan(0, (int)Math.Min(buf.Length, r.End - at)));
                if (n == 0) throw new EndOfStreamException("The snapshot ended before its declared length.");
                hash.AppendData(buf, 0, n);
                at += n;
            }
        }
        return text.Append(';').Append(Convert.ToHexString(hash.GetHashAndReset())).ToString();
    }

    /// <summary>True when the ranges named by <paramref name="intent"/> hold the bytes it was made from.</summary>
    public static bool Matches(string path, string intent)
    {
        try
        {
            int semi = intent.LastIndexOf(';');
            if (!IsInPlace(intent) || semi < 0) return false;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.RandomAccess);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buf = new byte[1 << 16];
            foreach (string part in intent[Prefix.Length..semi].Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] se = part.Split('-');
                long start = long.Parse(se[0], CultureInfo.InvariantCulture), end = long.Parse(se[1], CultureInfo.InvariantCulture);
                if (start < 0 || end < start || end > file.Length) return false;
                file.Position = start;
                for (long at = start; at < end;)
                {
                    int n = file.Read(buf, 0, (int)Math.Min(buf.Length, end - at));
                    if (n == 0) return false;
                    hash.AppendData(buf, 0, n);
                    at += n;
                }
            }
            return Convert.ToHexString(hash.GetHashAndReset()) == intent[(semi + 1)..];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or IndexOutOfRangeException or OverflowException)
        {
            return false;
        }
    }
}

using System.Buffers.Binary;
using System.IO.Hashing;
using System.Numerics;
using System.Security.Cryptography;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Hex;

/// <summary>
/// A CRC in the Rocksoft model: register width, polynomial (without the top bit), start value, whether the bytes and the result are bit-reversed, and the
/// value xored into the result. <see cref="Check"/> is the published CRC of the ASCII text <c>123456789</c>; null for a model the user typed.
/// </summary>
public sealed record CrcModel(string Name, int Width, ulong Polynomial, ulong Init, bool ReflectIn, bool ReflectOut, ulong XorOut, ulong? Check = null)
{
    /// <summary>The ready-made models; each one has its published check value, which the tests compute and compare.</summary>
    public static IReadOnlyList<CrcModel> Catalog { get; } =
    [
        new("CRC-16/ARC", 16, 0x8005, 0x0000, true, true, 0x0000, 0xBB3D),
        new("CRC-16/MODBUS", 16, 0x8005, 0xFFFF, true, true, 0x0000, 0x4B37),
        new("CRC-16/CCITT-FALSE", 16, 0x1021, 0xFFFF, false, false, 0x0000, 0x29B1),
        new("CRC-16/XMODEM", 16, 0x1021, 0x0000, false, false, 0x0000, 0x31C3),
        new("CRC-16/KERMIT", 16, 0x1021, 0x0000, true, true, 0x0000, 0x2189),
        new("CRC-16/X-25", 16, 0x1021, 0xFFFF, true, true, 0xFFFF, 0x906E),
        new("CRC-32", 32, 0x04C11DB7, 0xFFFFFFFF, true, true, 0xFFFFFFFF, 0xCBF43926),
        new("CRC-32C", 32, 0x1EDC6F41, 0xFFFFFFFF, true, true, 0xFFFFFFFF, 0xE3069283),
        new("CRC-32/BZIP2", 32, 0x04C11DB7, 0xFFFFFFFF, false, false, 0xFFFFFFFF, 0xFC891918),
        new("CRC-32/MPEG-2", 32, 0x04C11DB7, 0xFFFFFFFF, false, false, 0x00000000, 0x0376E6E7),
        new("CRC-64/ECMA-182", 64, 0x42F0E1EBA9EA3693, 0, false, false, 0, 0x6C40DF5F0B497347),
        new("CRC-64/XZ", 64, 0x42F0E1EBA9EA3693, ulong.MaxValue, true, true, ulong.MaxValue, 0x995DC9BBDF1939FA),
        new("CRC-64/GO-ISO", 64, 0x000000000000001B, ulong.MaxValue, true, true, ulong.MaxValue, 0xB90956C775A41001),
    ];

    /// <summary>The model with every field valid for its width, or why it is not.</summary>
    public string? Problem
    {
        get
        {
            if (Width is < 1 or > 64) return "The width must be 1 to 64 bits.";
            ulong mask = Mask;
            if ((Polynomial & ~mask) != 0 || (Init & ~mask) != 0 || (XorOut & ~mask) != 0) return "The polynomial, the start value and the final xor must fit the width.";
            if ((Polynomial & 1) == 0) return "The polynomial must be odd (its lowest bit is the constant term).";
            return null;
        }
    }

    internal ulong Mask => Width == 64 ? ulong.MaxValue : (1UL << Width) - 1;
}

/// <summary>A running checksum or hash: bytes in, text out.</summary>
public interface IChecksumState : IDisposable
{
    void Append(ReadOnlySpan<byte> data);
    /// <summary>The value as lower case hex digits (big-endian, as the usual tools print it).</summary>
    string Finish();
}

/// <summary>One algorithm the checksum dialog offers; <see cref="Create"/> starts a fresh run.</summary>
public sealed record ChecksumSpec(string Id, string Name, Func<IChecksumState> Create);

/// <summary>The algorithms of the checksum dialog of the hex view and of the hash command of the text tools plugin, so both offer the same list under the same names.</summary>
public static class Checksums
{
    /// <summary>Every algorithm of the dialog: the CRC models, Adler-32, plain sums, xxHash (also XXH3), MD5, SHA-1 and SHA-2.</summary>
    public static IReadOnlyList<ChecksumSpec> All { get; } =
    [
        .. CrcModel.Catalog.Select(Crc),
        new("adler32", "Adler-32", () => new Adler32State()),
        new("sum8", "Sum-8", () => new SumState(1)),
        new("sum16", "Sum-16", () => new SumState(2)),
        new("sum32", "Sum-32", () => new SumState(4)),
        new("xxh32", "xxHash32", () => new XxHash32State()),
        new("xxh64", "xxHash64", () => new XxHash64State()),
        new("xxh3", "XXH3-64", () => new LibraryState(new XxHash3())),
        new("xxh128", "XXH3-128", () => new LibraryState(new XxHash128())),
        Hash("md5", "MD5", HashAlgorithmName.MD5),
        Hash("sha1", "SHA-1", HashAlgorithmName.SHA1),
        Hash("sha256", "SHA-256", HashAlgorithmName.SHA256),
        Hash("sha384", "SHA-384", HashAlgorithmName.SHA384),
        Hash("sha512", "SHA-512", HashAlgorithmName.SHA512),
    ];

    public static ChecksumSpec? Find(string id) => All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    public static ChecksumSpec Crc(CrcModel model)
    {
        if (model.Problem is { } problem) throw new ArgumentException(problem, nameof(model));
        return new(model.Name.ToLowerInvariant(), model.Name, () => new CrcState(model));
    }

    private static ChecksumSpec Hash(string id, string name, HashAlgorithmName algorithm) => new(id, name, () => new CryptoState(algorithm));

    /// <summary>
    /// The checksum of <paramref name="range"/> of <paramref name="source"/>, read in 1 MiB blocks. Cancelling throws <see cref="OperationCanceledException"/>;
    /// <paramref name="progress"/> gets the number of bytes done after each block.
    /// </summary>
    public static string Compute(ChecksumSpec spec, IByteSnapshot source, ByteRange range, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        if (range.End > source.ByteLength) throw new ArgumentOutOfRangeException(nameof(range));
        using var state = spec.Create();
        var buffer = new byte[1 << 20];
        for (long at = range.Start; at < range.End;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int n = source.Read(at, buffer.AsSpan(0, (int)Math.Min(buffer.Length, range.End - at)));
            if (n == 0) throw new EndOfStreamException("The snapshot ended before its declared length.");
            state.Append(buffer.AsSpan(0, n));
            at += n;
            progress?.Report(at - range.Start);
        }
        return state.Finish();
    }

    /// <summary>The digits of a value that was typed or pasted: no spaces, colons, dashes or underscores, no leading <c>0x</c>, in lower case.</summary>
    public static string Digits(string value)
    {
        var digits = new string([.. value.Where(c => !char.IsWhiteSpace(c) && c is not (':' or '-' or '_'))]);
        return (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? digits[2..] : digits).ToLowerInvariant();
    }

    /// <summary>True when two values are the same number: spaces, colons, dashes, a leading <c>0x</c>, case and leading zeros do not matter.</summary>
    public static bool SameValue(string computed, string pasted) =>
        !string.IsNullOrWhiteSpace(pasted) && Digits(computed).TrimStart('0') == Digits(pasted).TrimStart('0');

    private static string Hex(ulong value, int width) => value.ToString("x" + ((width + 3) / 4), System.Globalization.CultureInfo.InvariantCulture);

    internal static ulong Reflect(ulong value, int width)
    {
        ulong reflected = 0;
        for (int i = 0; i < width; i++, value >>= 1) reflected = reflected << 1 | value & 1;
        return reflected;
    }

    // ---- CRC ----

    private sealed class CrcState : IChecksumState
    {
        private readonly CrcModel _m;
        private readonly ulong _mask;
        private readonly ulong[]? _table;
        private ulong _reg;

        public CrcState(CrcModel model)
        {
            _m = model;
            _mask = model.Mask;
            // a register narrower than a byte has no byte table; it is fed bit by bit
            if (model.Width >= 8) _table = BuildTable(model);
            _reg = model.ReflectIn && _table is not null ? Reflect(model.Init, model.Width) : model.Init;
        }

        private static ulong[] BuildTable(CrcModel m)
        {
            var table = new ulong[256];
            ulong mask = m.Mask, top = 1UL << (m.Width - 1), reflected = Reflect(m.Polynomial, m.Width);
            for (int i = 0; i < 256; i++)
            {
                ulong r = m.ReflectIn ? (ulong)i : (ulong)i << (m.Width - 8);
                for (int bit = 0; bit < 8; bit++)
                    r = m.ReflectIn ? (r & 1) != 0 ? r >> 1 ^ reflected : r >> 1 : (r & top) != 0 ? (r << 1 ^ m.Polynomial) & mask : r << 1 & mask;
                table[i] = r;
            }
            return table;
        }

        public void Append(ReadOnlySpan<byte> data)
        {
            if (_table is null)
            {
                foreach (byte b in data) AppendBits(b);
                return;
            }
            ulong reg = _reg;
            if (_m.ReflectIn)
                foreach (byte b in data) reg = _table[(byte)reg ^ b] ^ reg >> 8;
            else
            {
                int shift = _m.Width - 8;
                foreach (byte b in data) reg = _table[(byte)(reg >> shift) ^ b] ^ reg << 8 & _mask;
            }
            _reg = reg;
        }

        private void AppendBits(byte b)
        {
            if (_m.ReflectIn) b = (byte)Reflect(b, 8);
            ulong top = 1UL << (_m.Width - 1);
            for (int i = 7; i >= 0; i--)
            {
                bool flip = ((_reg & top) != 0) ^ ((b >> i & 1) != 0);
                _reg = _reg << 1 & _mask;
                if (flip) _reg ^= _m.Polynomial;
            }
        }

        public string Finish()
        {
            ulong reg = _reg;
            // the byte table keeps a reflected register when the input is reflected
            bool registerReflected = _m.ReflectIn && _table is not null;
            if (registerReflected != _m.ReflectOut) reg = Reflect(reg, _m.Width);
            return Hex((reg ^ _m.XorOut) & _mask, _m.Width);
        }

        public void Dispose() { }
    }

    // ---- Adler-32 and plain sums ----

    private sealed class Adler32State : IChecksumState
    {
        private uint _a = 1, _b;

        public void Append(ReadOnlySpan<byte> data)
        {
            uint a = _a, b = _b;
            // 5552 bytes is the longest run before b could overflow 32 bits
            while (data.Length > 0)
            {
                int n = Math.Min(data.Length, 5552);
                foreach (byte x in data[..n]) { a += x; b += a; }
                a %= 65521;
                b %= 65521;
                data = data[n..];
            }
            (_a, _b) = (a, b);
        }

        public string Finish() => (_b << 16 | _a).ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        public void Dispose() { }
    }

    private sealed class SumState(int bytes) : IChecksumState
    {
        private ulong _sum;

        public void Append(ReadOnlySpan<byte> data)
        {
            ulong sum = _sum;
            foreach (byte b in data) sum += b;
            _sum = sum;
        }

        public string Finish() => Hex(_sum & ((1UL << bytes * 8) - 1), bytes * 8);
        public void Dispose() { }
    }

    // ---- MD5, SHA-1, SHA-2 ----

    private sealed class CryptoState(HashAlgorithmName algorithm) : IChecksumState
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(algorithm);

        public void Append(ReadOnlySpan<byte> data) => _hash.AppendData(data);
        public string Finish() => Convert.ToHexStringLower(_hash.GetHashAndReset());
        public void Dispose() => _hash.Dispose();
    }

    // ---- XXH3 (seed 0), from System.IO.Hashing; its result is the canonical (big-endian) form, as the xxhsum tool prints it ----

    private sealed class LibraryState(NonCryptographicHashAlgorithm hash) : IChecksumState
    {
        public void Append(ReadOnlySpan<byte> data) => hash.Append(data);
        public string Finish() => Convert.ToHexStringLower(hash.GetCurrentHash());
        public void Dispose() { }
    }

    // ---- xxHash (seed 0) ----

    private sealed class XxHash32State : IChecksumState
    {
        private const uint P1 = 2654435761, P2 = 2246822519, P3 = 3266489917, P4 = 668265263, P5 = 374761393;
        private uint _v1 = unchecked(P1 + P2), _v2 = P2, _v3 = 0, _v4 = unchecked(0u - P1);
        private readonly byte[] _tail = new byte[16];
        private int _tailLength;
        private long _total;

        private static uint Round(uint acc, uint input) => BitOperations.RotateLeft(unchecked(acc + input * P2), 13) * P1;

        private void Stripe(ReadOnlySpan<byte> s)
        {
            _v1 = Round(_v1, BinaryPrimitives.ReadUInt32LittleEndian(s));
            _v2 = Round(_v2, BinaryPrimitives.ReadUInt32LittleEndian(s[4..]));
            _v3 = Round(_v3, BinaryPrimitives.ReadUInt32LittleEndian(s[8..]));
            _v4 = Round(_v4, BinaryPrimitives.ReadUInt32LittleEndian(s[12..]));
        }

        public void Append(ReadOnlySpan<byte> data)
        {
            _total += data.Length;
            if (_tailLength > 0)
            {
                int take = Math.Min(16 - _tailLength, data.Length);
                data[..take].CopyTo(_tail.AsSpan(_tailLength));
                _tailLength += take;
                data = data[take..];
                if (_tailLength < 16) return;
                Stripe(_tail);
                _tailLength = 0;
            }
            while (data.Length >= 16)
            {
                Stripe(data);
                data = data[16..];
            }
            data.CopyTo(_tail);
            _tailLength = data.Length;
        }

        public string Finish()
        {
            uint h = _total >= 16
                ? BitOperations.RotateLeft(_v1, 1) + BitOperations.RotateLeft(_v2, 7) + BitOperations.RotateLeft(_v3, 12) + BitOperations.RotateLeft(_v4, 18)
                : _v3 + P5;
            h += (uint)_total;
            var rest = _tail.AsSpan(0, _tailLength);
            while (rest.Length >= 4)
            {
                h = BitOperations.RotateLeft(h + BinaryPrimitives.ReadUInt32LittleEndian(rest) * P3, 17) * P4;
                rest = rest[4..];
            }
            foreach (byte b in rest) h = BitOperations.RotateLeft(h + b * P5, 11) * P1;
            h ^= h >> 15;
            h *= P2;
            h ^= h >> 13;
            h *= P3;
            h ^= h >> 16;
            return h.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
        }

        public void Dispose() { }
    }

    private sealed class XxHash64State : IChecksumState
    {
        private const ulong P1 = 11400714785074694791, P2 = 14029467366897019727, P3 = 1609587929392839161, P4 = 9650029242287828579, P5 = 2870177450012600261;
        private ulong _v1 = unchecked(P1 + P2), _v2 = P2, _v3 = 0, _v4 = unchecked(0ul - P1);
        private readonly byte[] _tail = new byte[32];
        private int _tailLength;
        private long _total;

        private static ulong Round(ulong acc, ulong input) => BitOperations.RotateLeft(unchecked(acc + input * P2), 31) * P1;
        private static ulong Merge(ulong h, ulong v) => (h ^ Round(0, v)) * P1 + P4;

        private void Stripe(ReadOnlySpan<byte> s)
        {
            _v1 = Round(_v1, BinaryPrimitives.ReadUInt64LittleEndian(s));
            _v2 = Round(_v2, BinaryPrimitives.ReadUInt64LittleEndian(s[8..]));
            _v3 = Round(_v3, BinaryPrimitives.ReadUInt64LittleEndian(s[16..]));
            _v4 = Round(_v4, BinaryPrimitives.ReadUInt64LittleEndian(s[24..]));
        }

        public void Append(ReadOnlySpan<byte> data)
        {
            _total += data.Length;
            if (_tailLength > 0)
            {
                int take = Math.Min(32 - _tailLength, data.Length);
                data[..take].CopyTo(_tail.AsSpan(_tailLength));
                _tailLength += take;
                data = data[take..];
                if (_tailLength < 32) return;
                Stripe(_tail);
                _tailLength = 0;
            }
            while (data.Length >= 32)
            {
                Stripe(data);
                data = data[32..];
            }
            data.CopyTo(_tail);
            _tailLength = data.Length;
        }

        public string Finish()
        {
            ulong h;
            if (_total >= 32)
            {
                h = BitOperations.RotateLeft(_v1, 1) + BitOperations.RotateLeft(_v2, 7) + BitOperations.RotateLeft(_v3, 12) + BitOperations.RotateLeft(_v4, 18);
                h = Merge(Merge(Merge(Merge(h, _v1), _v2), _v3), _v4);
            }
            else h = _v3 + P5;
            h += (ulong)_total;
            var rest = _tail.AsSpan(0, _tailLength);
            while (rest.Length >= 8)
            {
                h = BitOperations.RotateLeft(h ^ Round(0, BinaryPrimitives.ReadUInt64LittleEndian(rest)), 27) * P1 + P4;
                rest = rest[8..];
            }
            if (rest.Length >= 4)
            {
                h = BitOperations.RotateLeft(h ^ BinaryPrimitives.ReadUInt32LittleEndian(rest) * P1, 23) * P2 + P3;
                rest = rest[4..];
            }
            foreach (byte b in rest) h = BitOperations.RotateLeft(h ^ b * P5, 11) * P1;
            h ^= h >> 33;
            h *= P2;
            h ^= h >> 29;
            h *= P3;
            h ^= h >> 32;
            return h.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }

        public void Dispose() { }
    }
}

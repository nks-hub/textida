using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Core.Data.Structure;

namespace NksHub.Textida.Core.Tests;

internal sealed class StructurePerformanceFixture : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("textida-structure-perf-").FullName;
    public string Path { get; }
    public StructureFormat Format { get; }
    public long Size { get; }
    public long Rows { get; private init; }
    public long ExpectedNodes { get; private init; }
    public long ExpectedDuplicates { get; private init; }
    public long LargeTokenContentStart { get; private set; }
    public long LargeTokenContentEnd { get; private set; }
    public string Sha256 { get; private set; } = "";
    public double GenerationMilliseconds { get; private set; }

    private StructurePerformanceFixture(StructureFormat format, long size)
    {
        Format = format;
        Size = size;
        Path = System.IO.Path.Combine(directory, format == StructureFormat.Xml ? "fixture.xml" : "fixture.json");
    }

    public static StructurePerformanceFixture Giant(StructureFormat format, long size)
    {
        const int rows = 16_384;
        var fixture = new StructurePerformanceFixture(format, size)
        {
            Rows = rows,
            ExpectedNodes = format == StructureFormat.Json ? 4 + 3L * rows : 5 + 3L * rows,
            ExpectedDuplicates = format == StructureFormat.Json ? rows : 0,
        };
        return fixture.Generate(writer =>
        {
            string prefix = format == StructureFormat.Json ? "{\"blob\":\"" : "<root><blob>";
            string middle = format == StructureFormat.Json ? "\",\"rows\":[" : "</blob><rows>";
            string row = format == StructureFormat.Json ? "{\"key\":0,\"\\u006bey\":1}" : "<item key=\"0\">x</item>";
            string separator = format == StructureFormat.Json ? "," : "";
            string end = format == StructureFormat.Json ? "]}" : "</rows></root>";
            long payload = size - Encoding.UTF8.GetByteCount(prefix + middle + end)
                - (long)Encoding.UTF8.GetByteCount(row) * rows - Encoding.UTF8.GetByteCount(separator) * (rows - 1L);
            fixture.LargeTokenContentStart = Encoding.UTF8.GetByteCount(prefix);
            fixture.LargeTokenContentEnd = fixture.LargeTokenContentStart + payload;
            writer.Text(prefix);
            writer.Fill((byte)'x', payload);
            writer.Text(middle);
            writer.Repeat(row + separator, rows - 1);
            writer.Text(row);
            writer.Text(end);
        });
    }

    public static StructurePerformanceFixture ManyNodes(StructureFormat format, long size)
    {
        string payload = new('x', 64);
        string row = format == StructureFormat.Json
            ? "{\"key\":0,\"\\u006bey\":1,\"label\":\"" + payload + "\"}"
            : "<item key=\"0\">" + payload + "</item>";
        string prefix = format == StructureFormat.Json ? "[" : "<root>";
        string suffix = format == StructureFormat.Json ? "]" : "</root>";
        string separator = format == StructureFormat.Json ? "," : "";
        int separatorBytes = Encoding.UTF8.GetByteCount(separator);
        long available = size - Encoding.UTF8.GetByteCount(prefix + suffix);
        long rows = (available + separatorBytes) / Encoding.UTF8.GetByteCount(row + separator);
        long padding = available - rows * Encoding.UTF8.GetByteCount(row + separator) + separatorBytes;
        var fixture = new StructurePerformanceFixture(format, size)
        {
            Rows = rows,
            ExpectedNodes = format == StructureFormat.Json ? 2 + 4 * rows : 2 + 3 * rows + (padding > 0 ? 1 : 0),
            ExpectedDuplicates = format == StructureFormat.Json ? rows : 0,
        };
        return fixture.Generate(writer =>
        {
            writer.Text(prefix);
            writer.Repeat(row + separator, rows - 1);
            writer.Text(row);
            writer.Fill((byte)' ', padding);
            writer.Text(suffix);
        });
    }

    private StructurePerformanceFixture Generate(Action<Writer> generate)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            using (var writer = new Writer(Path, Size))
            {
                generate(writer);
                Sha256 = writer.Finish(Size);
            }
            Assert.Equal(Size, new FileInfo(Path).Length);
            GenerationMilliseconds = clock.Elapsed.TotalMilliseconds;
            return this;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        File.Delete(Path);
        Directory.Delete(directory, false);
    }

    private sealed class Writer : IDisposable
    {
        private readonly FileStream stream;
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private long written;

        public Writer(string path, long size) => stream = new(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 64 * 1024,
            Options = FileOptions.SequentialScan,
            PreallocationSize = size,
        });

        public void Text(string text) => Write(Encoding.UTF8.GetBytes(text));

        public void Fill(byte value, long count)
        {
            byte[] block = new byte[64 * 1024];
            block.AsSpan().Fill(value);
            while (count > 0)
            {
                int bytes = (int)Math.Min(count, block.Length);
                Write(block.AsSpan(0, bytes));
                count -= bytes;
            }
        }

        public void Repeat(string text, long count)
        {
            byte[] pattern = Encoding.UTF8.GetBytes(text);
            int perBlock = 64 * 1024 / pattern.Length;
            byte[] block = new byte[perBlock * pattern.Length];
            for (int i = 0; i < perBlock; i++) pattern.CopyTo(block, i * pattern.Length);
            while (count > 0)
            {
                int copies = (int)Math.Min(count, perBlock);
                Write(block.AsSpan(0, copies * pattern.Length));
                count -= copies;
            }
        }

        private void Write(ReadOnlySpan<byte> bytes)
        {
            stream.Write(bytes);
            hash.AppendData(bytes);
            written += bytes.Length;
        }

        public string Finish(long expectedBytes)
        {
            Assert.Equal(expectedBytes, written);
            stream.Flush();
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        public void Dispose()
        {
            stream.Dispose();
            hash.Dispose();
        }
    }
}

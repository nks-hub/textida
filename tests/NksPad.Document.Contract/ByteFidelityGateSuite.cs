using System.Text;
using NksPad.Core.Documents;
using NksPad.Core.Encodings;
using Doc = NksPad.Core.Documents.Document;

namespace NksPad.Document.Contract;

/// <summary>
/// P1.3.2 gate through <see cref="Doc"/> (PLAN 5.1): open a file, change one character, save, and the binary difference is
/// exactly that range. Expectations are built from framework encodings and raw bytes, never from the codec under test.
/// A backend that cannot hold UTF-16/32 (<see cref="BackendCapabilities.SupportsUtf16"/> false) must refuse them with NotSupported.
/// </summary>
public abstract class ByteFidelityGateSuite
{
    protected abstract IBackendFactory Factory { get; }

    private sealed record Case(string Name, TextCodec Codec, byte[] Original, int Start, int End, byte[] Replacement, string? Text, string? NewText);

    private const string Mixed = "první řádek\r\nDruhý řádek\nTřetí řádek\rkonec";

    private static Encoding Oracle(TextCodec c) => c.Kind switch
    {
        CodecKind.Utf8 => new UTF8Encoding(false),
        CodecKind.Utf16 => new UnicodeEncoding(c.BigEndian, false),
        CodecKind.Utf32 => new UTF32Encoding(c.BigEndian, false),
        _ => CodePages.Get(1250),
    };

    private static class CodePages
    {
        public static Encoding Get(int page)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(page);
        }
    }

    private static readonly TextCodec[] Codecs =
    [
        TextCodec.Utf8, TextCodec.Utf8Bom, TextCodec.Cp1250,
        TextCodec.Utf16LE, TextCodec.Utf16LEBom, TextCodec.Utf16BE, TextCodec.Utf16BEBom,
        TextCodec.Utf32LE, TextCodec.Utf32LEBom, TextCodec.Utf32BE, TextCodec.Utf32BEBom,
    ];

    private static bool IsWide(TextCodec c) => c.Kind is CodecKind.Utf16 or CodecKind.Utf32;

    /// <summary>Changes the character at <paramref name="index"/> of <paramref name="head"/> to <paramref name="with"/>; <paramref name="parts"/> follow as raw bytes.</summary>
    private static Case Build(string name, TextCodec c, string head, int index, string with, params byte[][] parts)
    {
        var enc = Oracle(c);
        int bom = c.Bom.Length;
        int start = bom + enc.GetByteCount(head[..index]);
        int end = bom + enc.GetByteCount(head[..(index + 1)]);
        byte[] original = [.. c.Bom, .. enc.GetBytes(head), .. parts.SelectMany(p => p)];
        return new($"{name}/{c.Name}", c, original, start, end, enc.GetBytes(with), parts.Length == 0 ? head : null, parts.Length == 0 ? head[..index] + with + head[(index + 1)..] : null);
    }

    private static Dictionary<string, Case> BuildCases()
    {
        var cases = new List<Case>();
        int y = Mixed.IndexOf('ý');
        foreach (var c in Codecs) cases.Add(Build("mixed-eol", c, Mixed, y, "í"));

        // invalid sequences and an undefined code page byte stay exactly as they were
        cases.Add(Build("broken-utf8", TextCodec.Utf8, "ab", 1, "B", [0xE2, 0x82], "x"u8.ToArray(), [0xFF, 0xC3, 0xA9, 0x80], "\r\n\r\n"u8.ToArray()));
        cases.Add(Build("broken-utf8", TextCodec.Utf8Bom, "ab", 0, "A", [0xE2, 0x82], "x"u8.ToArray(), [0xFF, 0xC3, 0xA9, 0x80], "\r\n\r\n"u8.ToArray()));
        cases.Add(Build("broken-utf8-edit-after", TextCodec.Utf8, "abx", 2, "y", [0xE2, 0x82, 0xFF]));
        cases.Add(Build("undefined-bytes", TextCodec.Cp1250, "řádek", 0, "Ř", [0x81, 0x98, 0x0D, 0x0A]));

        foreach (var c in Codecs.Where(IsWide))
        {
            // incomplete unit at the end: the edit before it must not touch it
            int last = Mixed.Length - 1;
            cases.Add(Build("tail-1", c, Mixed, last, "C", [0x41]));
            if (c.Kind == CodecKind.Utf32)
            {
                cases.Add(Build("tail-2", c, Mixed, last, "C", [0x41, 0x42]));
                cases.Add(Build("tail-3", c, Mixed, last, "C", [0x41, 0x42, 0x43]));
            }
            // lone surrogate (UTF-16) or scalar above U+10FFFF (UTF-32) in the middle
            byte[] bad = c.Kind == CodecKind.Utf16
                ? (c.BigEndian ? [0xD8, 0x00] : [0x00, 0xD8])
                : (c.BigEndian ? [0x00, 0x11, 0x00, 0x00] : [0x00, 0x00, 0x11, 0x00]);
            cases.Add(Build("broken-unit", c, "abc", 2, "C", bad, Oracle(c).GetBytes("\r\nxyz")));
        }
        return cases.ToDictionary(k => k.Name);
    }

    private static readonly Dictionary<string, Case> All = BuildCases();

    public static IEnumerable<object[]> Names() => All.Keys.Select(k => new object[] { k });

    private static byte[] Bytes(Doc d) => d.ReadBytes(new(0, d.ByteLength));

    [Theory]
    [MemberData(nameof(Names))]
    public void OpenChangeOneCharacterSaveWritesOnlyThatRange(string name)
    {
        var c = All[name];
        using var dir = new TempDir();
        string path = dir.File("a.txt");
        File.WriteAllBytes(path, c.Original);
        var options = new DocumentOptions { Format = c.Codec };

        if (IsWide(c.Codec) && !Factory.Capabilities.SupportsUtf16)
        {
            Assert.Throws<NotSupportedException>(() => Doc.Open(path, Factory, options));
            Assert.Throws<NotSupportedException>(() => Doc.FromBytes(c.Original, Factory, options));
            return;
        }

        using var d = Doc.Open(path, Factory, options);
        Assert.Same(c.Codec, d.Codec);
        Assert.Equal(c.Original, Bytes(d));
        Assert.True(d.Save().Saved);
        Assert.Equal(c.Original, File.ReadAllBytes(path));

        Assert.True(d.Replace(new(c.Start, c.End), c.Replacement).Applied);
        Assert.True(d.Save().Saved);

        byte[] expected = [.. c.Original.AsSpan(0, c.Start), .. c.Replacement, .. c.Original.AsSpan(c.End)];
        var saved = File.ReadAllBytes(path);
        Assert.Equal(expected, saved);
        var diff = ByteDiff.Compute(c.Original, saved);
        Assert.True(diff.FirstDifference >= c.Start && diff.FirstDifference + diff.NewChanged <= c.Start + c.Replacement.Length, $"{name}: difference {diff} outside [{c.Start}, {c.Start + c.Replacement.Length})");
        if (c.NewText is { } text) Assert.Equal(text, d.ReadText(new(d.BomLength, d.ByteLength)));

        d.Undo();
        Assert.Equal(c.Original, Bytes(d));
    }

    [Theory]
    [InlineData("utf8-bom")]
    [InlineData("utf16le-bom")]
    [InlineData("utf16be-bom")]
    [InlineData("utf32le-bom")]
    [InlineData("utf32be-bom")]
    public void ABomSelectsTheEncodingOnOpen(string which)
    {
        var codec = which switch
        {
            "utf8-bom" => TextCodec.Utf8Bom, "utf16le-bom" => TextCodec.Utf16LEBom, "utf16be-bom" => TextCodec.Utf16BEBom,
            "utf32le-bom" => TextCodec.Utf32LEBom, _ => TextCodec.Utf32BEBom,
        };
        byte[] bytes = [.. codec.Bom, .. Oracle(codec).GetBytes(Mixed)];
        using var dir = new TempDir();
        string path = dir.File("a.txt");
        File.WriteAllBytes(path, bytes);
        if (IsWide(codec) && !Factory.Capabilities.SupportsUtf16)
        {
            Assert.Throws<NotSupportedException>(() => Doc.Open(path, Factory));
            return;
        }
        using var d = Doc.Open(path, Factory);
        Assert.Same(codec, d.Codec);
        Assert.Equal(codec.Bom.Length, d.BomLength);
        Assert.Equal(Mixed, d.ReadText(new(d.BomLength, d.ByteLength)));
    }
}

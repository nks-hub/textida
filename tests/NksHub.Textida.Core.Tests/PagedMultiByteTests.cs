using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P5.5.3: the multi-byte encodings in the large file view. Files of several MiB are written in each encoding and opened through the paged backend; the character boundaries it finds, the line index, the
/// text it reads and the bytes after an edit are compared with a full decode of the same bytes. Characters of one to four bytes fall across every block edge the view reads.
/// </summary>
public class PagedMultiByteTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("textida-mb-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); }
        catch (IOException) { }
    }

    private const int Lines = 60_000;

    private static string Line(string codecName, int n) => codecName switch
    {
        "Shift-JIS" or "EUC-JP" => $"{n}行目：日本語のテキスト、ｶﾀｶﾅとひらがな。\\ ~ 番号 {n % 97}\r\n",
        "GBK" => $"第{n}行：这是简体中文的文本，用来测试编码。\\ ~ 编号 {n % 97}\n",
        "GB18030" => $"第{n}行：𠀀 中文 😀 ř 文本。\\ ~ 编号 {n % 97}\n",
        "Big5" => $"第{n}行：這是繁體中文的文字，用來測試編碼。\\ ~ 編號 {n % 97}\n",
        _ => $"{n}번째 줄: 한국어 텍스트입니다. \\ ~ 번호 {n % 97}\n",
    };

    private (string Path, TextCodec Codec, byte[] Bytes, DecodedText Full) Write(string name)
    {
        var codec = TextCodec.ByName(name)!;
        var text = new StringBuilder();
        for (int n = 0; n < Lines; n++) text.Append(Line(name, n));
        var bytes = codec.Encode(text.ToString());
        Assert.True(bytes.Length > 2 << 20, $"{name}: {bytes.Length} bytes"); // several MiB: many blocks of the view
        var path = Path.Combine(_folder, name + ".txt");
        File.WriteAllBytes(path, bytes);
        return (path, codec, bytes, codec.Decode(bytes));
    }

    private static Doc OpenPaged(string path, TextCodec codec)
    {
        var doc = Doc.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = codec });
        var until = DateTime.UtcNow.AddMinutes(5);
        while (!doc.LineIndexState.Complete)
        {
            Assert.True(DateTime.UtcNow < until, "the line index did not complete");
            Thread.Sleep(10);
        }
        return doc;
    }

    public static TheoryData<string> Names => ["Shift-JIS", "EUC-JP", "GBK", "GB18030", "Big5", "EUC-KR"];

    [Theory]
    [MemberData(nameof(Names))]
    public void The_boundaries_the_view_finds_are_the_ones_of_a_full_decode_at_every_place_it_is_asked(string name)
    {
        var (path, codec, bytes, full) = Write(name);
        using var doc = OpenPaged(path, codec);
        Assert.True(doc.Backend.Supports(codec));
        Assert.Equal(bytes.Length, doc.ByteLength);

        var starts = new HashSet<long>();
        for (int i = 0; i <= full.Text.Length; i++) starts.Add(full.ByteOffsetOf(i));
        var random = new Random(55);
        var places = new List<long> { 0, 1, 2, 3, bytes.Length - 1, bytes.Length };
        for (int k = 0; k < 3000; k++) places.Add(random.NextInt64(0, bytes.Length + 1));
        // every offset around some of the block edges of the view (1 MiB and 128 KiB) and of a few random places
        foreach (long edge in new long[] { 128 << 10, 1 << 20, 2 << 20, bytes.Length / 2 })
            for (long o = edge - 40; o <= edge + 40; o++) places.Add(o);
        foreach (long at in places.Where(o => o >= 0 && o <= bytes.Length))
            Assert.Equal(starts.Contains(at), doc.IsCharBoundary(at)); // a trail byte is no boundary even when its value looks like ASCII
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void The_line_index_and_the_text_read_between_boundaries_agree_with_the_full_decode(string name)
    {
        var (path, codec, bytes, full) = Write(name);
        using var doc = OpenPaged(path, codec);
        long expectedLines = 1 + full.Text.Count(c => c == '\n');
        Assert.Equal(expectedLines, doc.LineIndexState.Lines);

        var random = new Random(56);
        for (int k = 0; k < 300; k++)
        {
            int a = random.Next(0, full.Text.Length - 1), b = Math.Min(full.Text.Length, a + random.Next(1, 400));
            while (a > 0 && !full.IsCharBoundary(a)) a--;
            while (b < full.Text.Length && !full.IsCharBoundary(b)) b++;
            var range = new ByteRange(full.ByteOffsetOf(a), full.ByteOffsetOf(b));
            Assert.Equal(full.Text[a..b], doc.ReadText(range));
        }
        // a position of the middle of the file, by line and column, is where the full decode says
        int line = 31_337;
        int offset = 0;
        for (int i = 0; i < line; i++) offset = full.Text.IndexOf('\n', offset) + 1;
        Assert.True(doc.TryGetOffset(new LinePosition(line, 0), out long lineStart));
        Assert.Equal(full.ByteOffsetOf(offset), lineStart);
        Assert.True(doc.TryGetPosition(lineStart, out var position));
        Assert.Equal((line, 0), (position.Line, position.Column));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void An_edit_on_a_boundary_is_applied_and_the_bytes_stay_exact_and_one_inside_a_character_is_refused(string name)
    {
        var (path, codec, bytes, full) = Write(name);
        using var doc = OpenPaged(path, codec);

        // replace a few characters in the middle with other characters of the encoding
        int a = full.Text.Length / 2;
        while (!full.IsCharBoundary(a) || full.Text[a] is '\r' or '\n') a++;
        int b = a + 3;
        while (!full.IsCharBoundary(b) || full.Text[b - 1] is '\r' or '\n') b++;
        var range = new ByteRange(full.ByteOffsetOf(a), full.ByteOffsetOf(b));
        var insert = codec.Encode(name is "EUC-KR" ? "한글" : name is "Big5" ? "測試" : name is "Shift-JIS" or "EUC-JP" ? "あア漢" : "测试");
        Assert.True(doc.Replace(range, insert).Applied);
        var expected = bytes[..(int)range.Start].Concat(insert).Concat(bytes[(int)range.End..]).ToArray();
        Assert.Equal(expected.Length, doc.ByteLength);
        Assert.Equal(expected, doc.ReadBytes(new ByteRange(0, doc.ByteLength)));

        // the second byte of a two-byte character is not a place an edit may start at
        int wide = Enumerable.Range(a + 10, 200).First(i => full.Text[i] > 0x7F && full.ByteOffsetOf(i + 1) - full.ByteOffsetOf(i) >= 2);
        long inside = full.ByteOffsetOf(wide) + 1;
        long shift = insert.Length - range.Length;
        Assert.False(doc.IsCharBoundary(inside + shift));
        Assert.False(doc.Replace(new ByteRange(inside + shift, inside + shift), codec.Encode("a")).Applied);
        Assert.Equal(expected, doc.ReadBytes(new ByteRange(0, doc.ByteLength)));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Saving_streams_the_edited_text_byte_for_byte(string name)
    {
        var (path, codec, bytes, full) = Write(name);
        using var doc = OpenPaged(path, codec);
        var insert = codec.Encode("xyz");
        Assert.True(doc.Replace(new ByteRange(0, 0), insert).Applied);
        var target = Path.Combine(_folder, "saved-" + name + ".txt");
        File.Copy(path, target);
        using var saved = OpenPaged(target, codec);
        Assert.True(saved.Replace(new ByteRange(0, 0), insert).Applied);
        await saved.SaveAsync();
        Assert.Equal(insert.Concat(bytes).ToArray(), File.ReadAllBytes(target));
    }

    [Fact]
    public void A_run_cut_out_of_the_middle_starts_after_the_first_byte_that_can_only_begin_a_character()
    {
        var sjis = TextCodec.ByName("Shift-JIS")!;
        var bytes = sjis.Encode("日本語 テキスト");
        int space = Array.IndexOf(bytes, (byte)' ');
        Assert.Equal(space + 1, sjis.FirstBoundary(bytes));
        Assert.True(sjis.IsSyncByte((byte)' '));
        Assert.False(sjis.IsSyncByte((byte)'A')); // 0x41 can be the trail byte of a two-byte character
        Assert.True(TextCodec.ByName("EUC-JP")!.IsSyncByte((byte)'A'));
        Assert.False(TextCodec.ByName("GB18030")!.IsSyncByte((byte)'5')); // a digit is the second byte of a four-byte character
        Assert.Equal(0, TextCodec.Utf8.FirstBoundary(bytes));
    }
}

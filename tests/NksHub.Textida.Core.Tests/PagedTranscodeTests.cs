using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P5.5.3: "Convert to" for a large file. The conversion is a pending step; the save streams the file into the new encoding block by block, cut between characters, and swaps it in when it is complete.
/// The files here are several MiB, so many blocks and many characters across their edges.
/// </summary>
public class PagedTranscodeTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("textida-transcode-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); }
        catch (IOException) { }
    }

    private static string Japanese(int lines)
    {
        var text = new StringBuilder();
        for (int n = 0; n < lines; n++) text.Append($"{n}行目：日本語のテキスト、ｶﾀｶﾅとひらがな。\\ ~ 番号 {n % 97}\r\n");
        return text.ToString();
    }

    private static string Czech(int lines)
    {
        var text = new StringBuilder();
        for (int n = 0; n < lines; n++) text.Append($"Řádek {n}: příliš žluťoučký kůň úpěl ďábelské ódy, € „citát“ {n % 97}\n");
        return text.ToString();
    }

    private string Write(string name, TextCodec codec, string text, bool bom = false)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, [.. (bom ? codec.Bom : []), .. codec.WithBom(false).Encode(text)]);
        return path;
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

    [Fact]
    public async Task UTF8_to_Shift_JIS_and_back_is_byte_exact_and_the_original_is_whole_until_the_new_file_is_complete()
    {
        string text = Japanese(70_000);
        var sjis = TextCodec.ByName("Shift-JIS")!;
        string path = Write("jp.txt", TextCodec.Utf8, text);
        byte[] original = File.ReadAllBytes(path);
        Assert.True(original.Length > 3 << 20);
        using var doc = OpenPaged(path, TextCodec.Utf8);

        Assert.False(doc.PreviewTranscode(sjis).HasLoss);
        Assert.True(doc.StageTranscode(sjis).Applied);
        Assert.Equal(sjis, doc.PendingTranscode);
        Assert.True(doc.IsModified);

        bool checkedOriginal = false;
        doc.StreamSaveStep = (step, temp) =>
        {
            if (step != "before-swap") return;
            Assert.Equal(original, File.ReadAllBytes(path)); // the file is still the old one when the new one is complete beside it
            Assert.True(File.Exists(temp));
            checkedOriginal = true;
        };
        var result = await doc.SaveAsync();
        Assert.True(result.Saved);
        Assert.True(checkedOriginal);
        Assert.Equal(sjis.Encode(text), File.ReadAllBytes(path));
        Assert.Equal((sjis, null), (doc.Codec, doc.PendingTranscode));
        Assert.False(doc.IsModified);
        Assert.Equal(new FileInfo(path).Length, doc.ByteLength);

        // undo converts back: one pending step, and the next save writes the original bytes again
        Assert.True(doc.CanUndo);
        doc.Undo();
        Assert.Equal(TextCodec.Utf8, doc.PendingTranscode);
        Assert.True(doc.IsModified);
        doc.StreamSaveStep = null;
        Assert.True((await doc.SaveAsync()).Saved);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(TextCodec.Utf8, doc.Codec);
        Assert.Equal(text, doc.ReadText(new ByteRange(0, doc.ByteLength)).Replace("﻿", ""));
    }

    [Fact]
    public async Task CP1250_to_UTF16_with_a_byte_order_mark_is_byte_exact_in_multi_MiB()
    {
        string text = Czech(60_000);
        string path = Write("cz.txt", TextCodec.Cp1250, text);
        Assert.True(new FileInfo(path).Length > 3 << 20);
        using var doc = OpenPaged(path, TextCodec.Cp1250);
        var utf16 = TextCodec.Utf16LEBom;

        Assert.False(doc.PreviewTranscode(utf16).HasLoss);
        Assert.True(doc.StageTranscode(utf16).Applied);
        Assert.True((await doc.SaveAsync()).Saved);

        byte[] expected = [.. utf16.Bom, .. utf16.WithBom(false).Encode(text)];
        Assert.Equal(expected, File.ReadAllBytes(path));
        Assert.Equal(utf16, doc.Codec);
        Assert.Equal(expected.Length, doc.ByteLength);
        Assert.Equal(text.Length, doc.ReadText(new ByteRange(doc.BomLength, doc.ByteLength)).Length);
        // and the line index of the new file is the one of the text
        var until = DateTime.UtcNow.AddMinutes(5);
        while (!doc.LineIndexState.Complete) { Assert.True(DateTime.UtcNow < until); Thread.Sleep(10); }
        Assert.Equal(1 + text.Count(c => c == '\n'), doc.LineIndexState.Lines);
    }

    [Fact]
    public async Task A_character_the_target_cannot_hold_is_reported_before_anything_is_written_and_a_confirmed_replacement_is_used()
    {
        string text = Japanese(40_000) + "emoji 😀 and ř\r\n" + Japanese(2_000);
        var sjis = TextCodec.ByName("Shift-JIS")!;
        string path = Write("loss.txt", TextCodec.Utf8, text);
        byte[] original = File.ReadAllBytes(path);
        using var doc = OpenPaged(path, TextCodec.Utf8);

        var loss = doc.PreviewTranscode(sjis);
        Assert.True(loss.HasLoss);
        Assert.Equal(2, loss.Total); // the emoji and the ř, each once
        Assert.Contains(loss.Groups, g => g.CodePoint == 0x1F600 && g.Count == 1);

        Assert.True(doc.StageTranscode(sjis).Applied); // nothing confirmed
        var refused = await doc.SaveAsync();
        Assert.False(refused.Saved);
        Assert.Equal(2, refused.Loss!.Total);
        Assert.Equal(original, File.ReadAllBytes(path)); // not a byte written
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
        Assert.Equal(TextCodec.Utf8, doc.Codec);

        doc.Undo(); // cancels the pending conversion
        Assert.Null(doc.PendingTranscode);
        Assert.False(doc.IsModified);
        doc.Redo();
        Assert.Equal(sjis, doc.PendingTranscode);

        doc.Undo();
        Assert.True(doc.StageTranscode(sjis, "?").Applied);
        Assert.True((await doc.SaveAsync()).Saved);
        Assert.Equal(sjis.Encode(text.Replace("😀", "?").Replace("ř", "?")), File.ReadAllBytes(path));
    }

    [Fact]
    public async Task A_cancelled_save_leaves_the_file_and_the_pending_conversion_as_they_were()
    {
        string text = Japanese(60_000);
        string path = Write("cancel.txt", TextCodec.Utf8, text);
        byte[] original = File.ReadAllBytes(path);
        using var doc = OpenPaged(path, TextCodec.Utf8);
        var sjis = TextCodec.ByName("Shift-JIS")!;
        Assert.True(doc.StageTranscode(sjis).Applied);
        doc.StreamSaveStep = (step, _) => { if (step == "temp-writing") doc.CancelSave(); };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doc.SaveAsync());

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(sjis, doc.PendingTranscode);
        Assert.Equal(TextCodec.Utf8, doc.Codec);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }
}

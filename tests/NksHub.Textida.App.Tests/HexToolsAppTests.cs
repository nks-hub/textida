using System.Reflection;
using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Tests;

/// <summary>The hex tools of the Edit menu (P4.10): copy, paste and export as, fill and operations, checksums, driven through the same methods the menu items call.</summary>
[Collection("LocState")]
public class HexToolsAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public HexToolsAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static byte[] Sample(int length = 4096)
    {
        var bytes = new byte[length];
        new Random(11).NextBytes(bytes);
        bytes[0] = 0; // a NUL early on makes it a binary file
        return bytes;
    }

    private DocTab OpenHex(byte[] bytes, string name = "data.bin")
    {
        _w.OpenFile(_temp.Bytes(name, bytes), null);
        Press(Key.K, TestApp.Mod);
        Press(Key.H, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(_w.Active!.Hex);
        _w.Active.Hex!.Focus();
        Dispatcher.UIThread.RunJobs();
        return _w.Active;
    }

    private void Press(Key key, RawInputModifiers mods = RawInputModifiers.None) => TestApp.Press(_w, key, mods);

    private static void Pump(Task task)
    {
        for (int i = 0; i < 4000 && !task.IsCompleted; i++)
        {
            Thread.Sleep(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static byte[] Bytes(DocTab tab) => tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength));

    /// <summary>The CRC-32 as the trailer of a gzip stream holds it: another implementation than the one under test.</summary>
    private static string GzipCrc32(byte[] data)
    {
        var gzip = new MemoryStream();
        using (var z = new System.IO.Compression.GZipStream(gzip, System.IO.Compression.CompressionLevel.NoCompression, leaveOpen: true)) z.Write(data);
        var g = gzip.ToArray();
        return Convert.ToHexStringLower([g[^5], g[^6], g[^7], g[^8]]);
    }

    private string Clipboard() => _w.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult()!;

    // ---- copy and paste as ----

    private static byte[] CompileAndRun(string statements)
    {
        string source = "public static class Holder { public static byte[] Get() {\n" + statements + "\nreturn data; } }";
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Length > 0).Select(a => MetadataReference.CreateFromFile(a.Location));
        var compilation = CSharpCompilation.Create("copied", [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: false));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        var assembly = Assembly.Load(image.ToArray());
        return (byte[])assembly.GetType("Holder")!.GetMethod("Get")!.Invoke(null, null)!;
    }

    [AvaloniaFact]
    public void Copy_as_CSharp_compiles_and_holds_the_same_bytes()
    {
        var tab = OpenHex(Sample());
        var hex = tab.Hex!;
        hex.Select(100, 2100);
        Pump(_w.HexCopyAsAsync(hex, ByteFormat.CSharp));
        string text = Clipboard();
        Assert.StartsWith("byte[] data = new byte[] {", text);
        Assert.Equal(Bytes(tab)[100..2100], CompileAndRun(text));
        Assert.Contains(Loc.Get(K.HexCopyAsDone, ("bytes", 2000), ("format", Loc.Get(K.HexFormatCSharp))), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public void Base64_goes_out_and_comes_back_as_the_same_bytes()
    {
        var tab = OpenHex(Sample());
        var hex = tab.Hex!;
        hex.Select(10, 1010);
        Pump(_w.HexCopyAsAsync(hex, ByteFormat.Base64));
        Assert.Equal(Convert.ToBase64String(Bytes(tab)[10..1010]), Clipboard());
        hex.MoveTo(2000);
        Pump(_w.HexPasteAsAsync(hex, ByteTextKind.Base64));
        Assert.Equal(Bytes(tab)[10..1010], Bytes(tab)[2000..3000]);
        Assert.Equal(4096, tab.Doc.Doc.ByteLength);
    }

    [AvaloniaFact]
    public void Paste_as_reads_hex_text_and_arrays_and_refuses_what_is_not_in_the_form()
    {
        var tab = OpenHex(Sample());
        var hex = tab.Hex!;
        _w.Clipboard!.SetTextAsync("byte[] x = { 0xDE, 0xAD, 255, -1 };").GetAwaiter().GetResult();
        hex.MoveTo(0);
        Pump(_w.HexPasteAsAsync(hex, ByteTextKind.CArray));
        Assert.Equal([0xDE, 0xAD, 0xFF, 0xFF], Bytes(tab)[..4]);

        var before = Bytes(tab);
        _w.Clipboard!.SetTextAsync("0x1FF").GetAwaiter().GetResult();
        Pump(_w.HexPasteAsAsync(hex, ByteTextKind.CArray));
        Assert.Equal(before, Bytes(tab));
        Assert.Contains("0x1FF", _w.StatusMessageText);
        _w.Clipboard!.SetTextAsync("not base64 *").GetAwaiter().GetResult();
        Pump(_w.HexPasteAsAsync(hex, ByteTextKind.Base64));
        Assert.Equal(before, Bytes(tab));
    }

    [AvaloniaFact]
    public void Export_writes_the_selection_as_a_binary_file_or_in_a_text_form()
    {
        var tab = OpenHex(Sample(), "dump.bin");
        var hex = tab.Hex!;
        hex.Select(256, 1000);
        string binary = Path.Combine(_temp.Path, "out.bin"), text = Path.Combine(_temp.Path, "out.hex");
        _dialogs.SaveFiles.Enqueue(binary);
        Pump(_w.HexExportAsync(hex, null));
        Assert.Equal(Bytes(tab)[256..1000], File.ReadAllBytes(binary));
        Assert.Equal("dump.bin", _dialogs.SaveFileNames[0]);

        _dialogs.SaveFiles.Enqueue(text);
        Pump(_w.HexExportAsync(hex, ByteFormat.IntelHex));
        Assert.Equal(ByteFormats.Format(Bytes(tab)[256..1000], ByteFormat.IntelHex, address: 256).Replace("\r\n", "\n"), File.ReadAllText(text).Replace("\r\n", "\n"));
        Assert.Equal("dump.hex", _dialogs.SaveFileNames[1]);

        _dialogs.SaveFiles.Enqueue(null); // cancelled: nothing is written
        Pump(_w.HexExportAsync(hex, ByteFormat.HexDump));
        Assert.False(File.Exists(Path.Combine(_temp.Path, "out.txt")));
    }

    [AvaloniaFact]
    public void Copy_and_export_without_a_selection_say_so()
    {
        var hex = OpenHex(Sample()).Hex!;
        Pump(_w.HexCopyAsAsync(hex, ByteFormat.CSharp));
        Assert.Contains(Loc.Get(K.HexNeedsSelection), _w.StatusMessageText);
    }

    // ---- fill and operations ----

    [AvaloniaFact]
    public void Fill_with_a_pattern_asks_shows_how_many_bytes_and_is_one_undo_step()
    {
        var tab = OpenHex(Sample());
        var hex = tab.Hex!;
        var original = Bytes(tab);
        hex.Select(8, 19);
        _dialogs.Prompts.Enqueue("DE AD");
        Pump(_w.HexFillPatternAsync(hex));
        Assert.Equal([0xDE, 0xAD, 0xDE, 0xAD, 0xDE, 0xAD, 0xDE, 0xAD, 0xDE, 0xAD, 0xDE], Bytes(tab)[8..19]);
        Assert.Equal(Loc.Get(K.HexFillPreview, ("bytes", 11), ("length", 2)), _dialogs.Previews[0]);
        Assert.Equal(original[19..], Bytes(tab)[19..]);
        hex.UndoRedo(redo: false);
        Assert.Equal(original, Bytes(tab));

        hex.MoveTo(0);
        _dialogs.Prompts.Enqueue("\"AB\" * 4K");
        Pump(_w.HexFillPatternAsync(hex));
        Assert.Equal((byte)'A', Bytes(tab)[0]);
        Assert.Equal((byte)'B', Bytes(tab)[4095]);
        Assert.Equal(4096, tab.Doc.Doc.ByteLength);
    }

    [AvaloniaFact]
    public void A_pattern_that_does_not_parse_or_a_fill_without_target_changes_nothing_and_says_why()
    {
        var tab = OpenHex(Sample());
        var hex = tab.Hex!;
        var original = Bytes(tab);
        hex.MoveTo(5);
        _dialogs.Prompts.Enqueue("XYZ");
        Pump(_w.HexFillPatternAsync(hex));
        Assert.Contains("XYZ", _w.StatusMessageText);
        _dialogs.Prompts.Enqueue("00");
        Pump(_w.HexFillPatternAsync(hex));
        Assert.Contains(Loc.Get(K.HexFillNeedsCount), _w.StatusMessageText);
        Assert.Equal(original, Bytes(tab));
        Assert.Equal(0, tab.Doc.Doc.UndoDepth);
    }

    [AvaloniaFact]
    public void Repeat_random_and_sequence_fills_work_on_the_selection()
    {
        var tab = OpenHex(Sample());
        var hex = tab.Hex!;
        var original = Bytes(tab);
        hex.Select(10, 14);
        _dialogs.Prompts.Enqueue("2");
        Pump(_w.HexFillRepeatAsync(hex));
        Assert.Equal(4096 + 8, tab.Doc.Doc.ByteLength);
        Assert.Equal(original[10..14], Bytes(tab)[14..18]);
        Assert.Equal(original[10..14], Bytes(tab)[18..22]);
        hex.UndoRedo(redo: false);

        hex.Select(0, 64);
        _w.HexFillRandom(hex);
        Assert.NotEqual(original[..64], Bytes(tab)[..64]);
        Assert.Equal(original[64..], Bytes(tab)[64..]);

        hex.Select(0, 8);
        _dialogs.Prompts.Enqueue("0x10 -2 16 be");
        Pump(_w.HexFillSequenceAsync(hex));
        Assert.Equal([0x00, 0x10, 0x00, 0x0E, 0x00, 0x0C, 0x00, 0x0A], Bytes(tab)[..8]);
    }

    [AvaloniaFact]
    public void Operations_ask_for_what_they_need_and_apply_to_the_selection()
    {
        var tab = OpenHex(Sample());
        var hex = tab.Hex!;
        var original = Bytes(tab);
        hex.Select(16, 24);
        _dialogs.Prompts.Enqueue("FF");
        Pump(_w.HexOperateAsync(hex, HexOperation.Xor));
        Assert.Equal(original[16..24].Select(b => (byte)(b ^ 0xFF)).ToArray(), Bytes(tab)[16..24]);
        Assert.Equal(Loc.Get(K.HexOpPreview, ("bytes", 8)), _dialogs.Previews[0]);

        Pump(_w.HexOperateAsync(hex, HexOperation.Not));
        Assert.Equal(original[16..24], Bytes(tab)[16..24]);

        _dialogs.Prompts.Enqueue("3");
        Pump(_w.HexOperateAsync(hex, HexOperation.RotateLeft));
        Assert.Equal(original[16..24].Select(b => (byte)(b << 3 | b >> 5)).ToArray(), Bytes(tab)[16..24]);

        Pump(_w.HexOperateAsync(hex, HexOperation.SwapEndian, 4));
        var rotated = original[16..24].Select(b => (byte)(b << 3 | b >> 5)).ToArray();
        Assert.Equal([.. rotated[..4].Reverse(), .. rotated[4..].Reverse()], Bytes(tab)[16..24]);
        Assert.Equal(original[..16], Bytes(tab)[..16]);
        Assert.Equal(original[24..], Bytes(tab)[24..]);

        _dialogs.Prompts.Enqueue("9"); // not 1 to 7
        Pump(_w.HexOperateAsync(hex, HexOperation.ShiftLeft));
        Assert.Contains(Loc.Get(K.HexOpInvalid, ("reason", "9")), _w.StatusMessageText);
    }

    // ---- checksums ----

    [AvaloniaFact]
    public void A_checksum_of_the_selection_is_shown_copied_and_compared_with_a_pasted_value()
    {
        var tab = OpenHex(Sample());
        var hex = tab.Hex!;
        hex.Select(100, 3100);
        string expected = Convert.ToHexStringLower(SHA256.HashData(Bytes(tab).AsSpan(100, 3000)));

        _dialogs.Answers.Enqueue(1); // compare...
        _dialogs.Prompts.Enqueue("  " + expected.ToUpperInvariant() + " ");
        _dialogs.Answers.Enqueue(1);
        _dialogs.Prompts.Enqueue("0000");
        _dialogs.Answers.Enqueue(0); // copy
        Pump(_w.HexChecksumAsync(hex, Checksums.Find("sha256")!));

        Assert.Equal(3, _dialogs.Asked.Count);
        Assert.Equal([expected], _dialogs.Asked[0].Details);
        Assert.Contains("SHA-256", _dialogs.Asked[0].Message);
        Assert.Contains(Loc.Get(K.HexSumMatch), _dialogs.Asked[1].Message);
        Assert.Contains(Loc.Get(K.HexSumDiffers), _dialogs.Asked[2].Message);
        Assert.Equal(expected, Clipboard());
    }

    [AvaloniaFact]
    public void Without_a_selection_the_checksum_covers_the_whole_file_and_a_custom_crc_follows_its_definition()
    {
        var tab = OpenHex(Sample(5000));
        var hex = tab.Hex!;
        hex.MoveTo(10);
        _dialogs.Answers.Enqueue(2);
        Pump(_w.HexChecksumAsync(hex, Checksums.Find("crc-32")!));
        var crc32 = GzipCrc32(Bytes(tab));
        Assert.Equal([crc32], _dialogs.Asked[0].Details);
        Assert.Contains(Loc.Get(K.HexSumScopeFile, ("bytes", 5000)), _dialogs.Asked[0].Message);

        _dialogs.Prompts.Enqueue("32 4C11DB7 FFFFFFFF 1 1 FFFFFFFF");
        _dialogs.Answers.Enqueue(2);
        Pump(_w.HexCustomCrcAsync(hex));
        Assert.Equal([crc32], _dialogs.Asked[1].Details);

        Assert.True(MainWindow.TryParseCrcModel("16 8005 0000 1 1 0000", out var model, out _));
        Assert.Equal(16, model.Width);
        Assert.False(MainWindow.TryParseCrcModel("16 8004 0000 1 1 0000", out _, out string even));
        Assert.NotEmpty(even);
        Assert.False(MainWindow.TryParseCrcModel("16 8005 0000 2 1 0000", out _, out _));
    }

    [AvaloniaFact]
    public void Esc_cancels_a_running_checksum_without_a_result()
    {
        string path = Path.Combine(_temp.Path, "big.bin");
        using (var fs = new FileStream(path, FileMode.Create)) fs.SetLength(128L << 20);
        _w.OpenFile(path, null);
        Press(Key.K, TestApp.Mod);
        Press(Key.H, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        var hex = _w.Active!.Hex!;
        hex.Focus();
        Dispatcher.UIThread.RunJobs();

        var running = _w.HexChecksumAsync(hex, Checksums.Find("crc-32")!);
        Assert.NotNull(hex.Job);
        Press(Key.Escape);
        Pump(running);
        Assert.Equal(Loc.Get(K.HexSumCancelled), _w.StatusMessageText);
        Assert.Empty(_dialogs.Asked);
        Assert.Null(hex.Job);
    }

    [AvaloniaFact]
    public void The_menus_of_the_tools_list_every_form_and_every_operation()
    {
        var hex = OpenHex(Sample()).Hex!;
        Assert.Equal(14, _w.HexCopyAsItems(hex).Count);
        Assert.Equal(3, _w.HexPasteAsItems(hex).Count);
        Assert.Equal(15, _w.HexExportItems(hex).Count);
        Assert.Equal(4, _w.HexFillItems(hex).Count);
        Assert.Equal(12, _w.HexOperateItems(hex).Count);
        Assert.All(_w.HexCopyAsItems(hex).Concat(_w.HexOperateItems(hex)), i => Assert.False(string.IsNullOrWhiteSpace(i.Header)));
    }
}

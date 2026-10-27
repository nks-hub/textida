using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Tests;

/// <summary>P5.6.5 in the window: two read-only grids that scroll together, the list of differences and the two modes.</summary>
[Collection("LocState")]
public class HexCompareAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public HexCompareAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        foreach (var window in _w.CompareWindows.ToList()) window.Close();
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static byte[] Random(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        bytes[0] = 0;
        return bytes;
    }

    private DocTab OpenHex(byte[] bytes, string name = "left.bin")
    {
        _w.OpenFile(_temp.Bytes(name, bytes), null);
        TestApp.Press(_w, Key.K, TestApp.Mod);
        TestApp.Press(_w, Key.H, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        var tab = _w.Active!;
        Assert.NotNull(tab.Hex);
        return tab;
    }

    private static void PumpUntil(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(20))
        {
            Thread.Sleep(5);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition(), what);
    }

    [AvaloniaFact]
    public void Compared_with_its_file_an_inserted_block_is_one_insertion_and_the_grids_cannot_be_edited()
    {
        var original = Random(60_000, 1);
        var tab = OpenHex(original);
        var hex = tab.Hex!;
        hex.Editor.Insert = true;
        var inserted = Random(300, 2);
        inserted[0] = (byte)(original[29_999] ^ 1);
        inserted[^1] = (byte)(original[30_000] ^ 1);
        Assert.True(hex.Editor.Write(new ByteRange(30_000, 30_000), inserted).Applied);

        var window = _w.CompareHexWithSaved(tab)!;
        Assert.NotNull(window);
        PumpUntil(() => window.Result is not null, "no result");

        // the document on the left has the extra bytes, the file on the right lacks them
        var d = Assert.Single(window.Result!.Differences);
        Assert.Equal((DiffKind.Delete, new ByteRange(30_000, 30_300), new ByteRange(30_000, 30_000)), (d.Kind, d.Left, d.Right));
        Assert.True(window.Result.Aligned);
        Assert.Single(window.RowTexts);
        Assert.Contains(Loc.Get(K.HexCompareKindDelete), window.RowTexts[0]);
        Assert.Equal(Loc.Plural(K.HexCompareSummary, 1, ("left", 300), ("right", 0)), window.SummaryText);

        // the right grid is the file on disk; neither grid takes a key
        Assert.Equal(60_000, window.RightView.Document.ByteLength);
        window.LeftView.Editor.MoveTo(5);
        window.LeftView.TypeNibble(0xA);
        window.RightView.TypeNibble(0xA);
        Assert.Equal(60_300, window.LeftView.Document.ByteLength);
        Assert.Equal(60_000, window.RightView.Document.ByteLength);
        Assert.Equal(original[5], window.RightView.Document.ReadBytes(new ByteRange(5, 6))[0]);

        window.Close();
        Assert.Empty(_w.CompareWindows);
    }

    [AvaloniaFact]
    public void Scrolling_one_grid_scrolls_the_other_to_the_corresponding_place_and_the_list_goes_to_the_differences()
    {
        var a = Random(80_000, 3);
        var block = Random(1_000, 4);
        block[0] = (byte)(a[9_999] ^ 1);
        block[^1] = (byte)(a[10_000] ^ 1);
        var b = a.Take(10_000).Concat(block).Concat(a.Skip(10_000)).ToArray();
        b[50_500] ^= 0xFF;
        var tab = OpenHex(a);
        _dialogs.Picked.Add(_temp.Bytes("right.bin", b));
        var open = _w.CompareHexWithFileAsync(tab);
        PumpUntil(() => open.IsCompleted, "the file was not picked");
        var window = open.Result!;
        PumpUntil(() => window.Result is not null, "no result");

        Assert.Equal([DiffKind.Insert, DiffKind.Replace], window.Result!.Differences.Select(d => d.Kind));
        Assert.Equal(2, window.RowTexts.Count);

        window.Select(1);
        Assert.Equal(1, window.SelectedDifference);
        Assert.Equal(new ByteRange(49_500, 49_501), window.LeftView.Editor.Selection);
        Assert.Equal(new ByteRange(50_500, 50_501), window.RightView.Editor.Selection);

        window.Step(1); // wraps to the first
        Assert.Equal(0, window.SelectedDifference);
        window.Step(-1);
        Assert.Equal(1, window.SelectedDifference);

        window.LeftView.ScrollToOffset(40_000);
        Dispatcher.UIThread.RunJobs();
        long mapped = window.Result.MapLeftToRight(window.LeftView.FirstByte);
        Assert.Equal(mapped / window.RightView.BytesPerRow * window.RightView.BytesPerRow, window.RightView.FirstByte);
        Assert.Equal(window.LeftView.FirstByte + 1_000, mapped);
    }

    [AvaloniaFact]
    public void Byte_by_byte_does_not_look_for_insertions_and_identical_files_are_told_so()
    {
        var a = Random(50_000, 5);
        var b = a.Take(20_000).Concat(new byte[] { 9, 9, 9 }).Concat(a.Skip(20_000)).ToArray();
        var tab = OpenHex(a);
        _dialogs.Picked.Add(_temp.Bytes("shifted.bin", b));
        var open = _w.CompareHexWithFileAsync(tab);
        PumpUntil(() => open.IsCompleted, "the file was not picked");
        var window = open.Result!;
        PumpUntil(() => window.Result is not null, "no result");
        Assert.Single(window.Result!.Differences);
        Assert.True(window.IsAligned);

        window.SetAligned(false);
        PumpUntil(() => window.Result is { Aligned: false }, "the byte by byte result did not arrive");
        Assert.True(window.Result!.Differences.Count > 100);
        Assert.False(window.IsAligned);

        var same = _w.CompareHexWithSaved(tab)!;
        PumpUntil(() => same.Result is not null, "no result");
        Assert.True(same.Result!.AreEqual);
        Assert.Equal(Loc.Get(K.HexCompareIdentical), same.SummaryText);
        Assert.Empty(same.RowTexts);
    }

    [AvaloniaFact]
    public void A_document_without_a_file_cannot_be_compared_with_it()
    {
        var tab = _w.NewDocument();
        tab.Editor!.Document.Text = "abc";
        _w.SetHex(tab.Doc, true);
        Assert.Null(_w.CompareHexWithSaved(_w.Active!));
        Assert.Contains(Loc.Get(K.HexCompareNoFile), _w.StatusLeftText);
        Assert.Empty(_w.CompareWindows);
    }
}

using System.Buffers.Binary;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>P8.10.1 in the window: the structure panel beside the hex grid and the selection of the bytes of a field.</summary>
[Collection("LocState")]
public class HexTemplateAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public HexTemplateAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _w.Width = 1200;
        _w.Height = 800;
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static byte[] Chunk(string type, byte[] body)
    {
        var bytes = new byte[12 + body.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)body.Length);
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        body.CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] Png(params byte[][] chunks) => [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, .. chunks.SelectMany(c => c)];

    private HexView OpenHex(string name, byte[] bytes)
    {
        _w.OpenFile(_temp.Bytes(name, bytes), null);
        TestApp.Press(_w, Key.K, TestApp.Mod);
        TestApp.Press(_w, Key.H, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        var hex = _w.Active!.Hex!;
        hex.Focus();
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return hex;
    }

    private static TreeViewItem Open(TreeViewItem item, int index)
    {
        item.IsExpanded = true;
        return (TreeViewItem)item.Items[index]!;
    }

    [AvaloniaFact]
    public void Selecting_a_field_in_the_tree_selects_its_bytes_in_the_grid()
    {
        var file = Png(Chunk("IHDR", new byte[13]), Chunk("IEND", []));
        var hex = OpenHex("pic.png", file);
        var panel = hex.Structure;
        Assert.False(panel.IsVisible);
        Assert.Equal(CommandOutcome.Handled, _w.ToggleHexTemplate());
        Assert.True(panel.IsVisible);
        Assert.Equal("png_chunks", panel.Current!.Id);

        var root = (TreeViewItem)panel.Tree.Items[0]!;
        Assert.True(root.IsExpanded);
        var chunks = (TreeViewItem)root.Items[1]!;
        var first = Open(chunks, 0);
        var type = Open(first, 1);

        panel.Tree.SelectedItem = type;
        Assert.Equal((12, 16), (hex.Editor.Selection.Start, hex.Editor.Selection.End));

        panel.Tree.SelectedItem = first;
        Assert.Equal((8, 8 + 25), (hex.Editor.Selection.Start, hex.Editor.Selection.End));

        panel.Tree.SelectedItem = root.Items[0];
        Assert.Equal((0, 8), (hex.Editor.Selection.Start, hex.Editor.Selection.End));
    }

    [AvaloniaFact]
    public void A_field_past_the_end_of_the_file_shows_its_reason_and_its_bytes()
    {
        var file = Png(Chunk("IDAT", [1, 2, 3, 4, 5]))[..^7];
        var hex = OpenHex("cut.png", file);
        _w.ToggleHexTemplate();
        var root = (TreeViewItem)hex.Structure.Tree.Items[0]!;
        var chunks = (TreeViewItem)root.Items[1]!;
        var node = (Core.Hex.Ksy.KsyNode)chunks.Tag!;
        Assert.Equal(Core.Hex.Ksy.KsyErrorKind.OutOfFile, node.Error!.Kind);
        Assert.Equal(file.Length, node.End);
        Assert.Contains(Loc.Get(K.KsyErrOutOfFile, ("subject", "body")), ((TextBlock)((StackPanel)chunks.Header!).Children[0]).Text);
    }

    [AvaloniaFact]
    public void A_file_no_template_fits_says_so_and_the_closed_panel_is_hidden_as_a_whole()
    {
        var hex = OpenHex("plain.dat", [1, 2, 3, 4]);
        _w.ToggleHexTemplate();
        var panel = hex.Structure;
        Assert.Null(panel.Current);
        Assert.Equal(Loc.Get(K.KsyNone), panel.NoteShown);
        Assert.Empty(panel.Tree.Items);

        panel.IsVisible = false;
        Assert.False(panel.IsVisible);
    }
}

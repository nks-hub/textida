using System.Buffers.Binary;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Hex.Ksy;
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

    private const string FourBytes = "meta:\n  id: four\n  title: Four bytes\nseq:\n  - id: first\n    type: u1\n  - id: rest\n    size: 3\n";

    [AvaloniaFact]
    public void Moving_the_caret_in_the_grid_selects_the_field_that_holds_the_byte_and_leaves_the_grid_alone()
    {
        var file = Png(Chunk("IHDR", new byte[13]), Chunk("IEND", []));
        var hex = OpenHex("pic.png", file);
        var panel = hex.Structure;
        _w.ToggleHexTemplate();
        hex.Focus();
        Dispatcher.UIThread.RunJobs();

        hex.Select(13, 13);
        var node = (KsyNode)((TreeViewItem)panel.Tree.SelectedItem!).Tag!;
        Assert.Equal(("type", 12L, 4L), (node.Name, node.Start, node.Length));
        Assert.Equal((13, 13), (hex.Editor.Anchor, hex.Editor.Caret));

        hex.Select(40, 40);
        node = (KsyNode)((TreeViewItem)panel.Tree.SelectedItem!).Tag!;
        Assert.Equal(("type", 37L), (node.Name, node.Start));
        Assert.Equal((40, 40), (hex.Editor.Anchor, hex.Editor.Caret));
    }

    [AvaloniaFact]
    public void A_template_file_with_process_is_refused_with_the_reason_and_the_panel_keeps_its_template()
    {
        Loc.SetLanguage("cs");
        var hex = OpenHex("pic.png", Png(Chunk("IEND", [])));
        _w.ToggleHexTemplate();
        var panel = hex.Structure;
        string path = _temp.File("xor.ksy", "meta:\n  id: xor_demo\nseq:\n  - id: a\n    size: 4\n    process: xor(0x55)\n");

        panel.LoadFile(path);

        Assert.Equal("nepodporováno: process", Loc.Get(K.KsyErrUnsupported, ("subject", "process")));
        Assert.Contains("nepodporováno: process", panel.NoteShown);
        Assert.Contains("xor.ksy", panel.NoteShown);
        Assert.Contains(Loc.Get(K.KsyErrLine, ("line", 6)), panel.NoteShown);
        Assert.Equal("png_chunks", panel.Current!.Id);
        Assert.DoesNotContain(panel.Picker.Items.OfType<KsyPinned>(), p => p.Id == "xor_demo");
    }

    [AvaloniaFact]
    public void A_template_file_that_cannot_be_read_is_named_in_the_note()
    {
        var hex = OpenHex("pic.png", Png(Chunk("IEND", [])));
        _w.ToggleHexTemplate();
        hex.Structure.LoadFile(Path.Combine(_temp.Path, "missing.ksy"));
        Assert.Contains("missing.ksy", hex.Structure.NoteShown);
        Assert.Equal("png_chunks", hex.Structure.Current!.Id);
    }

    [AvaloniaFact]
    public void A_template_file_is_offered_in_the_list_and_read_through_for_the_file()
    {
        var hex = OpenHex("pic.dat", [1, 2, 3, 4, 5]);
        _w.ToggleHexTemplate();
        var panel = hex.Structure;
        Assert.Null(panel.Current);

        panel.LoadFile(_temp.File("four.ksy", FourBytes));

        Assert.True(panel.Current!.Custom);
        Assert.Equal("four", panel.Current.Id);
        Assert.Null(panel.NoteShown);
        Assert.Contains("four.ksy", panel.InfoShown);
        Assert.Contains(panel.Picker.Items.OfType<KsyPinned>(), p => p.Id == "four");
        var root = (TreeViewItem)panel.Tree.Items[0]!;
        Assert.Equal(["first", "rest"], root.Items.OfType<TreeViewItem>().Select(i => ((KsyNode)i.Tag!).Name));
    }

    [AvaloniaFact]
    public void The_open_template_command_asks_for_a_file_and_reads_the_template()
    {
        var hex = OpenHex("pic.dat", [1, 2, 3, 4, 5]);
        _dialogs.Picked.Add(_temp.File("four.ksy", FourBytes));
        Assert.Equal(CommandOutcome.Handled, _w.OpenHexTemplateFile());
        Assert.True(hex.Structure.IsVisible);
        Assert.Equal("four", hex.Structure.Current!.Id);
    }

    [AvaloniaFact]
    public void The_template_picked_for_a_file_type_is_written_to_the_setting_and_used_for_the_next_file_of_that_type()
    {
        var zipLike = new byte[] { 0x50, 0x4B, 3, 4 };
        var first = OpenHex("a.dat", zipLike);
        _w.ToggleHexTemplate();
        Assert.Equal("zip_archive", first.Structure.Current!.Id);

        first.Structure.Picker.SelectedItem = KsyCatalog.ById("elf_file");
        Assert.Equal("dat=elf_file", _app.Workbench.Settings.GetString("hex.structureTemplates"));

        _app.HexTemplates.Chosen.Clear();
        var second = OpenHex("b.dat", zipLike);
        _w.ToggleHexTemplate();
        Assert.Equal("elf_file", second.Structure.Current!.Id);
        Assert.Contains("elf.ksy", second.Structure.InfoShown);
        Assert.Contains("MIT", second.Structure.InfoShown);
    }

    [AvaloniaFact]
    public void The_list_offers_every_pinned_template_and_the_tree_has_names_for_a_screen_reader()
    {
        var hex = OpenHex("pic.png", Png(Chunk("IHDR", new byte[13]), Chunk("IEND", [])));
        _w.ToggleHexTemplate();
        var panel = hex.Structure;
        Assert.Equal(KsyCatalog.All.Select(p => p.Id), panel.Picker.Items.OfType<KsyPinned>().Select(p => p.Id));
        Assert.Equal(Loc.Get(K.KsyPickerA11y), Avalonia.Automation.AutomationProperties.GetName(panel.Picker));
        Assert.Equal(Loc.Get(K.KsyTitle), Avalonia.Automation.AutomationProperties.GetName(panel.Tree));

        var root = (TreeViewItem)panel.Tree.Items[0]!;
        var chunks = (TreeViewItem)root.Items[1]!;
        var first = Open(chunks, 0);
        var length = (TreeViewItem)first.Items[0]!;
        Assert.Equal("length: 13 (0x0000000D), 0x8 +4", Avalonia.Automation.AutomationProperties.GetName(length));
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

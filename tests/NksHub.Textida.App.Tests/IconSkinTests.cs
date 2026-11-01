using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Icons;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.4.3: a bitmap skin read from a folder (pictures named like icons, a map, a strip with a transparent corner) and used by the icons of the commands.</summary>
[Collection("LocState")]
public class IconSkinTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly string _folder;

    public IconSkinTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _folder = Path.Combine(_temp.Path, "skin");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        IconSkin.Use("");
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    /// <summary>A 24-bit BMP of <paramref name="cells"/> square cells of <paramref name="size"/> pixels; every cell has its own colour, the background is magenta (the bottom left pixel).</summary>
    private static byte[] Strip(int cells, int size)
    {
        int width = cells * size, rowBytes = (width * 3 + 3) & ~3, dataSize = rowBytes * size;
        var file = new byte[54 + dataSize];
        BitConverter.GetBytes((ushort)0x4D42).CopyTo(file, 0);
        BitConverter.GetBytes(file.Length).CopyTo(file, 2);
        BitConverter.GetBytes(54).CopyTo(file, 10);
        BitConverter.GetBytes(40).CopyTo(file, 14);
        BitConverter.GetBytes(width).CopyTo(file, 18);
        BitConverter.GetBytes(size).CopyTo(file, 22);
        BitConverter.GetBytes((ushort)1).CopyTo(file, 26);
        BitConverter.GetBytes((ushort)24).CopyTo(file, 28);
        BitConverter.GetBytes(dataSize).CopyTo(file, 34);
        for (int row = 0; row < size; row++)
            for (int x = 0; x < width; x++)
            {
                int cell = x / size;
                bool inner = x % size is > 3 and < 12 && row is > 3 and < 12; // an inner square in the colour of the cell, magenta around it
                int at = 54 + row * rowBytes + x * 3;
                (file[at], file[at + 1], file[at + 2]) = inner ? ((byte)(40 * (cell + 1)), (byte)10, (byte)200) : ((byte)255, (byte)0, (byte)255);
            }
        return file;
    }

    private static void Png(string path)
    {
        var bitmap = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bitmap.Lock()) for (int i = 0; i < 16 * 16; i++) System.Runtime.InteropServices.Marshal.WriteInt32(fb.Address, i * 4, unchecked((int)0xFF2080D0));
        bitmap.Save(path);
    }

    private static uint PixelAt(SkinImage image, int x, int y)
    {
        var copy = new WriteableBitmap(image.Bitmap.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = copy.Lock();
        image.Bitmap.CopyPixels(new PixelRect(image.Bitmap.PixelSize), fb.Address, fb.RowBytes * fb.Size.Height, fb.RowBytes);
        return unchecked((uint)System.Runtime.InteropServices.Marshal.ReadInt32(fb.Address, (y + image.Source.Y) * fb.RowBytes + (x + image.Source.X) * 4));
    }

    [AvaloniaFact]
    public void Pictures_named_like_icons_are_the_skin_and_a_name_the_editor_does_not_know_is_reported()
    {
        Png(Path.Combine(_folder, "save.png"));
        Png(Path.Combine(_folder, "no-such-icon.png"));
        File.WriteAllText(Path.Combine(_folder, "readme.txt"), "not a picture");

        var skin = IconSkin.Load(_folder);

        Assert.Equal(["save"], skin.Images.Keys);
        Assert.Contains(skin.Problems, p => p.Contains("no-such-icon"));
        Assert.Equal(new PixelRect(0, 0, 16, 16), skin.Find("save")!.Source);
    }

    [AvaloniaFact]
    public void A_map_gives_another_file_name_and_a_strip_gives_cells_with_the_corner_colour_transparent()
    {
        Png(Path.Combine(_folder, "floppy.png"));
        File.WriteAllBytes(Path.Combine(_folder, "icons.bmp"), Strip(3, 16));
        File.WriteAllText(Path.Combine(_folder, "skin.json"), """
            { "map": { "save": "floppy.png" },
              "strips": [ { "file": "icons.bmp", "names": ["play", "hash", "nothing-here", "", "hash"], "transparent": "corner" } ] }
            """);

        var skin = IconSkin.Load(_folder);

        Assert.Equal(["hash", "play", "save"], skin.Images.Keys.Order());
        Assert.Equal(new PixelRect(16, 0, 16, 16), skin.Find("hash")!.Source);
        Assert.Equal(0u, PixelAt(skin.Find("hash")!, 0, 0)); // the magenta around the picture is transparent
        Assert.NotEqual(0u, PixelAt(skin.Find("hash")!, 8, 8) & 0xFF000000); // the picture itself is not
        Assert.Contains(skin.Problems, p => p.Contains("nothing-here"));
    }

    [AvaloniaFact]
    public void An_icon_with_a_picture_in_the_skin_is_drawn_from_it_and_one_without_stays_vector()
    {
        Png(Path.Combine(_folder, "save.png"));
        IconSkin.Use(_folder);
        Assert.NotNull(CommandIcons.Create(BuiltinIcons.All["save"]).SkinBitmap);
        Assert.Null(CommandIcons.Create(BuiltinIcons.All["upload"]).SkinBitmap);
        Assert.Equal("save", IconSkin.NameOf(BuiltinIcons.All["save"]));
        IconSkin.Use("");
        Assert.Null(CommandIcons.Create(BuiltinIcons.All["save"]).SkinBitmap);
    }

    [AvaloniaFact]
    public async Task The_command_takes_a_folder_with_icons_and_refuses_one_without()
    {
        _dialogs.Folders.Enqueue(_temp.Path); // nothing usable in it
        await _w.ChooseIconSkinAsync();
        Assert.Null(IconSkin.Current);
        Assert.Contains(_temp.Path, _w.StatusMessageText);

        Png(Path.Combine(_folder, "save.png"));
        _dialogs.Folders.Enqueue(_folder);
        await _w.ChooseIconSkinAsync();
        Assert.NotNull(IconSkin.Current);
        Assert.Equal(_folder, _app.Workbench.Settings.GetString("appearance.iconSkin"));
        Assert.Equal(Loc.Get(K.IconSkinUsed, ("icons", 1), ("problems", 0)), _w.StatusMessageText);

        _w.ResetIconSkin();
        Assert.Null(IconSkin.Current);
        Assert.Equal("", _app.Workbench.Settings.GetString("appearance.iconSkin"));
    }
}

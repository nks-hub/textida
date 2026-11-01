using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NksHub.Textida.Workbench.Icons;

namespace NksHub.Textida.App.Services;

/// <summary>One bitmap of a skin: the picture and the part of it that is the icon (a strip holds many).</summary>
internal sealed record SkinImage(Bitmap Bitmap, PixelRect Source);

/// <summary>
/// An optional bitmap skin for the icons of commands (PZ.4.3), mapped onto the names of the SVG icons of the editor (<see cref="BuiltinIcons.All"/>). A skin is a folder:
/// <list type="bullet">
/// <item>a picture named like the icon (<c>save.png</c>, also <c>.bmp</c>, <c>.gif</c>, <c>.jpg</c>) is the bitmap of that icon;</item>
/// <item>a file <c>skin.json</c> may add <c>"map"</c> (<c>{"save": "floppy.png"}</c>: another file name for an icon) and <c>"strips"</c>
/// (<c>[{"file": "icons.bmp", "names": ["new", "open", "save"], "transparent": "corner"}]</c>): one horizontal strip of square cells, the height of the strip is the size of a cell and the names are the cells in order.
/// With <c>"transparent": "corner"</c> the colour of the pixel at the bottom left corner is the transparent one, as in the strips of PSPad; a picture with an alpha channel needs nothing.</item>
/// </list>
/// A strip carries no names of its own, so the order is the skin's to state. A name that is not an icon of the editor is skipped and reported; an icon the skin does not have keeps its vector drawing.
/// </summary>
internal sealed class IconSkin
{
    private static readonly string[] PictureExtensions = [".png", ".bmp", ".gif", ".jpg", ".jpeg"];
    private static readonly Dictionary<IconShape, string> s_names = Names();

    private static Dictionary<IconShape, string> Names()
    {
        var names = new Dictionary<IconShape, string>((IEqualityComparer<IconShape>)ReferenceEqualityComparer.Instance);
        foreach (var (name, shape) in BuiltinIcons.All) names.TryAdd(shape, name);
        return names;
    }

    private readonly Dictionary<string, SkinImage> _images = new(StringComparer.Ordinal);

    /// <summary>The skin in use, or null for the vector icons.</summary>
    public static IconSkin? Current { get; private set; }

    public string Folder { get; private init; } = "";
    public IReadOnlyDictionary<string, SkinImage> Images => _images;
    public List<string> Problems { get; } = [];

    /// <summary>The SVG icon name of a shape of the editor, or null for a shape of a plugin.</summary>
    public static string? NameOf(IconShape shape) => s_names.GetValueOrDefault(shape);

    public SkinImage? Find(string name) => _images.GetValueOrDefault(name);

    /// <summary>Makes the skin of <paramref name="folder"/> the one in use (empty or unreadable: none). Returns it with its problems.</summary>
    public static IconSkin? Use(string? folder)
    {
        Current = string.IsNullOrWhiteSpace(folder) ? null : Load(folder);
        return Current;
    }

    /// <summary>Reads a skin folder; never throws. A skin with no usable icon is still returned, with the reasons in <see cref="Problems"/>.</summary>
    public static IconSkin Load(string folder)
    {
        var skin = new IconSkin { Folder = folder };
        try
        {
            if (!Directory.Exists(folder)) { skin.Problems.Add("The folder does not exist."); return skin; }
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var strips = new List<(string File, List<string> Names, bool Corner)>();
            string manifest = Path.Combine(folder, "skin.json");
            if (File.Exists(manifest))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifest), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (doc.RootElement.TryGetProperty("map", out var m) && m.ValueKind == JsonValueKind.Object)
                    foreach (var p in m.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.String) map[p.Name] = p.Value.GetString()!;
                if (doc.RootElement.TryGetProperty("strips", out var s) && s.ValueKind == JsonValueKind.Array)
                    foreach (var strip in s.EnumerateArray())
                        if (strip.TryGetProperty("file", out var f) && f.ValueKind == JsonValueKind.String && strip.TryGetProperty("names", out var n) && n.ValueKind == JsonValueKind.Array)
                            strips.Add((f.GetString()!, [.. n.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)],
                                strip.TryGetProperty("transparent", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == "corner"));
            }
            foreach (string file in Directory.EnumerateFiles(folder).Where(f => PictureExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (strips.Any(s => string.Equals(s.File, Path.GetFileName(file), StringComparison.OrdinalIgnoreCase)) || map.ContainsValue(Path.GetFileName(file))) continue;
                skin.AddFile(name, file, false);
            }
            foreach (var (name, file) in map) skin.AddFile(name, Path.Combine(folder, file), false);
            foreach (var (file, names, corner) in strips) skin.AddStrip(Path.Combine(folder, file), names, corner);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            skin.Problems.Add(e.Message);
        }
        return skin;
    }

    private bool Known(string name)
    {
        if (BuiltinIcons.All.ContainsKey(name)) return true;
        Problems.Add($"'{name}' is not the name of an icon of the editor.");
        return false;
    }

    private Bitmap? Read(string file)
    {
        try
        {
            if (new FileInfo(file).Length > 16 << 20) { Problems.Add($"{Path.GetFileName(file)} is larger than 16 MiB."); return null; }
            using var stream = File.OpenRead(file);
            return new Bitmap(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Problems.Add($"{Path.GetFileName(file)}: {e.Message}");
            return null;
        }
    }

    private void AddFile(string name, string file, bool corner)
    {
        if (!Known(name) || Read(file) is not { } bitmap) return;
        _images[name] = new SkinImage(corner ? KeyCorner(bitmap) : bitmap, new PixelRect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height));
    }

    private void AddStrip(string file, List<string> names, bool corner)
    {
        if (Read(file) is not { } read) return;
        var bitmap = corner ? KeyCorner(read) : read;
        int cell = bitmap.PixelSize.Height;
        if (cell <= 0 || bitmap.PixelSize.Width < cell) { Problems.Add($"{Path.GetFileName(file)} is not a strip of square cells."); return; }
        if (names.Count > bitmap.PixelSize.Width / cell) Problems.Add($"{Path.GetFileName(file)} has {bitmap.PixelSize.Width / cell} cells and the skin names {names.Count}.");
        for (int i = 0; i < names.Count && (i + 1) * cell <= bitmap.PixelSize.Width; i++)
            if (names[i].Length > 0 && Known(names[i])) _images[names[i]] = new SkinImage(bitmap, new PixelRect(i * cell, 0, cell, cell));
    }

    /// <summary>A copy of the picture in which every pixel of the colour of its bottom left corner is transparent.</summary>
    internal static Bitmap KeyCorner(Bitmap source)
    {
        var size = source.PixelSize;
        var result = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = result.Lock();
        source.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), buffer.Address, buffer.RowBytes * size.Height, buffer.RowBytes);
        int stride = buffer.RowBytes;
        var pixels = new byte[stride * size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        int corner = (size.Height - 1) * stride;
        uint key = BitConverter.ToUInt32(pixels, corner) | 0xFF000000;
        for (int y = 0; y < size.Height; y++)
            for (int x = 0; x < size.Width; x++)
            {
                int at = y * stride + x * 4;
                if ((BitConverter.ToUInt32(pixels, at) | 0xFF000000) == key) pixels[at] = pixels[at + 1] = pixels[at + 2] = pixels[at + 3] = 0;
            }
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        return result;
    }
}

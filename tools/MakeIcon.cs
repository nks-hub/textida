#:package SkiaSharp@3.119.4
#:project ../src/NksHub.Textida.Install/NksHub.Textida.Install.csproj
// Draws the Textida icon: a teal rounded tile with a white T whose stem stands left, two lines of text and a caret beside it.
// Small sizes (24 px and less) get the T alone, drawn thicker, so it stays readable.
//
//   dotnet run tools/MakeIcon.cs -- <output folder>
//
// Writes textida.ico (16-256 px, PNG entries), textida-256.png, textida-1024.png and textida.icns (16-1024 px with the @2x sizes, PNG entries; the macOS package takes it as Textida.icns).
// Next to them, in the folder FileTypes, one document icon per group of NksHub.Textida.Install.FileTypeIcons: {id}.ico (16, 32, 48, 256 px), {id}-48.png, {id}-256.png and {id}.svg for the hicolor theme
// of Linux, and {id}.icns for the macOS bundle (Textida-{id}.icns there; not checked in, the package build draws it).
using NksHub.Textida.Install;
using SkiaSharp;

string output = args.Length > 0 ? args[0] : Path.Combine("src", "NksHub.Textida.App", "Assets");
Directory.CreateDirectory(output);

int[] icoSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
var entries = icoSizes.Select(s => (Size: s, Png: Render(s))).ToList();
File.WriteAllBytes(Path.Combine(output, "textida.ico"), Ico(entries));
File.WriteAllBytes(Path.Combine(output, "textida-256.png"), Render(256));
File.WriteAllBytes(Path.Combine(output, "textida-1024.png"), Render(1024));
File.WriteAllBytes(Path.Combine(output, "textida.icns"), Icns(Render));

// the band color of each group; the page, its fold and the outline are the teal of the application icon
var bandColors = new Dictionary<string, SKColor>
{
    ["md"] = new SKColor(0x3B, 0x6F, 0xD8), ["data"] = new SKColor(0xD9, 0x8A, 0x0B), ["text"] = new SKColor(0x6B, 0x72, 0x80), ["log"] = new SKColor(0x7A, 0x5A, 0xC8),
    ["web"] = new SKColor(0xE0, 0x56, 0x2B), ["code"] = new SKColor(0x1F, 0x9D, 0x55), ["sql"] = new SKColor(0xB8, 0x3A, 0x8A), ["config"] = new SKColor(0x8A, 0x6B, 0x3F),
};
var types = Path.Combine(output, "FileTypes");
Directory.CreateDirectory(types);
foreach (var g in FileTypeIcons.Groups)
{
    var color = bandColors[g.Id];
    File.WriteAllBytes(Path.Combine(types, g.Id + ".ico"), Ico(new[] { 16, 32, 48, 256 }.Select(n => (n, RenderDocument(n, g.Label, color))).ToList()));
    File.WriteAllBytes(Path.Combine(types, g.Id + "-48.png"), RenderDocument(48, g.Label, color));
    File.WriteAllBytes(Path.Combine(types, g.Id + "-256.png"), RenderDocument(256, g.Label, color));
    File.WriteAllText(Path.Combine(types, g.Id + ".svg"), DocumentSvg(g.Label, color));
    File.WriteAllBytes(Path.Combine(types, g.Id + ".icns"), Icns(n => RenderDocument(n, g.Label, color)));
}
Console.WriteLine($"icon written to {Path.GetFullPath(output)}");

static byte[] Render(int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var c = surface.Canvas;
    c.Clear(SKColors.Transparent);
    float s = size;
    bool small = size <= 24;

    // tile: D5 accent (#0F7B6C) lightened at the top, deepened at the bottom
    float inset = small ? 0 : s * 0.03f;
    var tile = new SKRect(inset, inset, s - inset, s - inset);
    float radius = s * (small ? 0.18f : 0.22f);
    using (var fill = new SKPaint { IsAntialias = true })
    {
        fill.Shader = SKShader.CreateLinearGradient(new SKPoint(0, tile.Top), new SKPoint(0, tile.Bottom),
            [new SKColor(0x16, 0x9A, 0x86), new SKColor(0x0F, 0x7B, 0x6C), new SKColor(0x0A, 0x5A, 0x50)], [0f, 0.55f, 1f], SKShaderTileMode.Clamp);
        c.DrawRoundRect(tile, radius, radius, fill);
    }
    if (!small)
    {
        // a soft light along the top edge gives the tile some depth
        using var gloss = new SKPaint { IsAntialias = true };
        gloss.Shader = SKShader.CreateLinearGradient(new SKPoint(0, tile.Top), new SKPoint(0, tile.Top + s * 0.45f),
            [new SKColor(255, 255, 255, 46), new SKColor(255, 255, 255, 0)], SKShaderTileMode.Clamp);
        c.DrawRoundRect(tile, radius, radius, gloss);
    }

    using var white = new SKPaint { IsAntialias = true, Color = SKColors.White, StrokeCap = SKStrokeCap.Round, Style = SKPaintStyle.Stroke };
    if (small)
    {
        white.StrokeWidth = s * 0.19f;
        c.DrawLine(s * 0.24f, s * 0.29f, s * 0.76f, s * 0.29f, white);
        c.DrawLine(s * 0.50f, s * 0.29f, s * 0.50f, s * 0.78f, white);
    }
    else
    {
        white.StrokeWidth = s * 0.115f;
        c.DrawLine(s * 0.22f, s * 0.29f, s * 0.78f, s * 0.29f, white); // crossbar
        c.DrawLine(s * 0.36f, s * 0.29f, s * 0.36f, s * 0.76f, white); // stem
        using var text = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 215), StrokeCap = SKStrokeCap.Round, Style = SKPaintStyle.Stroke, StrokeWidth = s * 0.075f };
        c.DrawLine(s * 0.53f, s * 0.50f, s * 0.78f, s * 0.50f, text);
        c.DrawLine(s * 0.53f, s * 0.66f, s * 0.66f, s * 0.66f, text);
        using var caret = new SKPaint { IsAntialias = true, Color = new SKColor(0x9C, 0xF5, 0xE4), StrokeCap = SKStrokeCap.Round, Style = SKPaintStyle.Stroke, StrokeWidth = s * 0.04f };
        c.DrawLine(s * 0.745f, s * 0.585f, s * 0.745f, s * 0.735f, caret);
    }

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// Page geometry shared by the PNG and the SVG, in units of the icon size
static (float L, float T, float R, float B, float Fold, float BandTop, float BandBottom) Page(bool small) =>
    small ? (0.17f, 0.04f, 0.83f, 0.96f, 0.22f, 0.50f, 0.84f) : (0.17f, 0.06f, 0.83f, 0.94f, 0.22f, 0.52f, 0.80f);

// A document icon: white page with the teal outline and fold of the application icon, two lines of text and a band of the group color with the label.
// Small sizes (24 px and less) leave out the lines and the label; the color of the band tells the groups apart.
static byte[] RenderDocument(int size, string label, SKColor band)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var c = surface.Canvas;
    c.Clear(SKColors.Transparent);
    float s = size;
    bool small = size <= 24;
    var (l, t, r, b, fold, bandTop, bandBottom) = Page(small);
    l *= s; t *= s; r *= s; b *= s; fold *= s; bandTop *= s; bandBottom *= s;
    var teal = new SKColor(0x0F, 0x7B, 0x6C);
    float outline = Math.Max(1f, s * 0.035f);

    using var page = new SKPath();
    page.MoveTo(l, t); page.LineTo(r - fold, t); page.LineTo(r, t + fold); page.LineTo(r, b); page.LineTo(l, b); page.Close();
    using var round = SKPathEffect.CreateCorner(s * 0.05f);
    using (var fill = new SKPaint { IsAntialias = true, PathEffect = round })
    {
        fill.Shader = SKShader.CreateLinearGradient(new SKPoint(0, t), new SKPoint(0, b), [SKColors.White, new SKColor(0xE3, 0xF0, 0xED)], SKShaderTileMode.Clamp);
        c.DrawPath(page, fill);
    }
    using (var edge = new SKPaint { IsAntialias = true, PathEffect = round, Style = SKPaintStyle.Stroke, StrokeWidth = outline, Color = teal, StrokeJoin = SKStrokeJoin.Round })
        c.DrawPath(page, edge);
    using (var corner = new SKPath())
    {
        corner.MoveTo(r - fold, t); corner.LineTo(r - fold, t + fold); corner.LineTo(r, t + fold); corner.Close();
        using var cornerFill = new SKPaint { IsAntialias = true, Color = new SKColor(0x9C, 0xD6, 0xCB) };
        c.DrawPath(corner, cornerFill);
        using var cornerEdge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = outline, Color = teal, StrokeJoin = SKStrokeJoin.Round };
        c.DrawPath(corner, cornerEdge);
    }

    float inset = outline * 1.5f;
    using (var fill = new SKPaint { IsAntialias = true, Color = band })
        c.DrawRect(new SKRect(l + inset, bandTop, r - inset, bandBottom), fill);

    if (!small)
    {
        using var text = new SKPaint { IsAntialias = true, Color = new SKColor(0x9A, 0xB5, 0xB0), StrokeCap = SKStrokeCap.Round, Style = SKPaintStyle.Stroke, StrokeWidth = s * 0.035f };
        c.DrawLine(l + s * 0.09f, s * 0.35f, r - s * 0.16f, s * 0.35f, text);
        c.DrawLine(l + s * 0.09f, s * 0.44f, r - s * 0.26f, s * 0.44f, text);
        using var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, (bandBottom - bandTop) * 0.62f);
        float room = r - l - 2 * inset - s * 0.06f;
        float width = font.MeasureText(label);
        if (width > room) font.Size *= room / width;
        using var white = new SKPaint { IsAntialias = true, Color = SKColors.White };
        font.GetFontMetrics(out var metrics);
        float baseline = (bandTop + bandBottom) / 2 - (metrics.Ascent + metrics.Descent) / 2;
        c.DrawText(label, (l + r) / 2, baseline, SKTextAlign.Center, font, white);
    }

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// The same icon as vector graphics for the scalable folder of the hicolor theme, drawn in a 256 x 256 box
static string DocumentSvg(string label, SKColor band)
{
    var (l, t, r, b, fold, bandTop, bandBottom) = Page(small: false);
    static string N(float v) => (v * 256).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    const float outline = 256 * 0.035f;
    const float inset = outline * 1.5f;
    float fontSize = (bandBottom - bandTop) * 256 * 0.62f;
    float room = (r - l) * 256 - 2 * inset - 256 * 0.06f;
    string textLength = Math.Min(room, label.Length * fontSize * 0.72f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    string stroke = outline.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    string color = $"#{band.Red:X2}{band.Green:X2}{band.Blue:X2}";
    return $"""
        <svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
          <defs><linearGradient id="page" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#FFFFFF"/><stop offset="1" stop-color="#E3F0ED"/></linearGradient></defs>
          <path d="M{N(l)} {N(t)}H{N(r - fold)}L{N(r)} {N(t + fold)}V{N(b)}H{N(l)}Z" fill="url(#page)" stroke="#0F7B6C" stroke-width="{stroke}" stroke-linejoin="round"/>
          <path d="M{N(r - fold)} {N(t)}V{N(t + fold)}H{N(r)}Z" fill="#9CD6CB" stroke="#0F7B6C" stroke-width="{stroke}" stroke-linejoin="round"/>
          <path d="M{N(l + 0.09f)} {N(0.35f)}H{N(r - 0.16f)}M{N(l + 0.09f)} {N(0.44f)}H{N(r - 0.26f)}" stroke="#9AB5B0" stroke-width="8.96" stroke-linecap="round"/>
          <rect x="{(l * 256 + inset).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}" y="{N(bandTop)}" width="{((r - l) * 256 - 2 * inset).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}" height="{N(bandBottom - bandTop)}" fill="{color}"/>
          <text x="{N((l + r) / 2)}" y="{((bandTop + bandBottom) / 2 * 256 + fontSize * 0.35f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}" font-family="Arial, Helvetica, sans-serif" font-weight="bold" font-size="{fontSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}" text-anchor="middle" fill="#FFFFFF" textLength="{textLength}" lengthAdjust="spacingAndGlyphs">{label}</text>
        </svg>

        """;
}

// ICO container with PNG entries (Windows Vista and later read them at every size)
static byte[] Ico(List<(int Size, byte[] Png)> entries)
{
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)entries.Count);
    int offset = 6 + 16 * entries.Count;
    foreach (var (size, png) in entries)
    {
        w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
        w.Write(png.Length); w.Write(offset);
        offset += png.Length;
    }
    foreach (var (_, png) in entries) w.Write(png);
    return ms.ToArray();
}

// ICNS container with PNG entries: "icns" and the total length, then for each size a four-letter type, the length of the entry and the PNG. All numbers are big-endian.
// The types are those of iconutil: icp4 16, icp5 32, icp6 64, ic07 128, ic08 256, ic09 512, ic10 1024 and, for the @2x sizes, ic11 32 (16@2x), ic12 64 (32@2x), ic13 256 (128@2x), ic14 512 (256@2x).
static byte[] Icns(Func<int, byte[]> render)
{
    (string Type, int Size)[] kinds = [("icp4", 16), ("icp5", 32), ("icp6", 64), ("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024), ("ic11", 32), ("ic12", 64), ("ic13", 256), ("ic14", 512)];
    var pngs = kinds.Select(k => k.Size).Distinct().ToDictionary(size => size, render);
    using var ms = new MemoryStream();
    void Int(int value) => ms.Write([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
    ms.Write("icns"u8);
    Int(8 + kinds.Sum(k => 8 + pngs[k.Size].Length));
    foreach (var (type, size) in kinds)
    {
        ms.Write(System.Text.Encoding.ASCII.GetBytes(type));
        Int(8 + pngs[size].Length);
        ms.Write(pngs[size]);
    }
    return ms.ToArray();
}

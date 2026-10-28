#:package SkiaSharp@3.119.4
// Draws the Textida icon: a teal rounded tile with a white T whose stem stands left, two lines of text and a caret beside it.
// Small sizes (24 px and less) get the T alone, drawn thicker, so it stays readable.
//
//   dotnet run tools/MakeIcon.cs -- <output folder>
//
// Writes textida.ico (16-256 px, PNG entries), textida-256.png and textida-1024.png.
using SkiaSharp;

string output = args.Length > 0 ? args[0] : Path.Combine("src", "NksHub.Textida.App", "Assets");
Directory.CreateDirectory(output);

int[] icoSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
var entries = icoSizes.Select(s => (Size: s, Png: Render(s))).ToList();
File.WriteAllBytes(Path.Combine(output, "textida.ico"), Ico(entries));
File.WriteAllBytes(Path.Combine(output, "textida-256.png"), Render(256));
File.WriteAllBytes(Path.Combine(output, "textida-1024.png"), Render(1024));
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

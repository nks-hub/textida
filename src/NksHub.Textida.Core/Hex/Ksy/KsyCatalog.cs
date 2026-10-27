namespace NksHub.Textida.Core.Hex.Ksy;

/// <summary>A template shipped with the program. The files are written for Textida, so the source is this repository and the licence is its licence.</summary>
public sealed record KsyPinned(string Id, string File, string Version, string License, KsyTemplate Template);

/// <summary>The templates that are shipped, and the choice of one for a file by its extension and its first bytes (P8.10.5).</summary>
public static class KsyCatalog
{
    private static readonly string[] Files = ["bmp.ksy", "png.ksy"];
    private static IReadOnlyList<KsyPinned>? _all;

    public static IReadOnlyList<KsyPinned> All => _all ??= [.. Files.Select(Load)];

    private static KsyPinned Load(string file)
    {
        using var stream = typeof(KsyCatalog).Assembly.GetManifestResourceStream("Ksy/" + file) ?? throw new FileNotFoundException("Missing embedded template " + file);
        using var reader = new StreamReader(stream);
        if (!KsyParser.TryParse(reader.ReadToEnd(), out var template, out var error))
            throw new InvalidDataException($"Embedded template {file} line {error!.Line}: {error.Kind} {error.Subject}");
        return new KsyPinned(template!.Id, file, "1", "MIT", template);
    }

    /// <summary>
    /// The template for a file: the one whose magic number is at the start of <paramref name="header"/>, otherwise the one that lists the extension of
    /// <paramref name="path"/>; null when none fits.
    /// </summary>
    public static KsyPinned? Find(string? path, ReadOnlySpan<byte> header)
    {
        foreach (var pinned in All)
            if (pinned.Template.Magic is { } magic && header.StartsWith(magic)) return pinned;
        string ext = Path.GetExtension(path ?? "").TrimStart('.');
        return ext.Length == 0 ? null : All.FirstOrDefault(p => p.Template.Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase));
    }
}

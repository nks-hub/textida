using System.Text;
using NksHub.Textida.Core.Data;

namespace NksHub.Textida.Core.Hex.Ksy;

/// <summary>
/// A template of the list of pinned files (<c>pinned.yaml</c>, P8.10.5): the file, the version of the pinned copy, the licence it is shipped under, where it comes from and the constructs it uses.
/// <see cref="Custom"/> is set for a template the user opened from a file; it has none of the other facts.
/// </summary>
public sealed record KsyPinned(string Id, string File, string Version, string License, string Source, IReadOnlyList<string> Constructs, KsyTemplate Template)
{
    public bool Custom { get; init; }
}

/// <summary>
/// The templates that are shipped, and the choice of one for a file by its extension and its first bytes (P8.10.5). The shipped files are listed in <c>Templates/pinned.yaml</c>, each with a version,
/// a licence from <see cref="AllowedLicenses"/>, its source and the constructs it uses; a file that is not listed there, or a listed one that does not parse or has another licence, is not offered
/// and is named in <see cref="Problems"/>.
/// </summary>
public static class KsyCatalog
{
    /// <summary>The licences a shipped template may have. Kaitai Struct's GPL-3 compiler is not shipped, and no template under another licence is.</summary>
    public static IReadOnlyList<string> AllowedLicenses { get; } = ["CC0-1.0", "MIT", "BSD-2-Clause", "BSD-3-Clause"];

    private static readonly Lazy<(IReadOnlyList<KsyPinned> Templates, IReadOnlyList<string> Problems)> Loaded = new(Build);

    public static IReadOnlyList<KsyPinned> All => Loaded.Value.Templates;

    /// <summary>What is wrong with the list of pinned files or with a file on it, as plain text for a developer; empty when everything is fine.</summary>
    public static IReadOnlyList<string> Problems => Loaded.Value.Problems;

    private static string? ReadResource(string file)
    {
        using var stream = typeof(KsyCatalog).Assembly.GetManifestResourceStream("Ksy/" + file);
        if (stream is null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static (IReadOnlyList<KsyPinned>, IReadOnlyList<string>) Build()
    {
        var templates = new List<KsyPinned>();
        var problems = new List<string>();
        string? manifest = ReadResource("pinned.yaml");
        if (manifest is null)
        {
            problems.Add("pinned.yaml is missing");
            return (templates, problems);
        }
        if (!YamlDataReader.TryParse(manifest, 8, out var root, out var syntax) || root!.Kind != DataKind.Array)
        {
            problems.Add("pinned.yaml: " + (syntax?.Message ?? "a list is expected"));
            return (templates, problems);
        }
        foreach (var entry in root.Items)
        {
            string file = Text(entry, "file");
            string license = Text(entry, "license");
            if (file == "" || entry.Get("version") is null || license == "" || Text(entry, "source") == "")
            {
                problems.Add($"pinned.yaml: an entry lacks file, version, licence or source ({file})");
                continue;
            }
            if (!AllowedLicenses.Contains(license))
            {
                problems.Add($"{file}: licence {license} is not allowed");
                continue;
            }
            string? text = ReadResource(file);
            if (text is null)
            {
                problems.Add($"{file}: the file is not embedded");
                continue;
            }
            if (!KsyParser.TryParse(text, out var template, out var error))
            {
                problems.Add($"{file}: line {error!.Line} {error.Kind} {error.Subject}");
                continue;
            }
            var listed = entry.Get("constructs") is { Kind: DataKind.Array } c ? c.Items.Select(i => i.Text).ToList() : [];
            templates.Add(new KsyPinned(template!.Id, file, Text(entry, "version"), license, Text(entry, "source"), listed, template));
        }
        return (templates, problems);
    }

    private static string Text(DataNode entry, string key) => entry.Get(key) is { Kind: DataKind.String or DataKind.Number } n ? n.Text : "";

    /// <summary>Reads a template the user opened from a file; the error says what is wrong and on which line.</summary>
    public static bool TryParseCustom(string text, string file, out KsyPinned? pinned, out KsyError? error)
    {
        pinned = null;
        if (!KsyParser.TryParse(text, out var template, out error)) return false;
        pinned = new KsyPinned(template!.Id, file, "", "", "", template.Constructs, template) { Custom = true };
        return true;
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

    /// <summary>The pinned template with this id, or null.</summary>
    public static KsyPinned? ById(string id) => All.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// The choices of a template per file type, written <c>extension=template</c> and separated by commas or semicolons (the setting <c>hex.structureTemplates</c>). An entry that is not of that form is skipped.
    /// Extensions are lowercase without the dot.
    /// </summary>
    public static Dictionary<string, string> ParseChoices(string? text)
    {
        var choices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in (text ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            if (eq < 0) continue;
            string ext = part[..eq].Trim().TrimStart('.').ToLowerInvariant(), id = part[(eq + 1)..].Trim();
            if (ext.Length > 0 && id.Length > 0) choices[ext] = id;
        }
        return choices;
    }

    public static string FormatChoices(IReadOnlyDictionary<string, string> choices) =>
        string.Join(", ", choices.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key.ToLowerInvariant() + "=" + c.Value));
}

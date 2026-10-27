using System.IO.Enumeration;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Workbench.Clips;

namespace NksHub.Textida.PluginHost.Conversions;

/// <summary>
/// A recipe in a file of its own (the <c>recipes</c> folder of the profile). The fields are those of <see cref="RecipeContribution"/> from a plugin manifest, so a recipe saved by the first
/// recipe version loads as it is: a file without <see cref="Version"/> is version 1. Version 2 adds <see cref="Converters"/>, the converters the <c>convert</c> steps name (a plugin's recipe uses
/// the converters of its plugin), and the <c>format</c> step.
/// </summary>
public sealed class RecipeFile
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Command { get; set; }
    public List<RecipeStep> Steps { get; set; } = [];
    public List<ConverterContribution> Converters { get; set; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public RecipeContribution Recipe => new() { Id = Id, Title = Title, Command = Command, Steps = Steps };

    public static RecipeFile Of(RecipeContribution recipe, IEnumerable<ConverterContribution> converters) =>
        new() { Version = CurrentVersion, Id = recipe.Id, Title = recipe.Title, Command = recipe.Command, Steps = recipe.Steps, Converters = [.. converters] };

    public string ToJson() => JsonSerializer.Serialize(this, PluginManifestJson.Default.RecipeFile);

    /// <summary>Reads and checks a recipe file; null with the reason in <paramref name="error"/> when it is not valid, from a newer version or damaged.</summary>
    public static RecipeFile? Parse(string json, out string? error)
    {
        error = null;
        RecipeFile? file;
        try { file = JsonSerializer.Deserialize(json, PluginManifestJson.Default.RecipeFile); }
        catch (JsonException e)
        {
            error = e.Message;
            return null;
        }
        if (file is null) { error = "the file is empty"; return null; }
        if (file.Version is < 1 or > CurrentVersion) { error = $"version {file.Version} is not supported (this editor reads 1 to {CurrentVersion})"; return null; }
        if (file.Id.Length == 0 || file.Title.Length == 0) { error = "id and title are needed"; return null; }
        var problems = RecipeRunner.Check(file.Recipe, file.Converters.Select(c => c.Id).ToHashSet()).Concat(file.Converters.SelectMany(TextConversion.Check)).ToList();
        if (problems.Count > 0) { error = string.Join(" ", problems); return null; }
        return file;
    }
}

/// <summary>
/// Runs a recipe over a set of files and open documents. The text steps are those of <see cref="RecipeRunner"/> (every file is its own whole text); a <c>format</c> step runs a formatter of
/// <see cref="ConfigFormatters"/> over the result, as <see cref="ConfigFormatters.FormatBytesAsync"/> does for a save. The result is the proposal of <see cref="FileSetPlanner"/>, so it is previewed,
/// checked against the revision of every file and applied by the same change service as Replace in Files.
/// </summary>
public static class RecipeBatch
{
    /// <param name="context">The facts the variables come from; each file gets its own path in them.</param>
    /// <param name="formatTimeout">The time a formatter has per file; null while nobody has set it, and then a format step fails for every file with a reason.</param>
    /// <param name="formatters">Finds a formatter by id; the catalog of <see cref="ConfigFormatters"/> when null.</param>
    public static Task<ReplacePlan> PlanAsync(RecipeContribution recipe, IReadOnlyList<ConverterContribution> converters, IEnumerable<string> paths, IEnumerable<LiveBuffer> buffers,
        VariableContext context, TimeSpan? formatTimeout, Func<string, FormatterDef?>? formatters = null,
        int parallelism = 4, CancellationToken ct = default)
    {
        formatters ??= ConfigFormatters.Find;
        var text = new RecipeContribution { Id = recipe.Id, Title = recipe.Title, Steps = [.. recipe.Steps.Where(s => s.Kind != RecipeStep.Format)] };
        var formats = recipe.Steps.Where(s => s.Kind == RecipeStep.Format).ToList();

        async Task<FileSetEdits> Produce(FileSetText file, CancellationToken token)
        {
            var result = RecipeRunner.Run(text, converters, file.Text, [], context.WithFile(file.Path), file.NewLine);
            if (result.Problem is { } why) return new([], why);
            var edits = result.Changes.Select(c => (c.Start, c.Start + c.Length, c.Text)).ToList();
            if (formats.Count == 0) return new(edits);

            string current = Apply(file.Text, edits);
            foreach (var step in formats)
            {
                var (formatted, problem) = await Format(file, current, step.Formatter, formatTimeout, formatters, token);
                if (problem is not null) return new([], problem);
                current = formatted!;
            }
            return new(Narrow(file.Text, current));
        }

        return FileSetPlanner.PlanAsync($"Recipe '{recipe.Title}'", "recipe:" + recipe.Id, paths, buffers, Produce, parallelism, ct);
    }

    private static async Task<(string? Text, string? Problem)> Format(FileSetText file, string text, string id, TimeSpan? timeout, Func<string, FormatterDef?> formatters, CancellationToken ct)
    {
        if (formatters(id) is not { } def) return (null, $"unknown formatter '{id}'");
        if (timeout is not { } limit) return (null, "no time limit is set for formatters (formatTimeoutSeconds in saveActions.json)");
        if (file.Path is null) return (null, "a document without a file cannot be formatted");
        if (!def.Extensions.Contains(Path.GetExtension(file.Path), StringComparer.OrdinalIgnoreCase)) return (null, $"{def.Title} does not format {Path.GetExtension(file.Path)} files");
        if (file.HasInvalidBytes) return (null, "the file has bytes that are not valid " + file.Codec.Name + ", so a formatter would change them");

        byte[] bom = file.BomLength > 0 ? file.Codec.Bom : [];
        byte[] before;
        try { before = [.. bom, .. file.Codec.Encode(text)]; }
        catch (UnrepresentableCharactersException) { return (null, "the new text has characters that cannot be written in " + file.Codec.Name); }

        var (outcome, after) = await ConfigFormatters.FormatBytesAsync(file.Path, before, def, limit, ct);
        if (outcome.Result == FormatResult.Unchanged) return (text, null);
        if (after is null) return (null, outcome.Reason);
        if (after.Length < bom.Length || !after.AsSpan(0, bom.Length).SequenceEqual(bom)) return (null, $"{def.Title} changed the byte order mark");
        var decoded = file.Codec.Decode(after);
        return decoded.InvalidCount > 0 ? (null, $"{def.Title} wrote bytes that are not valid {file.Codec.Name}") : (decoded.Text, null);
    }

    private static string Apply(string text, IReadOnlyList<(int Start, int End, string NewText)> edits)
    {
        var sb = new StringBuilder(text.Length);
        int at = 0;
        foreach (var (start, end, newText) in edits)
        {
            sb.Append(text, at, start - at).Append(newText);
            at = end;
        }
        return sb.Append(text, at, text.Length - at).ToString();
    }

    /// <summary>The one edit, from line start to line start, that turns <paramref name="before"/> into <paramref name="after"/>; none when they are equal. A formatter's result is previewed as one change.</summary>
    internal static IReadOnlyList<(int Start, int End, string NewText)> Narrow(string before, string after)
    {
        if (before == after) return [];
        static bool LineStart(string s, int i) => i == 0 || s[i - 1] == '\n' || (s[i - 1] == '\r' && (i >= s.Length || s[i] != '\n'));
        int head = 0, limit = Math.Min(before.Length, after.Length);
        while (head < limit && before[head] == after[head]) head++;
        while (head > 0 && !(LineStart(before, head) && LineStart(after, head))) head--;
        int tail = 0;
        while (tail < limit - head && before[^(tail + 1)] == after[^(tail + 1)]) tail++;
        while (tail > 0 && !(LineStart(before, before.Length - tail) && LineStart(after, after.Length - tail))) tail--;
        return [(head, before.Length - tail, after.Substring(head, after.Length - tail - head))];
    }

    /// <summary>The files under <paramref name="folder"/> whose name matches one of the masks (<c>*.cs;*.txt</c>, separated by <c>;</c> or a comma; empty means every file), in the order of the search walk with its ignore rules.</summary>
    public static IReadOnlyList<string> FolderFiles(string folder, string mask, CancellationToken ct = default)
    {
        var masks = mask.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (masks.Length == 0) masks = ["*"];
        return [.. FileWalker.Walk(folder, ct: ct).Select(e => e.Path).Where(p => masks.Any(m => FileSystemName.MatchesSimpleExpression(m, Path.GetFileName(p), ignoreCase: true)))];
    }
}

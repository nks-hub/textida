using System.Text.Json;

namespace NksHub.Textida.Core.Agents.Hooks;

public enum HookProduct { ClaudeCode, Codex }

/// <summary>What an exit code of a hook does, as the public documentation of the product says.</summary>
public enum HookExitEffect
{
    /// <summary>Exit 0: the action goes ahead and the JSON on stdout applies.</summary>
    Success,
    /// <summary>The action the event announces does not happen; stderr is the reason.</summary>
    Blocks,
    /// <summary>The agent is kept from stopping and works on; stderr is what it works on.</summary>
    KeepsWorking,
    StderrToModel,
    StderrToUser,
    /// <summary>The exit code and stderr change nothing.</summary>
    Ignored,
    /// <summary>The event ignores the exit code; the decision is made in the JSON output.</summary>
    NotHonored,
    DebugLog,
    /// <summary>The original text is displayed whatever the hook does.</summary>
    DisplayOnly,
    /// <summary>The action goes ahead; the first line of stderr is shown as a notice.</summary>
    NonBlocking,
    /// <summary>The request is denied (a failing hook that is set to block, on a permission request).</summary>
    Denies,
    /// <summary>The hook runs in the background and cannot block anything.</summary>
    Background,
    /// <summary>The documentation says nothing about it.</summary>
    Undocumented,
}

/// <summary>What the hook may print on stdout when it exits 0.</summary>
public enum HookStdout { Ignored, ContextText, JsonRequired }

/// <param name="MatcherField">The field of the input the matcher is compared with; null when the event has no matcher.</param>
/// <param name="MatcherValues">Values the documentation gives as examples of what the matcher can hold.</param>
/// <param name="ToolEvent">The <c>if</c> condition of a handler is evaluated on this event (Claude Code).</param>
/// <param name="NarrowMatcher">Only letters, digits, underscore and the bar make an exact match; any other character makes the matcher a regular expression.</param>
/// <param name="Since">The first version of the product that has the event, where the documentation says so.</param>
/// <param name="Sample">An example of the input the hook gets on stdin (the example of the documentation, or one built from its field tables).</param>
public sealed record HookEventInfo(
    HookProduct Product, string Name, string? MatcherField, IReadOnlyList<string> MatcherValues, bool ToolEvent, bool NarrowMatcher,
    HookExitEffect ExitTwo, HookExitEffect ExitOther, HookStdout Stdout, string? Since, JsonElement Sample);

public sealed record HookHandlerType(string Name, IReadOnlyList<string> Required, IReadOnlyList<string> Optional);

/// <summary>
/// The hook events, handler types and fields of a product, read from data written from its public documentation (the page named in <see cref="Source"/>, as it read on <see cref="Checked"/>).
/// Nothing here is guessed: a field or an event the documentation does not name is not in the catalogue, and a finding about it says so.
/// </summary>
public sealed class HookCatalog
{
    private static readonly Lazy<HookCatalog> Claude = new(() => Load(HookProduct.ClaudeCode, "claude"));
    private static readonly Lazy<HookCatalog> Codex = new(() => Load(HookProduct.Codex, "codex"));

    private readonly Dictionary<string, HookEventInfo> _byName;
    private readonly Dictionary<string, HookHandlerType> _types;

    private HookCatalog(HookProduct product, string source, string checkedOn, IReadOnlyList<HookEventInfo> events, IReadOnlyList<HookHandlerType> types, IReadOnlyList<string> common, string schema)
    {
        Product = product;
        Source = source;
        Checked = checkedOn;
        Events = events;
        HandlerTypes = types;
        CommonFields = common;
        SchemaText = schema;
        _byName = events.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _types = types.ToDictionary(t => t.Name, StringComparer.Ordinal);
    }

    public HookProduct Product { get; }
    public string Source { get; }
    public string Checked { get; }
    public IReadOnlyList<HookEventInfo> Events { get; }
    public IReadOnlyList<HookHandlerType> HandlerTypes { get; }

    /// <summary>The fields every handler type has.</summary>
    public IReadOnlyList<string> CommonFields { get; }

    /// <summary>The JSON Schema (draft-07) of the file, with the hook events as the allowed names of <c>hooks</c>.</summary>
    public string SchemaText { get; }

    public static HookCatalog For(HookProduct product) => product == HookProduct.ClaudeCode ? Claude.Value : Codex.Value;

    public HookEventInfo? Find(string name) => _byName.GetValueOrDefault(name);

    public HookHandlerType? FindType(string name) => _types.GetValueOrDefault(name);

    /// <summary>True when the documentation lists <paramref name="field"/> for a handler of this type.</summary>
    public bool IsKnownField(string type, string field) =>
        CommonFields.Contains(field) || (_types.TryGetValue(type, out var t) && (t.Required.Contains(field) || t.Optional.Contains(field)));

    private static string ReadResource(string name)
    {
        using var stream = typeof(HookCatalog).Assembly.GetManifestResourceStream("Hooks/" + name) ?? throw new FileNotFoundException("Missing embedded hook data " + name);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static HookExitEffect Effect(string id) => id switch
    {
        "blocks" => HookExitEffect.Blocks,
        "keepsWorking" => HookExitEffect.KeepsWorking,
        "stderrToModel" => HookExitEffect.StderrToModel,
        "stderrToUser" => HookExitEffect.StderrToUser,
        "ignored" => HookExitEffect.Ignored,
        "notHonored" => HookExitEffect.NotHonored,
        "debugLog" => HookExitEffect.DebugLog,
        "displayOnly" => HookExitEffect.DisplayOnly,
        "nonBlocking" => HookExitEffect.NonBlocking,
        "undocumented" => HookExitEffect.Undocumented,
        _ => throw new InvalidDataException("Unknown hook effect " + id),
    };

    private static HookStdout StdoutRule(string id) => id switch
    {
        "ignored" => HookStdout.Ignored,
        "contextText" => HookStdout.ContextText,
        "jsonRequired" => HookStdout.JsonRequired,
        _ => throw new InvalidDataException("Unknown stdout rule " + id),
    };

    private static List<string> Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    private static HookCatalog Load(HookProduct product, string id)
    {
        // the document stays alive for the whole run: the samples are elements of it
        var document = JsonDocument.Parse(ReadResource(id + ".hooks.json"));
        var root = document.RootElement;
        var events = new List<HookEventInfo>();
        foreach (var e in root.GetProperty("events").EnumerateArray())
        {
            events.Add(new HookEventInfo(
                product, e.GetProperty("name").GetString()!, e.GetProperty("matcher").GetString(), Strings(e.GetProperty("values")), e.GetProperty("tool").GetBoolean(),
                e.GetProperty("narrowMatcher").GetBoolean(), Effect(e.GetProperty("exit2").GetString()!), Effect(e.GetProperty("exitOther").GetString()!),
                StdoutRule(e.GetProperty("stdout").GetString()!), e.GetProperty("since").GetString(), e.GetProperty("sample")));
        }
        var types = root.GetProperty("handlerTypes").EnumerateObject()
            .Select(t => new HookHandlerType(t.Name, Strings(t.Value.GetProperty("required")), Strings(t.Value.GetProperty("optional")))).ToList();
        return new HookCatalog(product, root.GetProperty("source").GetString()!, root.GetProperty("checked").GetString()!, events, types, Strings(root.GetProperty("commonFields")), ReadResource(id + ".hooks.schema.json"));
    }
}

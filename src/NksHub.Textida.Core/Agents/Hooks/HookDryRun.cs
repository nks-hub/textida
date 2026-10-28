using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace NksHub.Textida.Core.Agents.Hooks;

/// <summary>The settings of a handler that change what its exit code does.</summary>
/// <param name="OnFailure">Claude Code, <c>onFailure</c>: <c>block</c> turns a failing hook into a block.</param>
public sealed record HookHandlerTraits(bool Async = false, bool AsyncRewake = false, string? OnFailure = null);

public sealed record HookExitOutcome(int ExitCode, HookExitEffect Effect)
{
    /// <summary>The action the event announces does not go ahead, or the agent is kept from stopping.</summary>
    public bool IsBlocking => Effect is HookExitEffect.Blocks or HookExitEffect.KeepsWorking or HookExitEffect.Denies;
}

/// <summary>
/// The table of expected results by exit code (P8b.3.1). For Claude Code it is the table of the public hooks reference: exit 0 is success, exit 2 does what the event allows (blocks the tool call, keeps Claude
/// working, shows stderr to Claude, ...), any other code is a non-blocking error, except where the event has its own rule or the handler sets <c>onFailure</c> to <c>block</c>. For Codex only what its
/// documentation says is in the table; the rest is <see cref="HookExitEffect.Undocumented"/>. The two tables are not the same and are not assumed to hold for the other product.
/// </summary>
public static class HookExit
{
    private static readonly HashSet<string> BlockHasNoEffect = ["Stop", "SubagentStop", "TaskCompleted", "TeammateIdle"];

    public static HookExitOutcome Interpret(HookEventInfo ev, int exitCode, HookHandlerTraits? traits = null)
    {
        traits ??= new HookHandlerTraits();
        if (ev.Product == HookProduct.ClaudeCode && traits.Async)
            return new HookExitOutcome(exitCode, traits.AsyncRewake && exitCode == 2 ? HookExitEffect.StderrToModel : HookExitEffect.Background);
        if (exitCode == 0) return new HookExitOutcome(0, HookExitEffect.Success);
        if (exitCode == 2) return new HookExitOutcome(2, ev.ExitTwo);
        var other = ev.ExitOther;
        if (ev.Product == HookProduct.ClaudeCode && traits.OnFailure == "block" && other == HookExitEffect.NonBlocking && !BlockHasNoEffect.Contains(ev.Name))
            other = ev.Name == "PermissionRequest" ? HookExitEffect.Denies : ev.ExitTwo;
        return new HookExitOutcome(exitCode, other);
    }
}

/// <summary>
/// What a hook would receive and what its exit codes would do, worked out from the catalogue. Nothing is run: the command of the handler is never started, and no text leaves the editor.
/// </summary>
/// <param name="Event">Null when the catalogue of the product does not know the event.</param>
/// <param name="Matches">Whether the matcher fires for <see cref="TestedValue"/>; null when the matcher is a pattern that does not compile.</param>
/// <param name="Outcomes">The results for exit codes 0, 1 and 2.</param>
public sealed record HookDryRun(
    HookEventInfo? Event, string EventName, string Matcher, HookMatchKind MatchKind, string? MatcherField, string TestedValue, bool? Matches,
    string StdinJson, IReadOnlyList<HookExitOutcome> Outcomes, HookStdout Stdout);

public static class HookDryRunner
{
    private static readonly JavaScriptEncoder Relaxed = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>The value the matcher of the event is tried against when the user names none: the first plain example of the documentation, or the value of the sample.</summary>
    public static string DefaultValue(HookEventInfo ev)
    {
        foreach (var v in ev.MatcherValues)
            if (v.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return v;
        return ev.MatcherField is { } field && ev.Sample.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";
    }

    /// <summary>The JSON object the hook gets on stdin: the sample of the event, with the field the matcher looks at set to <paramref name="matchedValue"/>.</summary>
    public static string RenderInput(HookEventInfo ev, string? matchedValue)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = Relaxed, NewLine = "\n" }))
        {
            writer.WriteStartObject();
            foreach (var p in ev.Sample.EnumerateObject())
            {
                if (matchedValue is not null && p.Name == ev.MatcherField && p.Value.ValueKind == JsonValueKind.String) writer.WriteString(p.Name, Replaced(p.Name, p.Value.GetString()!, matchedValue));
                else p.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>A path field keeps its folder and takes the tested file name; every other field takes the tested value.</summary>
    private static string Replaced(string field, string sample, string value)
    {
        if (field != "file_path") return value;
        int slash = sample.LastIndexOfAny(['/', '\\']);
        return slash < 0 ? value : sample[..(slash + 1)] + value;
    }

    public static HookDryRun Run(HookCatalog catalog, string eventName, string? matcher, string? testedValue, HookHandlerTraits? traits, TimeSpan regexTimeout)
    {
        var ev = catalog.Find(eventName);
        matcher ??= "";
        if (ev is null) return new HookDryRun(null, eventName, matcher, HookMatchKind.All, null, "", null, "", [], HookStdout.Ignored);
        string value = string.IsNullOrEmpty(testedValue) ? DefaultValue(ev) : testedValue;
        return new HookDryRun(
            ev, eventName, matcher, HookMatcher.Classify(ev, matcher), ev.MatcherField, value, HookMatcher.Matches(ev, matcher, value, regexTimeout),
            RenderInput(ev, ev.MatcherField is null ? null : value), [HookExit.Interpret(ev, 0, traits), HookExit.Interpret(ev, 1, traits), HookExit.Interpret(ev, 2, traits)], ev.Stdout);
    }
}

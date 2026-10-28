using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugin.Testing;

/// <summary>A document the recorded session starts with.</summary>
public sealed class RecordedDocument
{
    /// <summary>The name steps refer to the document by.</summary>
    public string Name { get; set; } = "";

    /// <summary>The text of the document, UTF-8 without a byte order mark.</summary>
    public string Text { get; set; } = "";

    /// <summary>Selections as <c>[anchor, caret]</c> byte offsets; empty puts a caret at the start.</summary>
    public List<long[]> Selections { get; set; } = [];
}

/// <summary>
/// One thing that happened in a recorded session, or one thing the plugin must have done. <see cref="Kind"/> decides which of the other members count:
/// <c>command</c> (<see cref="Name"/>, <see cref="Document"/>, <see cref="Arguments"/>, <see cref="Status"/>, <see cref="ExpectError"/>), <c>event</c> (<see cref="Name"/>, <see cref="Payload"/>),
/// <c>edit</c> (the user types in <see cref="Document"/>: <see cref="Offset"/>, <see cref="End"/>, <see cref="Text"/>), <c>select</c> (<see cref="Document"/>, <see cref="Ranges"/>),
/// <c>expectText</c> (<see cref="Document"/>, <see cref="Text"/>), <c>expectCalled</c> (<see cref="Method"/>, optional <see cref="Count"/>), <c>expectNotCalled</c> (<see cref="Method"/>)
/// and <c>expectNotice</c> (a notice that contains <see cref="Text"/>). An <c>expect</c> step waits for its condition for a few seconds, because a plugin handles events and commands on its own time.
/// </summary>
public sealed class RecordedStep
{
    /// <summary>What kind of step this is.</summary>
    public string Kind { get; set; } = "";

    /// <summary>The command id (<c>command</c>) or the event name (<c>event</c>).</summary>
    public string? Name { get; set; }

    /// <summary>The name of the document the step is about.</summary>
    public string? Document { get; set; }

    /// <summary>For <c>edit</c>: the byte offset where the change starts.</summary>
    public long Offset { get; set; }

    /// <summary>For <c>edit</c>: the byte offset where the replaced text ends; absent means an insertion at <see cref="Offset"/>.</summary>
    public long? End { get; set; }

    /// <summary>The text typed (<c>edit</c>), expected in the document (<c>expectText</c>) or expected in a notice (<c>expectNotice</c>).</summary>
    public string? Text { get; set; }

    /// <summary>For <c>select</c>: the selections as <c>[anchor, caret]</c>.</summary>
    public List<long[]>? Ranges { get; set; }

    /// <summary>For <c>command</c>: members added to the request of the command.</summary>
    public JsonElement? Arguments { get; set; }

    /// <summary>For <c>event</c>: the payload of the event.</summary>
    public JsonElement? Payload { get; set; }

    /// <summary>For <c>command</c>: the <c>status</c> the plugin must answer with.</summary>
    public string? Status { get; set; }

    /// <summary>For <c>command</c>: the JSON-RPC error code the plugin must fail with.</summary>
    public int? ExpectError { get; set; }

    /// <summary>For <c>expectCalled</c> and <c>expectNotCalled</c>: the method the plugin calls on the editor.</summary>
    public string? Method { get; set; }

    /// <summary>For <c>expectCalled</c>: how many times; absent means at least once.</summary>
    public int? Count { get; set; }
}

/// <summary>
/// A session of the editor written down: the documents it had, the permissions the user granted, and what the user and the editor did, in order. Replay it against a plugin with
/// <see cref="TestEditorReplay.ReplayAsync"/>. The file is JSON; an unknown member is an error, so a typo does not silently turn a check off.
/// </summary>
public sealed class Recording
{
    /// <summary>Permissions granted to the plugin for all documents when the replay starts, in addition to those of the options.</summary>
    public List<string> Grants { get; set; } = [];

    /// <summary>The documents open when the session starts.</summary>
    public List<RecordedDocument> Documents { get; set; } = [];

    /// <summary>What happened, in order.</summary>
    public List<RecordedStep> Steps { get; set; } = [];

    /// <summary>Reads a recording from JSON text.</summary>
    public static Recording Parse(string json) =>
        JsonSerializer.Deserialize(json, RecordingJson.Default.Recording) ?? throw new JsonException("The recording is empty.");

    /// <summary>Reads a recording from a file.</summary>
    public static Recording Load(string path) => Parse(File.ReadAllText(path, Encoding.UTF8));
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(Recording))]
internal sealed partial class RecordingJson : JsonSerializerContext;

/// <summary>What one step of a replay did.</summary>
/// <param name="Index">The position of the step, from 0.</param>
/// <param name="Kind">The kind of the step.</param>
/// <param name="Reply">The answer of the plugin for a command; null for the other steps.</param>
public sealed record StepOutcome(int Index, string Kind, JsonElement? Reply);

/// <summary>The result of a replay: what each step did, and the expectations that did not hold.</summary>
/// <param name="Steps">One outcome per step that ran.</param>
/// <param name="Failures">One line per failed expectation or failed command; empty when the plugin did what the recording says.</param>
public sealed record ReplayResult(IReadOnlyList<StepOutcome> Steps, IReadOnlyList<string> Failures)
{
    /// <summary>True when every expectation held.</summary>
    public bool Passed => Failures.Count == 0;
}

/// <summary>Replays a <see cref="Recording"/> against the plugin of a <see cref="TestEditor"/>.</summary>
public static class TestEditorReplay
{
    /// <summary>
    /// Opens the documents of the recording, grants its permissions, and does its steps one after another: the user's edits and the editor's events reach the plugin the way they would
    /// in the editor, commands run, and the <c>expect</c> steps check the documents, the requests the plugin made and the notices it showed. A step that fails does not stop the replay.
    /// </summary>
    /// <param name="editor">The editor to replay in; it must have no document with a name the recording uses.</param>
    /// <param name="recording">The session.</param>
    /// <param name="expectTimeout">How long an <c>expect</c> step waits for its condition; 5 seconds when absent.</param>
    /// <param name="ct">Cancels the replay.</param>
    public static async Task<ReplayResult> ReplayAsync(this TestEditor editor, Recording recording, TimeSpan? expectTimeout = null, CancellationToken ct = default)
    {
        var wait = expectTimeout ?? TimeSpan.FromSeconds(5);
        var outcomes = new List<StepOutcome>();
        var failures = new List<string>();
        foreach (var grant in recording.Grants) editor.Permissions.Grant(editor.PluginId, grant, GrantScope.All);
        foreach (var document in recording.Documents)
        {
            var opened = editor.OpenDocument(document.Name, document.Text);
            if (document.Selections.Count > 0) opened.Select([.. document.Selections.Select(s => (s[0], s[1]))]);
        }

        for (var i = 0; i < recording.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = recording.Steps[i];
            JsonElement? reply = null;
            string Where() => $"step {i} ({step.Kind})";
            try
            {
                switch (step.Kind)
                {
                    case "command":
                        reply = await RunCommandAsync(editor, step, failures, Where(), ct).ConfigureAwait(false);
                        break;
                    case "event":
                        if (!await editor.SendEventAsync(Need(step.Name, "name"), step.Payload?.GetRawText() ?? "").ConfigureAwait(false)) failures.Add($"{Where()}: the plugin could not take the event.");
                        break;
                    case "edit":
                        editor.GetDocument(Need(step.Document, "document")).Replace(step.Offset, step.End ?? step.Offset, step.Text ?? "");
                        break;
                    case "select":
                        editor.GetDocument(Need(step.Document, "document")).Select([.. (step.Ranges ?? []).Select(s => (s[0], s[1]))]);
                        break;
                    case "expectText":
                        {
                            var document = editor.GetDocument(Need(step.Document, "document"));
                            if (!await UntilAsync(() => document.Text == step.Text, wait, ct).ConfigureAwait(false))
                                failures.Add($"{Where()}: the text of '{step.Document}' is {Quote(document.Text)}, expected {Quote(step.Text ?? "")}.");
                            break;
                        }
                    case "expectCalled":
                        {
                            var method = Need(step.Method, "method");
                            var wanted = step.Count;
                            if (!await UntilAsync(() => wanted is { } n ? editor.Calls.Count(c => c.Method == method) == n : editor.Calls.Any(c => c.Method == method), wait, ct).ConfigureAwait(false))
                                failures.Add($"{Where()}: the plugin called '{method}' {editor.Calls.Count(c => c.Method == method)} times, expected {(wanted?.ToString() ?? "at least 1")}.");
                            break;
                        }
                    case "expectNotCalled":
                        {
                            var method = Need(step.Method, "method");
                            await Task.Delay(100, ct).ConfigureAwait(false);
                            if (editor.Calls.Any(c => c.Method == method)) failures.Add($"{Where()}: the plugin called '{method}'.");
                            break;
                        }
                    case "expectNotice":
                        {
                            var text = Need(step.Text, "text");
                            if (!await UntilAsync(() => { lock (editor.Ui.Notices) return editor.Ui.Notices.Any(n => n.Message.Contains(text, StringComparison.Ordinal)); }, wait, ct).ConfigureAwait(false))
                                failures.Add($"{Where()}: the plugin showed no notice with '{text}'.");
                            break;
                        }
                    default:
                        failures.Add($"{Where()}: unknown kind of step.");
                        break;
                }
            }
            catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or JsonException or ArgumentException)
            {
                failures.Add($"{Where()}: {e.Message}");
            }
            outcomes.Add(new StepOutcome(i, step.Kind, reply));
        }
        return new ReplayResult(outcomes, failures);
    }

    private static async Task<JsonElement?> RunCommandAsync(TestEditor editor, RecordedStep step, List<string> failures, string where, CancellationToken ct)
    {
        var name = Need(step.Name, "name");
        var document = step.Document is null ? null : editor.GetDocument(step.Document);
        try
        {
            var reply = await editor.ExecuteCommandAsync(name, document, step.Arguments?.GetRawText(), ct: ct).ConfigureAwait(false);
            if (step.ExpectError is { } code) failures.Add($"{where}: command '{name}' succeeded, expected the error {code}.");
            else if (step.Status is { } status)
            {
                var actual = reply.ValueKind == JsonValueKind.Object && reply.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
                if (actual != status) failures.Add($"{where}: command '{name}' answered the status {Quote(actual ?? "(none)")}, expected {Quote(status)}.");
            }
            return reply;
        }
        catch (RpcException e)
        {
            if (step.ExpectError != e.Code) failures.Add($"{where}: command '{name}' failed with {e.Code}: {e.Message}");
            return null;
        }
    }

    private static string Need(string? value, string member) => value ?? throw new ArgumentException($"The step needs '{member}'.");

    private static string Quote(string text) => "\"" + text.Replace("\n", "\\n").Replace("\r", "\\r") + "\"";

    private static async Task<bool> UntilAsync(Func<bool> condition, TimeSpan timeout, CancellationToken ct)
    {
        var stop = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= stop) return false;
            await Task.Delay(10, ct).ConfigureAwait(false);
        }
        return true;
    }
}

using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Core.Tools;

public enum SaveActionState { Done, Unchanged, Skipped, Failed, TimedOut, Missing }

/// <param name="Reason">What happened, in English, for the status bar and the output; empty when the action simply ran.</param>
public sealed record SaveActionNote(SaveAction Action, SaveActionState State, string Reason = "");

/// <param name="Saved">The save itself went through; the actions that follow a save ran only then.</param>
public sealed record SaveActionReport(bool Saved, IReadOnlyList<SaveActionNote> Notes);

/// <summary>
/// The order of a save with actions. Formatters run first, each a bounded transaction (<see cref="ConfigFormatters.FormatAsync"/>) whose failure or timeout only costs the formatting:
/// the file is saved as it is and the note says why. The save runs next. Tools and uploads run after it, and an upload first checks that the file on disk is still exactly
/// the bytes this save wrote (the fingerprint taken by the save), so it never sends older or newer content than the save it belongs to.
/// An autosave leaves out actions marked "only on manual save". A save made while the actions of the same document are running, or by a plugin, runs none, so no action can start a loop.
/// </summary>
public sealed class SaveActionRunner
{
    private readonly AsyncLocal<Document?> _inside = new();
    private readonly Func<string, FormatterDef?> _formatters;

    /// <param name="formatters">Finds a formatter by id; the catalog of <see cref="ConfigFormatters"/> when null.</param>
    public SaveActionRunner(Func<string, FormatterDef?>? formatters = null) => _formatters = formatters ?? ConfigFormatters.Find;

    /// <param name="save">Does the save and says whether it went through; it may itself call <see cref="RunAsync"/> again (a conflict that is answered with a second try).</param>
    /// <param name="after">Runs a tool or an upload for the saved file; returns null when it went well, otherwise why not.</param>
    /// <param name="path">The file <paramref name="save"/> writes when it is not the file of the document (Save As); the formatters work for that file.</param>
    public async Task<SaveActionReport> RunAsync(Document doc, SaveActionSet set, SaveOrigin origin, Func<Task<bool>> save, Func<SaveAction, Task<string?>> after, CancellationToken ct = default, string? path = null)
    {
        var chosen = origin == SaveOrigin.Plugin || _inside.Value == doc
            ? []
            : set.Actions.Where(a => a.Enabled && !(origin == SaveOrigin.Autosave && a.OnlyManual)).ToList();
        if (chosen.Count == 0) return new SaveActionReport(await save(), []);

        _inside.Value = doc;
        try
        {
            var notes = new List<SaveActionNote>();
            foreach (var action in chosen.Where(a => a.Kind == SaveActionKind.Format))
                notes.Add(await FormatAsync(doc, action, set, path, ct));

            if (!await save()) return new SaveActionReport(false, notes);

            var written = doc.Fingerprint;
            string? saved = doc.FilePath;
            foreach (var action in chosen.Where(a => a.Kind != SaveActionKind.Format))
            {
                if (action.Kind == SaveActionKind.Upload && (saved is null || written is null || FileFingerprint.TryRead(saved)?.Sha256 != written.Sha256))
                {
                    notes.Add(new SaveActionNote(action, SaveActionState.Skipped, "The file on disk is no longer what this save wrote, so it was not sent."));
                    continue;
                }
                string? problem = await after(action);
                notes.Add(problem is null ? new SaveActionNote(action, SaveActionState.Done) : new SaveActionNote(action, SaveActionState.Failed, problem));
            }
            return new SaveActionReport(true, notes);
        }
        finally
        {
            _inside.Value = null;
        }
    }

    private async Task<SaveActionNote> FormatAsync(Document doc, SaveAction action, SaveActionSet set, string? path, CancellationToken ct)
    {
        if (_formatters(action.Id) is not { } def) return new(action, SaveActionState.Skipped, $"Unknown formatter '{action.Id}'.");
        if (set.FormatTimeout is not { } timeout) return new(action, SaveActionState.Skipped, "No time limit is set for formatters (formatTimeoutSeconds in saveActions.json).");
        if (!doc.IsModified) return new(action, SaveActionState.Skipped, "");
        var outcome = await ConfigFormatters.FormatAsync(doc, def, timeout, ct, path, set.ToolPaths?.GetValueOrDefault(def.Id));
        var state = outcome.Result switch
        {
            FormatResult.Formatted => SaveActionState.Done,
            FormatResult.Unchanged => SaveActionState.Unchanged,
            FormatResult.Failed => SaveActionState.Failed,
            FormatResult.TimedOut => SaveActionState.TimedOut,
            FormatResult.Missing => SaveActionState.Missing,
            _ => SaveActionState.Skipped,
        };
        return new(action, state, outcome.Reason);
    }
}

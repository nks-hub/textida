using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Documents;

/// <summary>
/// The API for language services: diagnostics a plugin publishes (the editor draws them) and completion a plugin answers (the editor lists and applies it).
/// Both name the revision they were made for; an answer for an older revision is mapped through the change log or dropped, never shown as if it were current.
/// </summary>
public sealed partial class DocumentApi
{
    private readonly Dictionary<(string Plugin, Guid Document, string Source), DiagnosticSet> _diagnostics = [];
    private readonly List<string> _completionProviders = [];

    // ---- diagnostics ----

    private static DiagnosticSeverity ParseSeverity(JsonElement item) => NeedString(item, "severity") switch
    {
        "error" => DiagnosticSeverity.Error,
        "warning" => DiagnosticSeverity.Warning,
        "information" or "info" => DiagnosticSeverity.Information,
        "hint" => DiagnosticSeverity.Hint,
        _ => throw Invalid("Parameter 'severity' must be error, warning, information or hint."),
    };

    /// <summary>
    /// Stores the diagnostics of one source, replacing what that source published before. A set for an older revision is moved to the current one through the change log:
    /// a diagnostic whose text was changed since is dropped, and a revision the log no longer reaches drops them all. A revision that does not exist yet is a conflict.
    /// The answer says how many were kept (<c>accepted</c>) and how many were dropped.
    /// </summary>
    private async Task<byte[]> PublishDiagnosticsAsync(string pluginId, JsonElement p)
    {
        var e = Find(NeedDocumentId(p));
        long revision = NeedLong(p, "revision");
        var source = LimitedText(p, "source");
        var array = Need(p, "diagnostics");
        if (array.ValueKind != JsonValueKind.Array) throw Invalid("Parameter 'diagnostics' must be an array.");
        if (array.GetArrayLength() > _options.MaxDiagnosticsPerDocument)
            throw Fail(DocumentErrorCodes.DiagnosticsQuotaExceeded, $"A source may publish {_options.MaxDiagnosticsPerDocument} diagnostics per document.", w => w.WriteNumber("limit", _options.MaxDiagnosticsPerDocument));
        return await _owner.InvokeAsync(() =>
        {
            var d = e.Doc;
            lock (_gate)
            {
                if (!_exposed.ContainsKey(d.Id)) throw Fail(DocumentErrorCodes.UnknownDocument, "The document is not open or not available to plugins.");
            }
            if (revision > d.Revision) throw Conflict("FutureRevision", d.Revision);
            bool reachable = revision == d.Revision || revision >= d.OldestMappableRevision;
            List<Diagnostic> kept = [];
            int total = 0;
            foreach (var item in array.EnumerateArray())
            {
                total++;
                var severity = ParseSeverity(item);
                var message = LimitedText(item, "message");
                var code = item.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? LimitedText(item, "code", allowEmpty: true) : null;
                long start = NeedLong(item, "start"), end = NeedLong(item, "end");
                if (start < 0 || end < start) throw Invalid($"The range [{start}, {end}) is not valid.");
                var range = new ByteRange(start, end);
                if (revision == d.Revision) CheckRange(d, start, end);
                else if (!reachable) continue;
                else
                {
                    var mapped = d.MapRange(range, revision);
                    if (!mapped.Ok || mapped.Range.End > d.ByteLength || !d.IsCharBoundary(mapped.Range.Start) || !d.IsCharBoundary(mapped.Range.End)) continue;
                    range = mapped.Range;
                }
                kept.Add(new Diagnostic(severity, message, code, range));
            }
            var key = (pluginId, d.Id, source);
            lock (_gate)
            {
                if (kept.Count == 0) _diagnostics.Remove(key);
                else _diagnostics[key] = new DiagnosticSet(pluginId, d.Id, source, d.Revision, kept);
            }
            _options.Tools.DiagnosticsChanged(d.Id);
            return Obj(w =>
            {
                w.WriteNumber("revision", d.Revision);
                w.WriteNumber("accepted", kept.Count);
                w.WriteNumber("dropped", total - kept.Count);
            });
        });
    }

    /// <summary>The diagnostics of all plugins for a document, with their ranges in the current revision. Call on the thread that owns the documents.</summary>
    public IReadOnlyList<PlacedDiagnostic> DiagnosticsOf(Guid document)
    {
        lock (_gate)
        {
            if (_diagnostics.Count == 0) return [];
            return [.. _diagnostics.Values.Where(s => s.Document == document)
                .SelectMany(s => s.Diagnostics.Select(x => new PlacedDiagnostic(s.PluginId, s.Source, x.Severity, x.Message, x.Code, x.Range)))];
        }
    }

    /// <summary>Whether any plugin has diagnostics published for the document, so withdrawing it would take them away.</summary>
    public bool HasDiagnostics(Guid document)
    {
        lock (_gate) return _diagnostics.Keys.Any(key => key.Document == document);
    }

    /// <summary>Moves the stored diagnostics of a changed document to its new revision; one whose text the change touched is dropped. Runs on the thread that changed the document.</summary>
    private void MoveDiagnostics(Document d, long revision)
    {
        bool any = false;
        lock (_gate)
        {
            if (_diagnostics.Count == 0) return;
            foreach (var (key, set) in _diagnostics.ToArray())
            {
                if (key.Document != d.Id || set.Revision >= revision) continue;
                any = true;
                List<Diagnostic> moved = [];
                bool reachable = set.Revision >= d.OldestMappableRevision;
                foreach (var x in set.Diagnostics)
                {
                    if (!reachable) break;
                    var m = d.MapRange(x.Range, set.Revision);
                    if (m.Ok) moved.Add(x with { Range = m.Range });
                }
                if (moved.Count == 0) _diagnostics.Remove(key);
                else _diagnostics[key] = set with { Revision = revision, Diagnostics = moved };
            }
        }
        if (any) _options.Tools.DiagnosticsChanged(d.Id);
    }

    private void ClearDiagnostics(Func<(string Plugin, Guid Document, string Source), bool> match)
    {
        HashSet<Guid> documents = [];
        lock (_gate)
        {
            foreach (var key in _diagnostics.Keys.Where(match).ToArray())
            {
                _diagnostics.Remove(key);
                documents.Add(key.Document);
            }
        }
        foreach (var document in documents) _options.Tools.DiagnosticsChanged(document);
    }

    // ---- completion ----

    private byte[] RegisterCompletion(string pluginId, JsonElement p)
    {
        bool enabled = !p.TryGetProperty("enabled", out var v) || v.ValueKind != JsonValueKind.False;
        lock (_gate)
        {
            if (!enabled) _completionProviders.Remove(pluginId);
            else if (!_completionProviders.Contains(pluginId)) _completionProviders.Add(pluginId);
        }
        return Obj(w => w.WriteBoolean("enabled", enabled));
    }

    /// <summary>True when a plugin offered to answer completion requests.</summary>
    public bool HasCompletionProviders
    {
        get { lock (_gate) return _completionProviders.Count > 0; }
    }

    private static readonly CompletionAnswer NoAnswer = new(0, false, []);

    /// <summary>
    /// Asks the plugins that registered for completion at a caret (<paramref name="offset"/>, bytes) of the current revision, all at once. An answer counts only when it names the revision
    /// that was asked and the document is still at it when the last plugin has answered; otherwise <see cref="CompletionAnswer.Stale"/> is set and nothing is offered. A plugin that fails,
    /// hangs, is gone or has no read grant for the document gives nothing. Items whose ranges are not valid for the revision are left out. Only a cancelled <paramref name="cancellation"/> throws.
    /// </summary>
    public async Task<CompletionAnswer> RequestCompletionAsync(Guid document, long offset, CancellationToken cancellation = default, string? language = null)
    {
        string[] providers;
        Exposed? e;
        lock (_gate)
        {
            providers = [.. _completionProviders];
            _exposed.TryGetValue(document, out e);
        }
        if (providers.Length == 0 || e is null) return NoAnswer;
        long revision = await _owner.InvokeAsync(() => e.Doc.Revision);
        var request = Obj(w =>
        {
            w.WriteString("document", document);
            w.WriteNumber("revision", revision);
            w.WriteNumber("offset", offset);
            if (language is not null) w.WriteString("language", language);
        });
        async Task<(string Plugin, JsonElement Answer)?> Ask(string plugin)
        {
            if (!_options.Permissions.IsGranted(plugin, DocumentPermissions.Read, GrantScope.OfDocument(document))) return null;
            try { return (plugin, (await _request(plugin, DocumentRequests.Completion, request, _options.CompletionTimeout, cancellation)).Clone()); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception) { return null; } // a plugin that fails or hangs gives no offers; it must not stop the others
        }
        var answers = await Task.WhenAll(providers.Select(Ask));
        return await _owner.InvokeAsync(() =>
        {
            var d = e.Doc;
            if (d.Revision != revision) return new CompletionAnswer(revision, true, []);
            List<CompletionItem> items = [];
            foreach (var a in answers)
            {
                if (a is not { } got || got.Answer.ValueKind != JsonValueKind.Object) continue;
                if (!got.Answer.TryGetProperty("revision", out var answered) || !answered.TryGetInt64(out var r) || r != revision) continue;
                if (!got.Answer.TryGetProperty("items", out var array) || array.ValueKind != JsonValueKind.Array) continue;
                int taken = 0;
                foreach (var item in array.EnumerateArray())
                {
                    if (taken >= _options.MaxCompletionItems) break;
                    if (ParseCompletionItem(got.Plugin, d, revision, item) is { } parsed)
                    {
                        items.Add(parsed);
                        taken++;
                    }
                }
            }
            return new CompletionAnswer(revision, false, items);
        });
    }

    private CompletionItem? ParseCompletionItem(string pluginId, Document d, long revision, JsonElement item)
    {
        try
        {
            var label = LimitedText(item, "label");
            string? detail = item.TryGetProperty("detail", out var dt) && dt.ValueKind == JsonValueKind.String ? LimitedText(item, "detail", allowEmpty: true) : null;
            string? kind = item.TryGetProperty("kind", out var kd) && kd.ValueKind == JsonValueKind.String ? LimitedText(item, "kind", allowEmpty: true) : null;
            long start = NeedLong(item, "start"), end = NeedLong(item, "end");
            CheckRange(d, start, end);
            var text = NeedString(item, "text");
            if (text.Length > _options.MaxSnippetLength) return null;
            List<CompletionEdit> extra = [];
            if (item.TryGetProperty("edits", out var edits) && edits.ValueKind == JsonValueKind.Array)
            {
                if (edits.GetArrayLength() > _options.MaxCompletionEdits) return null;
                foreach (var edit in edits.EnumerateArray())
                {
                    long s = NeedLong(edit, "start"), en = NeedLong(edit, "end");
                    CheckRange(d, s, en);
                    var t = NeedString(edit, "text");
                    if (t.Length > _options.MaxSnippetLength) return null;
                    extra.Add(new CompletionEdit(new ByteRange(s, en), t));
                }
            }
            var main = new ByteRange(start, end);
            Validate(d, [new ByteEdit(main, Array.Empty<byte>()), .. extra.Select(x => new ByteEdit(x.Range, Array.Empty<byte>()))]);
            return new CompletionItem(pluginId, d.Id, revision, label, detail, kind, main, text, extra);
        }
        catch (RpcException) { return null; }
    }

    /// <summary>
    /// Writes the chosen offer: the main text and all additional edits as one transaction, so one undo step, with the caret after the main text. Refused (a
    /// <see cref="RpcException"/>, nothing written) when the document is not at the revision of the offer, or the edits overlap or are not valid. Call on the thread that owns the document.
    /// </summary>
    public long ApplyCompletion(CompletionItem item)
    {
        var e = FindWritable(item.Document);
        var d = e.Doc;
        CheckCurrent(d, item.Revision);
        var newLine = _options.Tools.NewLine(d.Id);
        byte[] Encode(string text)
        {
            try { return d.Codec.Encode(NormalizeNewLines(text, newLine)); }
            catch (UnrepresentableCharactersException) { throw Invalid("The text cannot be written in the encoding of the document."); }
        }
        var main = Encode(item.Text);
        var edits = new List<ByteEdit> { new(item.Range, main) };
        long shift = 0;
        foreach (var extra in item.AdditionalEdits)
        {
            var insert = Encode(extra.Text);
            edits.Add(new ByteEdit(extra.Range, insert));
            if (extra.Range.Start < item.Range.Start || (extra.Range.Start == item.Range.Start && extra.Range.IsEmpty))
                shift += insert.Length - (extra.Range.End - extra.Range.Start);
        }
        Validate(d, edits);
        GuardSecrets(item.PluginId, d, edits);
        var before = e.Selections?.Invoke();
        var after = SelectionSet.Single(TextSelection.At(item.Range.Start + shift + main.Length));
        var result = d.Apply(edits, new EditOptions
        {
            BaseRevision = item.Revision,
            Source = "plugin:" + item.PluginId,
            SelectionsBefore = before is null ? null : [.. before.Items.Select(s => s.ToRange())],
            SelectionsAfter = [.. after.Items.Select(s => s.ToRange())],
        });
        if (!result.Applied) throw EditFailure(result.Conflict, result.Revision);
        _options.Tools.PlaceSelections(d.Id, after);
        return result.Revision;
    }
}

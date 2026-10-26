using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Documents;

/// <summary>
/// The API for the early tools (outline, Emmet-like expansion, key path): symbols of a revision, reveal, snippets with carets and the Tab key.
/// Every call names the revision it was made for; a stale revision is a <see cref="DocumentErrorCodes.RevisionConflict"/> and changes nothing.
/// </summary>
public sealed partial class DocumentApi
{
    private sealed record Published(SymbolSet Set, bool Current);

    private readonly Dictionary<(string Plugin, Guid Document), Published> _symbols = [];
    private readonly List<string> _tabProviders = [];

    private string LimitedText(JsonElement p, string name, bool allowEmpty = false)
    {
        var value = NeedString(p, name);
        if (value.Length == 0 && !allowEmpty) throw Invalid($"Parameter '{name}' must not be empty.");
        return value.Length <= _options.MaxSymbolTextLength ? value : throw Invalid($"Parameter '{name}' is longer than {_options.MaxSymbolTextLength} characters.");
    }

    // ---- symbols ----

    private List<SymbolInfo> ParseSymbols(JsonElement array, Document d)
    {
        List<SymbolInfo> symbols = [];
        HashSet<string> ids = [];
        foreach (var item in array.EnumerateArray())
        {
            var id = LimitedText(item, "id");
            if (!ids.Add(id)) throw Invalid($"The symbol id '{id}' is used twice.");
            var name = LimitedText(item, "name");
            var kind = item.TryGetProperty("kind", out _) ? LimitedText(item, "kind", allowEmpty: true) : "";
            string? parent = null;
            if (item.TryGetProperty("parent", out var pp) && pp.ValueKind != JsonValueKind.Null)
            {
                parent = LimitedText(item, "parent");
                if (!ids.Contains(parent) || parent == id) throw Invalid($"The parent '{parent}' of the symbol '{id}' must be listed before it.");
            }
            long start = NeedLong(item, "start"), end = NeedLong(item, "end");
            CheckRange(d, start, end);
            symbols.Add(new SymbolInfo(id, name, kind, parent, new ByteRange(start, end)));
        }
        return symbols;
    }

    private static void CheckRange(Document d, long start, long end)
    {
        if (start < 0 || end < start || end > d.ByteLength || !d.IsCharBoundary(start) || !d.IsCharBoundary(end))
            throw Fail(DocumentErrorCodes.InvalidRange, $"The range [{start}, {end}) is outside the document, inside the BOM or not on character boundaries.");
    }

    /// <summary>
    /// Stores the symbols of one revision. Only the current revision is accepted, so an answer that arrives late is rejected and never shown;
    /// when the document moves on, the stored set stays but is reported as outdated (<see cref="IEditorTools.SymbolsChanged"/>).
    /// </summary>
    private async Task<byte[]> PublishSymbolsAsync(string pluginId, JsonElement p)
    {
        var e = Find(NeedDocumentId(p));
        long revision = NeedLong(p, "revision");
        var array = Need(p, "symbols");
        if (array.ValueKind != JsonValueKind.Array) throw Invalid("Parameter 'symbols' must be an array.");
        if (array.GetArrayLength() > _options.MaxSymbolsPerDocument)
            throw Fail(DocumentErrorCodes.SymbolsQuotaExceeded, $"A plugin may publish {_options.MaxSymbolsPerDocument} symbols per document.", w => w.WriteNumber("limit", _options.MaxSymbolsPerDocument));
        return await _owner.InvokeAsync(() =>
        {
            var d = e.Doc;
            lock (_gate)
            {
                if (!_exposed.ContainsKey(d.Id)) throw Fail(DocumentErrorCodes.UnknownDocument, "The document is not open or not available to plugins.");
            }
            CheckCurrent(d, revision);
            var set = new SymbolSet(pluginId, d.Id, revision, ParseSymbols(array, d));
            lock (_gate) _symbols[(pluginId, d.Id)] = new Published(set, true);
            _options.Tools.SymbolsChanged(set, current: true);
            return Obj(w =>
            {
                w.WriteNumber("revision", revision);
                w.WriteNumber("symbols", set.Symbols.Count);
            });
        });
    }

    /// <summary>
    /// The range of a published symbol in the current revision: as published when the set is current, otherwise mapped through the change log.
    /// Null when the symbol is unknown or a later change touched it. Call on the thread that owns the documents.
    /// </summary>
    public ByteRange? ResolveSymbol(string pluginId, Guid document, string symbolId)
    {
        Published? published;
        Exposed? e;
        lock (_gate)
        {
            if (!_symbols.TryGetValue((pluginId, document), out published) || !_exposed.TryGetValue(document, out e)) return null;
        }
        var symbol = published.Set.Symbols.FirstOrDefault(s => s.Id == symbolId);
        if (symbol is null) return null;
        var d = e.Doc;
        long from = published.Set.Revision;
        if (from == d.Revision) return symbol.Range;
        if (from > d.Revision || from < d.OldestMappableRevision) return null;
        var mapped = d.MapRange(symbol.Range, from);
        return mapped.Ok ? mapped.Range : null;
    }

    /// <summary>Marks the sets of this document as outdated; called on the owner thread when it changed.</summary>
    private void MarkSymbolsOutdated(Document d, long revision)
    {
        List<SymbolSet>? outdated = null;
        lock (_gate)
        {
            if (_symbols.Count == 0) return;
            foreach (var (key, published) in _symbols.ToArray())
            {
                if (key.Document != d.Id || !published.Current || published.Set.Revision >= revision) continue;
                _symbols[key] = published with { Current = false };
                (outdated ??= []).Add(published.Set);
            }
        }
        if (outdated is not null)
            foreach (var set in outdated) _options.Tools.SymbolsChanged(set, current: false);
    }

    private void ClearSymbols(Func<(string Plugin, Guid Document), bool> match)
    {
        List<(string Plugin, Guid Document)> gone;
        lock (_gate)
        {
            gone = [.. _symbols.Keys.Where(match)];
            foreach (var key in gone) _symbols.Remove(key);
        }
        foreach (var (plugin, document) in gone) _options.Tools.SymbolsCleared(plugin, document);
    }

    // ---- reveal ----

    /// <summary>Shows a range of the current revision exactly as a click on a symbol of the outline does.</summary>
    private async Task<byte[]> RevealAsync(JsonElement p)
    {
        var e = Find(NeedDocumentId(p));
        long revision = NeedLong(p, "revision"), start = NeedLong(p, "start"), end = NeedLong(p, "end");
        return await _owner.InvokeAsync(() =>
        {
            var d = e.Doc;
            CheckCurrent(d, revision);
            CheckRange(d, start, end);
            if (!_options.Tools.Reveal(d.Id, new ByteRange(start, end))) throw Fail(DocumentErrorCodes.NoTextView, "The document has no text view to show the range in.");
            return Obj(w =>
            {
                w.WriteBoolean("revealed", true);
                w.WriteNumber("revision", revision);
            });
        });
    }

    // ---- snippets ----

    private static string NormalizeNewLines(string text, string newLine)
    {
        if (text.AsSpan().IndexOfAny('\r', '\n') < 0) return text;
        var sb = new StringBuilder(text.Length + 16);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            if (text[i] is '\r' or '\n') sb.Append(newLine); else sb.Append(text[i]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// <c>text</c> goes into every selection of the document, replacing what is selected; <c>carets</c> (UTF-16 offsets in <c>text</c>, the first one primary,
    /// default the end) say where the carets stand afterwards, in each insertion. Line breaks in <c>text</c> become the line break of the document.
    /// One revision and one undo step for all selections, or nothing; undo brings the old selections back.
    /// </summary>
    private async Task<byte[]> InsertSnippetAsync(string pluginId, JsonElement p, PermissionLease lease)
    {
        if (_options.Permissions.IsDelegated(pluginId))
            throw new RpcException(DocumentErrorCodes.PermissionDenied, "A command another plugin started cannot write documents.")
            { DataJson = $"{{\"permission\":\"{DocumentPermissions.EditDirect}\",\"reason\":\"Delegated\"}}" };
        var e = Find(NeedDocumentId(p));
        long revision = NeedLong(p, "revision");
        var text = NeedString(p, "text");
        if (text.Length > _options.MaxSnippetLength) throw Invalid($"A snippet is at most {_options.MaxSnippetLength} characters long.");
        var carets = ParseCarets(p, text);

        return await _owner.InvokeAsync(() =>
        {
            lease.Token.ThrowIfCancellationRequested();
            var d = e.Doc;
            CheckCurrent(d, revision);
            var selections = e.Selections?.Invoke() ?? throw Fail(DocumentErrorCodes.NoTextView, "The document has no text view to take selections from.");
            var newLine = _options.Tools.NewLine(d.Id);
            byte[] insert;
            long[] caretBytes;
            try
            {
                insert = d.Codec.Encode(NormalizeNewLines(text, newLine));
                caretBytes = [.. carets.Select(c => (long)d.Codec.Encode(NormalizeNewLines(text[..c], newLine)).Length)];
            }
            catch (UnrepresentableCharactersException) { throw Invalid("The text cannot be written in the encoding of the document."); }

            var edits = selections.Items.Select(s => new ByteEdit(s.ToRange(), insert)).ToList();
            Validate(d, edits);
            List<TextSelection> placed = new(selections.Count * caretBytes.Length);
            int primary = 0;
            long shift = 0;
            for (int i = 0; i < selections.Count; i++)
            {
                var s = selections.Items[i];
                if (i == selections.Primary) primary = placed.Count;
                foreach (var c in caretBytes) placed.Add(TextSelection.At(s.Start + shift + c));
                shift += insert.Length - s.Length;
            }
            var after = SelectionSet.Create(placed, primary);
            var result = d.Apply(edits, new EditOptions
            {
                BaseRevision = revision,
                Source = "plugin:" + pluginId,
                SelectionsBefore = [.. selections.Items.Select(s => s.ToRange())],
                SelectionsAfter = [.. after.Items.Select(s => s.ToRange())],
            });
            if (!result.Applied) throw EditFailure(result.Conflict, result.Revision);
            _options.Tools.PlaceSelections(d.Id, after);
            return Obj(w =>
            {
                w.WriteBoolean("applied", true);
                w.WriteNumber("revision", result.Revision);
                w.WriteNumber("carets", after.Count);
            });
        });
    }

    private int[] ParseCarets(JsonElement p, string text)
    {
        if (!p.TryGetProperty("carets", out var array) || array.ValueKind == JsonValueKind.Null) return [text.Length];
        if (array.ValueKind != JsonValueKind.Array) throw Invalid("Parameter 'carets' must be an array.");
        int count = array.GetArrayLength();
        if (count < 1 || count > _options.MaxSnippetCarets) throw Invalid($"A snippet has 1 to {_options.MaxSnippetCarets} carets.");
        var carets = new int[count];
        int n = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var at) || at < 0 || at > text.Length) throw Invalid("A caret must be an offset inside 'text'.");
            bool splitsPair = at > 0 && at < text.Length && ((char.IsHighSurrogate(text[at - 1]) && char.IsLowSurrogate(text[at])) || (text[at - 1] == '\r' && text[at] == '\n'));
            if (splitsPair) throw Invalid($"The caret {at} falls inside a character or a line break.");
            carets[n++] = at;
        }
        return carets;
    }

    // ---- Tab ----

    private byte[] RegisterTab(string pluginId, JsonElement p)
    {
        bool enabled = !p.TryGetProperty("enabled", out var v) || v.ValueKind != JsonValueKind.False;
        lock (_gate)
        {
            if (!enabled) _tabProviders.Remove(pluginId);
            else if (!_tabProviders.Contains(pluginId)) _tabProviders.Add(pluginId);
        }
        return Obj(w => w.WriteBoolean("enabled", enabled));
    }

    /// <summary>True when a plugin offered to answer the Tab key; the editor then calls <see cref="OfferTabAsync"/>.</summary>
    public bool HasTabProviders
    {
        get { lock (_gate) return _tabProviders.Count > 0; }
    }

    /// <summary>Whether plugins can see this document at the moment.</summary>
    public bool IsExposed(Guid document)
    {
        lock (_gate) return _exposed.ContainsKey(document);
    }

    /// <summary>
    /// Asks the plugins that registered for Tab, in the order they registered, whether one handles the key at this revision and these selections.
    /// False when none does, when none is there, or when asking failed or took longer than the time limit: the editor then does what Tab does without plugins.
    /// A plugin that handled it has changed the document through the other calls. Only a cancelled <paramref name="cancellation"/> throws.
    /// </summary>
    public async Task<bool> OfferTabAsync(Guid document, CancellationToken cancellation = default)
    {
        string[] providers;
        Exposed? e;
        lock (_gate)
        {
            providers = [.. _tabProviders];
            _exposed.TryGetValue(document, out e);
        }
        if (providers.Length == 0 || e is null) return false;
        foreach (var plugin in providers)
        {
            if (!_options.Permissions.IsGranted(plugin, DocumentPermissions.Read, GrantScope.OfDocument(document))) continue;
            var (revision, set) = await _owner.InvokeAsync(() => (e.Doc.Revision, e.Selections?.Invoke()));
            var request = Obj(w =>
            {
                w.WriteString("document", document);
                w.WriteNumber("revision", revision);
                w.WriteNumber("primary", set?.Primary ?? -1);
                w.WriteStartArray("selections");
                foreach (var s in set?.Items ?? [])
                {
                    w.WriteStartObject();
                    w.WriteNumber("anchor", s.Anchor);
                    w.WriteNumber("caret", s.Caret);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            });
            try
            {
                var answer = await _request(plugin, DocumentRequests.Tab, request, _options.TabTimeout, cancellation);
                if (answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("handled", out var handled) && handled.ValueKind == JsonValueKind.True) return true;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception) { } // a plugin that fails, hangs or is gone must not take Tab away from the user
        }
        return false;
    }
}

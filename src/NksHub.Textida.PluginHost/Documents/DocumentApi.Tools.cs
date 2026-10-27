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

    /// <summary>
    /// The symbols a plugin published for a document, and whether they belong to the current revision (otherwise they are outdated and a jump maps them
    /// through the change log, see <see cref="ResolveSymbol"/>). Empty when the plugin published none.
    /// </summary>
    public IReadOnlyList<SymbolInfo> SymbolsOf(string pluginId, Guid document, out bool current)
    {
        lock (_gate)
        {
            if (_symbols.TryGetValue((pluginId, document), out var published))
            {
                current = published.Current;
                return published.Set.Symbols;
            }
        }
        current = false;
        return [];
    }

    /// <summary>Whether any plugin has symbols published for the document, so withdrawing it would take them away.</summary>
    public bool HasSymbols(Guid document)
    {
        lock (_gate) return _symbols.Keys.Any(key => key.Document == document);
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

    private sealed record SnippetItem(long Start, long End, string Text, int[] Carets);

    /// <summary>
    /// <c>text</c> goes into every selection of the document, replacing what is selected; <c>carets</c> (UTF-16 offsets in <c>text</c>, the first one primary,
    /// default the end) say where the carets stand afterwards, in each insertion. Line breaks in <c>text</c> become the line break of the document.
    /// Instead of <c>text</c> a plugin may send <c>items</c>: each has its own byte range <c>start</c>/<c>end</c> of the revision, its own <c>text</c> and <c>carets</c>
    /// (an abbreviation in front of the caret, a selection wrapped in markup); the ranges must not overlap and must not touch a hidden secret.
    /// One revision and one undo step for all of them, or nothing; undo brings the old selections back.
    /// </summary>
    private async Task<byte[]> InsertSnippetAsync(string pluginId, JsonElement p, PermissionLease lease)
    {
        if (_options.Permissions.IsDelegated(pluginId))
            throw new RpcException(DocumentErrorCodes.PermissionDenied, "A command another plugin started cannot write documents.")
            { DataJson = $"{{\"permission\":\"{DocumentPermissions.EditDirect}\",\"reason\":\"Delegated\"}}" };
        var e = Find(NeedDocumentId(p));
        long revision = NeedLong(p, "revision");
        List<SnippetItem>? explicitItems = null;
        string text = "";
        int[] carets = [];
        if (p.TryGetProperty("items", out var itemsJson) && itemsJson.ValueKind != JsonValueKind.Null) explicitItems = ParseSnippetItems(itemsJson);
        else
        {
            text = NeedString(p, "text");
            if (text.Length > _options.MaxSnippetLength) throw Invalid($"A snippet is at most {_options.MaxSnippetLength} characters long.");
            carets = ParseCarets(p, text);
        }

        return await _owner.InvokeAsync(() =>
        {
            lease.Token.ThrowIfCancellationRequested();
            var d = e.Doc;
            CheckCurrent(d, revision);
            var selections = e.Selections?.Invoke() ?? throw Fail(DocumentErrorCodes.NoTextView, "The document has no text view to take selections from.");
            var newLine = _options.Tools.NewLine(d.Id);
            var items = explicitItems ?? [.. selections.Items.Select(s => new SnippetItem(s.Start, s.End, text, carets))];
            items = [.. items.OrderBy(i => i.Start)];
            var edits = new List<ByteEdit>(items.Count);
            var caretBytes = new long[items.Count][];
            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    edits.Add(new ByteEdit(new ByteRange(item.Start, item.End), d.Codec.Encode(NormalizeNewLines(item.Text, newLine))));
                    caretBytes[i] = [.. item.Carets.Select(c => (long)d.Codec.Encode(NormalizeNewLines(item.Text[..c], newLine)).Length)];
                }
            }
            catch (UnrepresentableCharactersException) { throw Invalid("The text cannot be written in the encoding of the document."); }

            Validate(d, edits);
            if (explicitItems is not null) GuardSecrets(pluginId, d, edits);
            var main = selections.Items[selections.Primary];
            List<TextSelection> placed = new(items.Count);
            int primary = 0;
            bool found = false;
            long shift = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (!found && (explicitItems is null ? i == selections.Primary : items[i].Start <= main.Caret && main.Caret <= items[i].End)) (primary, found) = (placed.Count, true);
                foreach (var c in caretBytes[i]) placed.Add(TextSelection.At(items[i].Start + shift + c));
                shift += edits[i].Insert.Length - (items[i].End - items[i].Start);
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

    private List<SnippetItem> ParseSnippetItems(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0) throw Invalid("Parameter 'items' must be a non-empty array.");
        List<SnippetItem> items = [];
        foreach (var item in array.EnumerateArray())
        {
            long start = NeedLong(item, "start"), end = NeedLong(item, "end");
            if (start < 0 || end < start) throw Invalid($"The range [{start}, {end}) is not valid.");
            var text = NeedString(item, "text");
            if (text.Length > _options.MaxSnippetLength) throw Invalid($"A snippet is at most {_options.MaxSnippetLength} characters long.");
            items.Add(new SnippetItem(start, end, text, ParseCarets(item, text)));
        }
        return items;
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
    /// <paramref name="language"/> (the language mode id) and <paramref name="indent"/> (the text one indentation level is written as) go to the plugin with the request.
    /// </summary>
    public async Task<bool> OfferTabAsync(Guid document, CancellationToken cancellation = default, string? language = null, string? indent = null)
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
                if (language is not null) w.WriteString("language", language);
                if (indent is not null) w.WriteString("indent", indent);
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

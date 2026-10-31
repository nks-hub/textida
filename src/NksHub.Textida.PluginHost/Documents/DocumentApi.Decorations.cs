using System.Text.Json;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.PluginHost.Documents;

/// <summary>
/// The API for decorations a plugin publishes and the editor draws: inlay hints (a short text shown at one place of the line, between two characters) and semantic tokens (a type name for a stretch of text,
/// which the editor colours like the syntax of that kind). A plugin never draws. Only the current revision is accepted, so an answer that arrives late is refused and never shown; after an edit a
/// decoration is moved through the change log and dropped when the edit touched its text.
/// </summary>
public sealed partial class DocumentApi
{
    private readonly Dictionary<(string Plugin, Guid Document, string Kind), DecorationSet> _decorations = [];

    private async Task<byte[]> PublishDecorationsAsync(string pluginId, JsonElement p)
    {
        var e = Find(NeedDocumentId(p));
        long revision = NeedLong(p, "revision");
        var kind = NeedString(p, "kind");
        if (kind is not (DecorationKinds.Inlay or DecorationKinds.Semantic)) throw Invalid("Parameter 'kind' must be inlay or semantic.");
        var array = Need(p, "items");
        if (array.ValueKind != JsonValueKind.Array) throw Invalid("Parameter 'items' must be an array.");
        if (array.GetArrayLength() > _options.MaxDecorationsPerDocument)
            throw Fail(DocumentErrorCodes.DecorationsQuotaExceeded, $"A plugin may publish {_options.MaxDecorationsPerDocument} decorations of one kind per document.", w => w.WriteNumber("limit", _options.MaxDecorationsPerDocument));
        return await _owner.InvokeAsync(() =>
        {
            var d = e.Doc;
            lock (_gate)
            {
                if (!_exposed.ContainsKey(d.Id)) throw Fail(DocumentErrorCodes.UnknownDocument, "The document is not open or not available to plugins.");
            }
            CheckCurrent(d, revision);
            List<Decoration> items = [];
            foreach (var item in array.EnumerateArray())
            {
                long start = NeedLong(item, "start"), end = kind == DecorationKinds.Inlay ? start : NeedLong(item, "end");
                CheckRange(d, start, end);
                items.Add(new Decoration(new ByteRange(start, end), LimitedText(item, "text")));
            }
            var key = (pluginId, d.Id, kind);
            lock (_gate)
            {
                if (items.Count == 0) _decorations.Remove(key);
                else _decorations[key] = new DecorationSet(pluginId, d.Id, kind, d.Revision, items);
            }
            _options.Tools.DecorationsChanged(d.Id);
            return Obj(w =>
            {
                w.WriteNumber("revision", d.Revision);
                w.WriteNumber("accepted", items.Count);
            });
        });
    }

    /// <summary>The decorations of all plugins for a document, with their ranges in the current revision. Call on the thread that owns the documents.</summary>
    public IReadOnlyList<PlacedDecoration> DecorationsOf(Guid document)
    {
        lock (_gate)
        {
            if (_decorations.Count == 0) return [];
            return [.. _decorations.Values.Where(s => s.Document == document).SelectMany(s => s.Items.Select(x => new PlacedDecoration(s.PluginId, s.Kind, x.Range, x.Text)))];
        }
    }

    /// <summary>Moves the stored decorations of a changed document to its new revision; one whose text the change touched is dropped. Runs on the thread that changed the document.</summary>
    private void MoveDecorations(Document d, long revision)
    {
        bool any = false;
        lock (_gate)
        {
            if (_decorations.Count == 0) return;
            foreach (var (key, set) in _decorations.ToArray())
            {
                if (key.Document != d.Id || set.Revision >= revision) continue;
                any = true;
                List<Decoration> moved = [];
                if (set.Revision >= d.OldestMappableRevision)
                    foreach (var x in set.Items)
                    {
                        var m = d.MapRange(x.Range, set.Revision);
                        if (m.Ok && m.Range.End <= d.ByteLength && d.IsCharBoundary(m.Range.Start) && d.IsCharBoundary(m.Range.End)) moved.Add(x with { Range = m.Range });
                    }
                if (moved.Count == 0) _decorations.Remove(key);
                else _decorations[key] = set with { Revision = revision, Items = moved };
            }
        }
        if (any) _options.Tools.DecorationsChanged(d.Id);
    }

    private void ClearDecorations(Func<(string Plugin, Guid Document, string Kind), bool> match)
    {
        HashSet<Guid> documents = [];
        lock (_gate)
        {
            foreach (var key in _decorations.Keys.Where(match).ToArray())
            {
                _decorations.Remove(key);
                documents.Add(key.Document);
            }
        }
        foreach (var document in documents) _options.Tools.DecorationsChanged(document);
    }
}

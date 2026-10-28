using System.Buffers;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.PluginHost.Documents;

/// <summary>Read-only inspection of one revision: file facts, byte patterns, log levels and the symbols plugins published. Every call names its revision.</summary>
public sealed partial class DocumentApi
{
    private const int FactsBlockBytes = 1 << 16;
    private const int MaxBytePatternText = 4096;
    private const int DefaultMatches = 100;
    private const int DefaultLogLines = 20;

    private static int OptionalCount(JsonElement p, string name, int fallback, int max)
    {
        if (!p.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return Math.Min(fallback, max);
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n >= 1 ? Math.Min(n, max) : throw Invalid($"Parameter '{name}' must be a positive integer.");
    }

    private static long OptionalOffset(JsonElement p, string name, long fallback)
    {
        if (!p.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return fallback;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n >= 0 ? n : throw Invalid($"Parameter '{name}' must be a non-negative integer.");
    }

    /// <summary>
    /// Encoding, byte order mark, storage mode and, from a scan of at most <see cref="DocumentApiOptions.MaxScanBytes"/>, the line endings and the byte sequences
    /// that are not valid in the encoding. <c>complete</c> says whether the scan reached the end; the counts are of what was scanned.
    /// </summary>
    private async Task<byte[]> FactsAsync(string pluginId, JsonElement p, CancellationToken ct)
    {
        var t = await ResolveAsync(pluginId, p);
        var e = Find(t.Snap.DocumentId);
        var (caps, path, lines, lineComplete) = await _owner.InvokeAsync(() => (e.Doc.Capabilities, e.Doc.FilePath, e.Doc.LineIndexState.Lines, e.Doc.LineIndexState.Complete));
        var snap = t.Snap;
        var codec = snap.Codec.WithBom(false);
        long end = Math.Min(snap.ByteLength, t.Bom + _options.MaxScanBytes);
        var eol = new EolScanner();
        int invalid = 0;
        long scanned = t.Bom;
        var buffer = ArrayPool<byte>.Shared.Rent(FactsBlockBytes);
        try
        {
            Guarded(snap, () =>
            {
                while (scanned < end)
                {
                    ct.ThrowIfCancellationRequested();
                    int got = snap.Read(scanned, buffer.AsSpan(0, (int)Math.Min(FactsBlockBytes, end - scanned)));
                    if (got == 0) break;
                    int whole = codec.CompleteLength(buffer.AsSpan(0, got));
                    if (whole == 0) whole = got - got % codec.Alignment;
                    if (whole == 0) break;
                    var decoded = codec.Decode(buffer.AsMemory(0, whole).ToArray());
                    invalid += decoded.InvalidCount;
                    eol.Add(decoded.Text);
                    scanned += whole;
                }
                return 0;
            });
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        var stats = eol.Finish();
        return Obj(w =>
        {
            w.WriteNumber("revision", snap.Revision);
            w.WriteNumber("byteLength", snap.ByteLength);
            w.WriteString("codec", snap.Codec.Name);
            w.WriteNumber("bomLength", t.Bom);
            w.WriteString("bom", Convert.ToHexString(snap.ReadBytes(new ByteRange(0, t.Bom))));
            if (path is not null) w.WriteString("path", path); else w.WriteNull("path");
            w.WriteString("storage", caps.Name);
            w.WriteBoolean("largeMode", !caps.MaterializesText);
            w.WriteStartObject("lineIndex");
            w.WriteNumber("lines", lines);
            w.WriteBoolean("complete", lineComplete);
            w.WriteEndObject();
            w.WriteStartObject("lineEndings");
            w.WriteNumber("lf", stats.Lf);
            w.WriteNumber("crlf", stats.CrLf);
            w.WriteNumber("cr", stats.Cr);
            w.WriteString("style", stats.Label);
            w.WriteString("dominant", stats.Dominant.ToString().ToLowerInvariant());
            w.WriteEndObject();
            w.WriteNumber("invalidSequences", invalid);
            w.WriteNumber("scannedBytes", scanned - t.Bom);
            w.WriteBoolean("complete", scanned >= snap.ByteLength);
        });
    }

    /// <summary>
    /// Matches of a hex pattern (<c>4D 5A ?? 90</c>, <c>?</c> is a wildcard nibble). A match that touches a hidden value of a secrets file is left out, as in <c>document/find</c>.
    /// Like <c>document/find</c> it looks at the starts of matches in at most <see cref="DocumentApiOptions.MaxScanBytes"/> bytes: past that <c>truncated</c> is true and <c>next</c> is the <c>start</c> to go on from.
    /// </summary>
    private async Task<byte[]> FindBytesAsync(string pluginId, JsonElement p, CancellationToken ct)
    {
        var t = await ResolveAsync(pluginId, p);
        var text = NeedString(p, "pattern");
        if (text.Length == 0 || text.Length > MaxBytePatternText) throw Invalid($"Parameter 'pattern' must hold 1 to {MaxBytePatternText} characters.");
        BytePattern pattern;
        try { pattern = BytePattern.ParseHex(text); }
        catch (FormatException ex) { throw Invalid("The pattern is not valid hex: " + ex.Message); }
        catch (ArgumentException ex) { throw Invalid("The pattern is not valid hex: " + ex.Message); }
        long from = OptionalOffset(p, "start", 0), to = OptionalOffset(p, "end", t.Snap.ByteLength);
        if (to < from || to > t.Snap.ByteLength) throw Fail(DocumentErrorCodes.InvalidRange, $"The range [{from}, {to}) is outside the document ({t.Snap.ByteLength} bytes).");
        int max = OptionalCount(p, "max", DefaultMatches, _options.MaxFindMatches);
        // matches that start before scanEnd are looked for (the last ones may reach past it); a stop there says where to go on
        long scanEnd = ScanEnd(from, to);
        bool limited = scanEnd < to;
        long readEnd = Math.Min(to, scanEnd + pattern.Length - 1);
        // one match more than asked for tells that there are more; a search that ends with exactly the cap is complete, as in document/find
        ByteSearchResult result = Guarded(t.Snap, () => ByteSearch.Find(t.Snap, pattern, new ByteSearchOptions { MaxResults = max + 1, Range = new ByteRange(from, readEnd) }, ct));
        var visible = result.Matches.Where(m => m.Start < scanEnd && (t.Secrets is null || !SecretValues.Overlaps(t.Secrets, m.Start, m.End))).ToList();
        bool capped = visible.Count > max;
        var matches = capped ? visible.GetRange(0, max) : visible;
        // the search limit can also be used up by hidden matches: then it goes on after the last match it looked at
        long? next = capped ? null : result.LimitReached ? Math.Min(result.Matches[^1].Start + 1, scanEnd) : limited ? scanEnd : null;
        return Obj(w =>
        {
            w.WriteNumber("revision", t.Snap.Revision);
            w.WriteNumber("length", pattern.Length);
            w.WriteStartArray("matches");
            foreach (var m in matches)
            {
                w.WriteStartObject();
                w.WriteNumber("start", m.Start);
                w.WriteNumber("end", m.End);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteBoolean("truncated", capped || result.LimitReached || limited);
            if (next is { } go) w.WriteNumber("next", go); else w.WriteNull("next");
        });
    }

    /// <summary>
    /// The next lines of a level (<c>error</c>, <c>warning</c>, <c>info</c> or <c>debug</c>) from <c>start</c>, read from a snapshot in blocks: the memory used does not depend on the size
    /// of the file. A call reads at most <see cref="DocumentApiOptions.MaxScanBytes"/>; <c>next</c> is where a following call goes on, null once the end is reached.
    /// </summary>
    private async Task<byte[]> FindLogAsync(string pluginId, JsonElement p, CancellationToken ct)
    {
        var t = await ResolveAsync(pluginId, p);
        var snap = t.Snap;
        LogLevel level = NeedString(p, "level") switch
        {
            "error" => LogLevel.Error, "warning" => LogLevel.Warning, "info" => LogLevel.Info, "debug" => LogLevel.Debug,
            _ => throw Invalid("Parameter 'level' must be error, warning, info or debug."),
        };
        long start = OptionalOffset(p, "start", t.Bom);
        if (start > snap.ByteLength) throw Fail(DocumentErrorCodes.InvalidRange, $"The offset {start} is outside the document ({snap.ByteLength} bytes).");
        int max = OptionalCount(p, "max", DefaultLogLines, 1000);
        var rules = _options.LogRules?.Invoke(snap.DocumentId) ?? LogLevelRules.Default;
        var codec = snap.Codec.WithBom(false);
        var found = new List<(long Offset, string Text, bool Cut)>();
        long? next = null;
        Guarded(snap, () =>
        {
            long pos = LogScan.LineStart(snap, codec, t.Bom, Math.Max(start, t.Bom)), budgetStart = pos;
            if (pos < start) pos = LogScan.NextLineStart(snap, codec, pos); // a start inside a line goes on with the next whole line
            while (found.Count < max)
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
                long reached = pos;
                long? hit;
                try
                {
                    hit = LogScan.Find(snap, codec, t.Bom, rules, level, pos, forward: true, s =>
                    {
                        reached = s;
                        if (s - budgetStart > _options.MaxScanBytes) budget.Cancel();
                    }, budget.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    next = reached;
                    return 0;
                }
                if (hit is not { } offset) return 0;
                long lineEnd = LogScan.NextLineStart(snap, codec, offset);
                long take = Math.Min(lineEnd - offset, _options.MaxReadBytes);
                take -= take % codec.Alignment;
                var bytes = snap.ReadBytes(new ByteRange(offset, offset + take));
                if (t.Secrets is not null) SecretValues.MaskBytes(bytes, offset, t.Secrets, codec);
                var line = codec.Decode(bytes.AsMemory(0, codec.CompleteLength(bytes))).Text.TrimEnd('\r', '\n');
                found.Add((offset, line, take < lineEnd - offset));
                pos = lineEnd;
                if (pos >= snap.ByteLength) return 0;
            }
            next = pos;
            return 0;
        });
        return Obj(w =>
        {
            w.WriteNumber("revision", snap.Revision);
            w.WriteString("level", NeedString(p, "level"));
            w.WriteStartArray("lines");
            foreach (var (offset, text, cut) in found)
            {
                w.WriteStartObject();
                w.WriteNumber("offset", offset);
                w.WriteString("text", text);
                w.WriteBoolean("truncated", cut);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            if (next is { } n && n < snap.ByteLength) w.WriteNumber("next", n); else w.WriteNull("next");
        });
    }

    /// <summary>Every symbol set published for the document, by plugin; <c>current</c> is false once the document moved past the revision of a set.</summary>
    private async Task<byte[]> SymbolsListAsync(JsonElement p)
    {
        var e = Find(NeedDocumentId(p));
        var revision = await _owner.InvokeAsync(() => e.Doc.Revision);
        List<(SymbolSet Set, bool Current)> sets;
        lock (_gate) sets = [.. _symbols.Where(x => x.Key.Document == e.Doc.Id).OrderBy(x => x.Key.Plugin, StringComparer.Ordinal).Select(x => (x.Value.Set, x.Value.Current))];
        return Obj(w =>
        {
            w.WriteNumber("revision", revision);
            w.WriteStartArray("sets");
            foreach (var (set, current) in sets)
            {
                w.WriteStartObject();
                w.WriteString("plugin", set.PluginId);
                w.WriteNumber("revision", set.Revision);
                w.WriteBoolean("current", current);
                w.WriteStartArray("symbols");
                foreach (var s in set.Symbols)
                {
                    w.WriteStartObject();
                    w.WriteString("id", s.Id);
                    w.WriteString("name", s.Name);
                    w.WriteString("kind", s.Kind);
                    if (s.ParentId is not null) w.WriteString("parent", s.ParentId); else w.WriteNull("parent");
                    w.WriteNumber("start", s.Range.Start);
                    w.WriteNumber("end", s.Range.End);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }

    /// <summary>The stored diagnostics of a document at its current revision. A diagnostic whose range touches a hidden secret value is counted in <c>withheld</c> and not returned, as its message could quote the value.</summary>
    private async Task<byte[]> DiagnosticsListAsync(string pluginId, JsonElement p)
    {
        var e = Find(NeedDocumentId(p));
        var (revision, hidden, items) = await _owner.InvokeAsync(() =>
        {
            var snap = e.Doc.GetSnapshot();
            return (e.Doc.Revision, Hidden(pluginId, e.Doc.Id, SecretsOf(e.Doc, snap, e.Doc.BomLength)), DiagnosticsOf(e.Doc.Id));
        });
        var shown = hidden is null ? items : [.. items.Where(x => !SecretValues.Overlaps(hidden, x.Range.Start, x.Range.End))];
        return Obj(w =>
        {
            w.WriteNumber("revision", revision);
            w.WriteNumber("withheld", items.Count - shown.Count);
            w.WriteStartArray("diagnostics");
            foreach (var x in shown)
            {
                w.WriteStartObject();
                w.WriteString("plugin", x.PluginId);
                w.WriteString("source", x.Source);
                w.WriteString("severity", x.Severity.ToString().ToLowerInvariant());
                w.WriteString("message", x.Message);
                if (x.Code is not null) w.WriteString("code", x.Code);
                w.WriteNumber("start", x.Range.Start);
                w.WriteNumber("end", x.Range.End);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }
}

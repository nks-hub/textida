using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.TextTools;

/// <summary>Limits of the text tools; the editor passes both on the command line, so no number is fixed in the plugin.</summary>
/// <param name="ChunkBytes">Bytes read from the document per request; must not exceed what the document API returns in one read.</param>
/// <param name="ProgressInterval">Time between two progress reports of a running task.</param>
public sealed record TextToolsSettings(int ChunkBytes, TimeSpan ProgressInterval);

/// <summary>
/// The built-in text tools in one process: statistics and hashes stream the document in chunks from a snapshot (the text is never held whole),
/// run as a cancellable task with progress, and report an unfinished run as such. Block calculation, expression evaluation and base conversion
/// work on a selection or on what the user types. Commands arrive as <c>command/execute</c>; the answer is <c>{"status":"..."}</c>.
/// </summary>
public sealed class TextToolsPlugin : PluginBase
{
    private readonly TextToolsSettings _settings;

    public TextToolsPlugin(TextToolsSettings settings) : base("text-tools", apiVersion: 1)
    {
        _settings = settings;
        On("command/execute", ExecuteAsync);
    }

    public const string Statistics = "text.stats";
    public const string Hash = "text.hash";
    public const string Calculate = "text.calculate";
    public const string Expression = "text.expression";
    public const string Bases = "text.bases";

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tasks = new();

    private sealed record Context(string Document, long Revision, string Language, JsonElement Parameters);

    private sealed record Range(long Start, long End);

    protected override ValueTask OnEventAsync(JsonElement eventPayload)
    {
        if (eventPayload.TryGetProperty("name", out var name) && name.GetString() == UiEvents.TaskCancelled
            && eventPayload.TryGetProperty("payload", out var payload) && payload.TryGetProperty("task", out var task)
            && task.GetString() is { } id && _tasks.TryGetValue(id, out var source))
            source.Cancel();
        return ValueTask.CompletedTask;
    }

    private async ValueTask<byte[]?> ExecuteAsync(JsonElement p, CancellationToken ct)
    {
        var command = p.GetProperty("command").GetString();
        var context = new Context(p.GetProperty("document").GetString()!, p.GetProperty("revision").GetInt64(),
            p.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : "en", p);
        string status;
        try
        {
            status = command switch
            {
                Statistics => await RunStatisticsAsync(context, ct),
                Hash => await RunHashAsync(context, ct),
                Calculate => await RunCalculateAsync(context, ct),
                Expression => await RunExpressionAsync(context, ct),
                Bases => await RunBasesAsync(context, ct),
                _ => throw new RpcException(RpcErrorCodes.InvalidParams, "Unknown command."),
            };
        }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.RevisionConflict)
        {
            status = await ReportAsync("stale", NoticeLevel.Warning, Texts.Get(context.Language, "stale"), ct);
        }
        catch (RpcException e) when (e.Code is DocumentErrorCodes.SnapshotExpired or DocumentErrorCodes.UnknownDocument or DocumentErrorCodes.InvalidRange)
        {
            status = await ReportAsync("failed", NoticeLevel.Error, e.Message, ct);
        }
        return Encoding.UTF8.GetBytes($"{{\"status\":\"{status}\"}}");
    }

    // ---- editor calls ----

    private async Task<JsonElement> CallAsync(string method, string paramsJson, CancellationToken ct) =>
        (await Rpc.RequestAsync(method, Encoding.UTF8.GetBytes(paramsJson), null, ct)).Clone();

    private static string Quote(string text) => JsonEncodedText.Encode(text).ToString();

    private async Task<string> ReportAsync(string status, NoticeLevel level, string message, CancellationToken ct)
    {
        await CallAsync(UiMethods.Notify, $"{{\"level\":\"{level.ToString().ToLowerInvariant()}\",\"message\":\"{Quote(message)}\"}}", ct);
        return status;
    }

    private sealed record Info(long Length, TextCodec Codec, int Bom);

    private async Task<Info> InfoAsync(Context c, CancellationToken ct)
    {
        var info = await CallAsync(DocumentMethods.Info, $"{{\"document\":\"{c.Document}\"}}", ct);
        if (info.GetProperty("revision").GetInt64() != c.Revision) throw new RpcException(DocumentErrorCodes.RevisionConflict, "Stale.");
        return new Info(info.GetProperty("byteLength").GetInt64(), TextCodec.ByName(info.GetProperty("codec").GetString()!) ?? TextCodec.Utf8, (int)info.GetProperty("bomLength").GetInt64());
    }

    /// <summary>The non-empty selections of the document in document order, as byte ranges.</summary>
    private async Task<List<Range>> SelectedAsync(Context c, CancellationToken ct)
    {
        var selections = await CallAsync(DocumentMethods.Selections, $"{{\"document\":\"{c.Document}\"}}", ct);
        if (selections.GetProperty("revision").GetInt64() != c.Revision) throw new RpcException(DocumentErrorCodes.RevisionConflict, "Stale.");
        return [.. selections.GetProperty("selections").EnumerateArray()
            .Select(s => (A: s.GetProperty("anchor").GetInt64(), C: s.GetProperty("caret").GetInt64()))
            .Where(s => s.A != s.C).Select(s => new Range(Math.Min(s.A, s.C), Math.Max(s.A, s.C))).OrderBy(r => r.Start)];
    }

    private async Task<string> ReadTextAsync(Context c, Range r, CancellationToken ct)
    {
        var read = await CallAsync(DocumentMethods.ReadText, $"{{\"document\":\"{c.Document}\",\"revision\":{c.Revision},\"start\":{r.Start},\"end\":{r.End}}}", ct);
        return read.GetProperty("text").GetString()!;
    }

    // ---- streamed commands ----

    private sealed class Streamed(TextToolsPlugin owner, string snapshot, string task) : IAsyncDisposable
    {
        public ChunkReader Reader => async (offset, buffer, ct) =>
        {
            var result = await owner.Rpc.RequestAsync(DocumentMethods.ReadBytes,
                Encoding.UTF8.GetBytes($"{{\"snapshot\":\"{snapshot}\",\"start\":{offset},\"end\":{offset + buffer.Length}}}"), null, ct);
            var bytes = result.GetProperty("bytes").GetBytesFromBase64();
            bytes.CopyTo(buffer);
            return bytes.Length;
        };

        private readonly Stopwatch _since = Stopwatch.StartNew();

        public Func<long, ValueTask> Progress(long total, string what) => async done =>
        {
            if (_since.Elapsed < owner._settings.ProgressInterval) return;
            _since.Restart();
            var fraction = total == 0 ? 1.0 : (double)done / total;
            await owner.CallAsync(UiMethods.TaskProgress,
                $"{{\"task\":\"{task}\",\"fraction\":{fraction.ToString("R", CultureInfo.InvariantCulture)},\"message\":\"{Quote(what)}\"}}", CancellationToken.None);
        };

        public async ValueTask DisposeAsync()
        {
            owner._tasks.TryRemove(task, out _);
            await owner.CallAsync(UiMethods.TaskEnd, $"{{\"task\":\"{task}\"}}", CancellationToken.None);
            await owner.CallAsync(DocumentMethods.SnapshotRelease, $"{{\"snapshot\":\"{snapshot}\"}}", CancellationToken.None);
        }
    }

    /// <summary>Pins the revision of the command, starts a cancellable task and returns the pieces a scan needs, with the token that the user's cancel fires.</summary>
    private async Task<(Streamed Scan, CancellationTokenSource Cancel)> BeginAsync(Context c, string title, CancellationToken ct)
    {
        var snapshot = await CallAsync(DocumentMethods.SnapshotCreate, $"{{\"document\":\"{c.Document}\"}}", ct);
        var snapshotId = snapshot.GetProperty("snapshot").GetString()!;
        if (snapshot.GetProperty("revision").GetInt64() != c.Revision)
        {
            await CallAsync(DocumentMethods.SnapshotRelease, $"{{\"snapshot\":\"{snapshotId}\"}}", CancellationToken.None);
            throw new RpcException(DocumentErrorCodes.RevisionConflict, "Stale.");
        }
        var task = await CallAsync(UiMethods.TaskStart, $"{{\"title\":\"{Quote(title)}\",\"cancellable\":true}}", ct);
        var taskId = task.GetProperty("task").GetString()!;
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _tasks[taskId] = source;
        return (new Streamed(this, snapshotId, taskId), source);
    }

    private async Task<string> RunStatisticsAsync(Context c, CancellationToken ct)
    {
        var info = await InfoAsync(c, ct);
        var (scan, cancel) = await BeginAsync(c, Texts.Get(c.Language, "stats.task"), ct);
        await using var _ = scan;
        using var __ = cancel;
        var statistics = new TextStatistics(info.Codec);
        var total = info.Length - info.Bom;
        var outcome = await ByteScan.RunAsync(info.Bom, info.Length, _settings.ChunkBytes, scan.Reader, statistics.Add, scan.Progress(total, ""), cancel.Token);
        ct.ThrowIfCancellationRequested();
        var result = statistics.Finish(info.Length);
        var format = NumberFormat.For(c.Language);
        var text = Texts.Get(c.Language, "stats.result", ("bytes", format.Format(result.Bytes)), ("characters", format.Format(result.Characters)), ("words", format.Format(result.Words)),
            ("lines", format.Format(result.Lines)), ("longest", format.Format(result.LongestLine)));
        if (outcome.Completed) return await ReportAsync("completed", NoticeLevel.Info, text, ct);
        var partial = Texts.Get(c.Language, "stats.partial", ("done", format.Format(outcome.Done)), ("total", format.Format(total)));
        return await ReportAsync("incomplete", NoticeLevel.Warning, partial + text, ct);
    }

    private async Task<string> RunHashAsync(Context c, CancellationToken ct)
    {
        var info = await InfoAsync(c, ct);
        var chosen = c.Parameters.TryGetProperty("algorithm", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString()! : await PickAlgorithmAsync(c, ct);
        if (chosen.Length == 0) return "cancelled";
        if (!BlockHash.TryGet(chosen, out var algorithm))
            return await ReportAsync("failed", NoticeLevel.Error, Texts.Get(c.Language, "hash.unknown", ("algorithm", chosen)), ct);
        var selected = await SelectedAsync(c, ct);
        if (selected.Count > 1) return await ReportAsync("failed", NoticeLevel.Error, Texts.Get(c.Language, "hash.severalRanges"), ct);
        var range = selected.Count == 1 ? selected[0] : new Range(0, info.Length);
        var (scan, cancel) = await BeginAsync(c, Texts.Get(c.Language, "hash.task", ("algorithm", algorithm.Label)), ct);
        await using var _ = scan;
        using var __ = cancel;
        var (hex, outcome) = await BlockHash.ComputeAsync(algorithm.Algorithm, range.Start, range.End, _settings.ChunkBytes, scan.Reader, scan.Progress(range.End - range.Start, algorithm.Label), cancel.Token);
        ct.ThrowIfCancellationRequested();
        var format = NumberFormat.For(c.Language);
        if (hex is null)
            return await ReportAsync("cancelled", NoticeLevel.Warning, Texts.Get(c.Language, "hash.cancelled", ("done", format.Format(outcome.Done)), ("total", format.Format(range.End - range.Start))), ct);
        return await ReportAsync("completed", NoticeLevel.Info, Texts.Get(c.Language, "hash.result", ("algorithm", algorithm.Label), ("bytes", format.Format(range.End - range.Start)), ("hash", hex)), ct);
    }

    /// <summary>The id of the algorithm the user picks, or an empty string when the pick is dismissed.</summary>
    private async Task<string> PickAlgorithmAsync(Context c, CancellationToken ct)
    {
        var items = string.Join(',', BlockHash.Algorithms.Select(x => $"{{\"id\":\"{x.Id}\",\"label\":\"{x.Label}\"}}"));
        var answer = await CallAsync(UiMethods.QuickPick,
            $"{{\"title\":\"{Quote(Texts.Get(c.Language, "hash.pickTitle"))}\",\"placeholder\":\"{Quote(Texts.Get(c.Language, "hash.pickPlaceholder"))}\",\"items\":[{items}]}}", ct);
        return answer.GetProperty("id").ValueKind == JsonValueKind.String ? answer.GetProperty("id").GetString()! : "";
    }

    // ---- commands on a selection or on typed text ----

    private async Task<string> RunCalculateAsync(Context c, CancellationToken ct)
    {
        await InfoAsync(c, ct);
        var selected = await SelectedAsync(c, ct);
        if (selected.Count == 0) return await ReportAsync("failed", NoticeLevel.Warning, Texts.Get(c.Language, "calc.noSelection"), ct);
        var output = NumberFormat.For(c.Language);
        var big = selected.FirstOrDefault(r => r.End - r.Start > _settings.ChunkBytes);
        if (big is not null)
            return await ReportAsync("tooLarge", NoticeLevel.Error, Texts.Get(c.Language, "calc.tooLarge", ("size", output.Format(big.End - big.Start)), ("max", output.Format(_settings.ChunkBytes))), ct);
        var texts = new List<string>();
        foreach (var r in selected) texts.Add(await ReadTextAsync(c, r, ct));
        BlockResult result;
        try
        {
            // "numberFormat" forces the format of the data; otherwise it is recognised from the text. The answer is in the format of the interface language.
            result = c.Parameters.TryGetProperty("numberFormat", out var f) && f.ValueKind == JsonValueKind.String
                ? BlockCalculator.Calculate(texts, NumberFormat.For(f.GetString()!))
                : BlockCalculator.CalculateAuto(texts, c.Language).Result;
        }
        catch (OverflowException) { return await ReportAsync("failed", NoticeLevel.Error, Texts.Get(c.Language, "calc.overflow"), ct); }
        if (result.Count == 0) return await ReportAsync("failed", NoticeLevel.Warning, Texts.Get(c.Language, "calc.none"), ct);
        var text = Texts.Get(c.Language, "calc.result", ("count", output.Format(result.Count)), ("sum", output.Format(result.Sum)), ("min", output.Format(result.Min)),
            ("max", output.Format(result.Max)), ("average", output.Format(result.Average)));
        if (result.Ignored > 0) text += Texts.Get(c.Language, "calc.ignored", ("ignored", output.Format(result.Ignored)));
        return await ReportAsync("completed", NoticeLevel.Info, text, ct);
    }

    /// <summary>Asks for a line of text, starting with the selection when it is one short range; null when the user dismisses the question.</summary>
    private async Task<string?> AskAsync(Context c, string titleKey, string labelKey, CancellationToken ct)
    {
        await InfoAsync(c, ct);
        var selected = await SelectedAsync(c, ct);
        var start = selected.Count == 1 && selected[0].End - selected[0].Start <= _settings.ChunkBytes ? await ReadTextAsync(c, selected[0], ct) : "";
        var answer = await CallAsync(UiMethods.Input,
            $"{{\"title\":\"{Quote(Texts.Get(c.Language, titleKey))}\",\"label\":\"{Quote(Texts.Get(c.Language, labelKey))}\",\"value\":\"{Quote(start.Length <= 1000 ? start : "")}\"}}", ct);
        return answer.GetProperty("value").ValueKind == JsonValueKind.String ? answer.GetProperty("value").GetString() : null;
    }

    private async Task<string> RunExpressionAsync(Context c, CancellationToken ct)
    {
        var typed = c.Parameters.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : await AskAsync(c, "expr.title", "expr.label", ct);
        if (typed is null) return "cancelled";
        var format = NumberFormat.For(c.Language);
        try
        {
            var value = ExpressionEvaluator.Evaluate(typed);
            return await ReportAsync("completed", NoticeLevel.Info, Texts.Get(c.Language, "expr.result", ("expression", typed.Trim()), ("result", format.Format(value))), ct);
        }
        catch (ExpressionException e)
        {
            var key = e.Error switch
            {
                ExpressionError.UnknownName => "expr.unknownName",
                ExpressionError.Domain => "expr.domain",
                ExpressionError.Range => "expr.range",
                ExpressionError.DivisionByZero => "expr.divisionByZero",
                _ => "expr.syntax",
            };
            return await ReportAsync("failed", NoticeLevel.Error, Texts.Get(c.Language, key, ("position", (e.Position + 1).ToString()), ("detail", e.Detail)), ct);
        }
        catch (InsufficientExecutionStackException)
        {
            return await ReportAsync("failed", NoticeLevel.Error, Texts.Get(c.Language, "expr.syntax", ("position", "1"), ("detail", "(")), ct);
        }
    }

    private async Task<string> RunBasesAsync(Context c, CancellationToken ct)
    {
        var typed = c.Parameters.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : await AskAsync(c, "base.title", "base.label", ct);
        if (typed is null) return "cancelled";
        if (!BaseConverter.TryParse(typed, out var value))
            return await ReportAsync("failed", NoticeLevel.Error, Texts.Get(c.Language, "base.invalid", ("text", typed.Trim())), ct);
        static string Prefixed(BigInteger v, int radix, string prefix) => v.Sign < 0 ? "-" + prefix + BaseConverter.ToRadix(-v, radix) : prefix + BaseConverter.ToRadix(v, radix);
        return await ReportAsync("completed", NoticeLevel.Info, Texts.Get(c.Language, "base.result", ("dec", value.ToString(CultureInfo.InvariantCulture)),
            ("hex", Prefixed(value, 16, "0x")), ("oct", Prefixed(value, 8, "0o")), ("bin", Prefixed(value, 2, "0b"))), ct);
    }
}

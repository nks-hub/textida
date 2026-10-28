using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.TextTools;

/// <summary>Limits of the text tools; the editor passes both on the command line, so no number is fixed in the plugin.</summary>
/// <param name="ChunkBytes">Bytes read from the document per request; must not exceed what the document API returns in one read.</param>
/// <param name="ProgressInterval">Time between two progress reports of a running task.</param>
/// <param name="FrequencyTop">How many entries the frequency lists of the statistics show.</param>
/// <param name="FrequencyMaxWords">Most distinct words the statistics keep for their frequency list; zero switches the frequency lists off.</param>
/// <param name="FrequencyMaxWordLength">Characters of a word that count towards its frequency entry.</param>
public sealed record TextToolsSettings(int ChunkBytes, TimeSpan ProgressInterval, int FrequencyTop = 0, int FrequencyMaxWords = 0, int FrequencyMaxWordLength = 0);

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

    /// <summary>Everything happens inside a command, so the editor may end the process between commands.</summary>
    protected override bool IdleStop => true;

    public const string Statistics = "text.stats";
    public const string Hash = "text.hash";
    public const string Calculate = "text.calculate";
    public const string Expression = "text.expression";
    public const string Bases = "text.bases";
    public const string CopyAsMarkdown = "table.copyAs.markdown";
    public const string CopyAsSql = "table.copyAs.sql";
    public const string CopyAsJson = "table.copyAs.json";
    public const string CopyAsHtml = "table.copyAs.html";

    private sealed record Context(string Document, long Revision, string Language, JsonElement Parameters);

    private sealed record Range(long Start, long End);

    /// <summary>What a command answers: the status, and more members of the result object already written as JSON (starting with a comma) for the commands that hand data back to the editor.</summary>
    private readonly record struct Answer(string Status, string? Json = null)
    {
        public static implicit operator Answer(string status) => new(status);
    }

    private async ValueTask<byte[]?> ExecuteAsync(JsonElement p, CancellationToken ct)
    {
        var command = p.GetProperty("command").GetString();
        var context = new Context(p.GetProperty("document").GetString()!, p.GetProperty("revision").GetInt64(),
            p.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : "en", p);
        Answer status;
        try
        {
            status = command switch
            {
                Statistics => await RunStatisticsAsync(context, ct),
                Hash => await RunHashAsync(context, ct),
                Calculate => await RunCalculateAsync(context, ct),
                Expression => await RunExpressionAsync(context, ct),
                Bases => await RunBasesAsync(context, ct),
                CopyAsMarkdown or CopyAsSql or CopyAsJson or CopyAsHtml => await RunCopyTableAsync(context, command, ct),
                _ => throw new RpcException(RpcErrorCodes.InvalidParams, "Unknown command."),
            };
        }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.RevisionConflict)
        {
            status = await ReportAsync("stale", NoticeLevel.Warning, Texts.Get(context.Language, "stale"), ct);
        }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.NoTableView)
        {
            status = await ReportAsync("failed", NoticeLevel.Warning, Texts.Get(context.Language, "table.none"), ct);
        }
        catch (RpcException e) when (e.Code is DocumentErrorCodes.SnapshotExpired or DocumentErrorCodes.UnknownDocument or DocumentErrorCodes.InvalidRange)
        {
            status = await ReportAsync("failed", NoticeLevel.Error, e.Message, ct);
        }
        return Encoding.UTF8.GetBytes($"{{\"status\":\"{status.Status}\"{status.Json}}}");
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
    private async Task<List<Range>> SelectedAsync(Context c, CancellationToken ct) =>
        [.. (await CaretsAsync(c, ct)).Where(s => s.Anchor != s.Caret).Select(s => new Range(Math.Min(s.Anchor, s.Caret), Math.Max(s.Anchor, s.Caret))).OrderBy(r => r.Start)];

    /// <summary>Every selection of the document, empty ones (a bare caret) included.</summary>
    private async Task<List<(long Anchor, long Caret)>> CaretsAsync(Context c, CancellationToken ct)
    {
        var selections = await CallAsync(DocumentMethods.Selections, $"{{\"document\":\"{c.Document}\"}}", ct);
        if (selections.GetProperty("revision").GetInt64() != c.Revision) throw new RpcException(DocumentErrorCodes.RevisionConflict, "Stale.");
        return [.. selections.GetProperty("selections").EnumerateArray().Select(s => (s.GetProperty("anchor").GetInt64(), s.GetProperty("caret").GetInt64()))];
    }

    private static string? Text(Context c, string name) => c.Parameters.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Flag(Context c, string name) => c.Parameters.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

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
            owner.ForgetTask(task);
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
        var source = TrackTask(taskId, ct);
        return (new Streamed(this, snapshotId, taskId), source);
    }

    private async Task<string> RunStatisticsAsync(Context c, CancellationToken ct)
    {
        var info = await InfoAsync(c, ct);
        var selected = await SelectedAsync(c, ct);
        var ranges = selected.Count > 0 ? selected : [new Range(info.Bom, info.Length)];
        var total = ranges.Sum(r => r.End - r.Start);
        var (scan, cancel) = await BeginAsync(c, Texts.Get(c.Language, "stats.task"), ct);
        await using var _ = scan;
        using var __ = cancel;
        var statistics = new TextStatistics(info.Codec, _settings.FrequencyMaxWords, _settings.FrequencyMaxWordLength);
        var progress = scan.Progress(total, "");
        long done = 0;
        bool completed = true;
        foreach (var r in ranges)
        {
            var before = done;
            var outcome = await ByteScan.RunAsync(r.Start, r.End, _settings.ChunkBytes, scan.Reader, statistics.Add, d => progress(before + d), cancel.Token);
            done += outcome.Done;
            statistics.Break();
            if (!(completed = outcome.Completed)) break;
        }
        ct.ThrowIfCancellationRequested();
        var result = statistics.Finish(selected.Count > 0 ? total : info.Length);
        var format = NumberFormat.For(c.Language);
        var text = (selected.Count > 0 ? Texts.Get(c.Language, "stats.selection") : "")
            + Texts.Get(c.Language, "stats.result", ("bytes", format.Format(result.Bytes)), ("characters", format.Format(result.Characters)), ("words", format.Format(result.Words)),
                ("lines", format.Format(result.Lines)), ("longest", format.Format(result.LongestLine)));
        if (_settings is { FrequencyMaxWords: > 0, FrequencyTop: > 0 })
        {
            string List(IReadOnlyList<(string Text, long Count)> top) => string.Join(", ", top.Select(x => $"{x.Text} ({format.Format(x.Count)})"));
            if (statistics.TopCharacters(1).Count > 0)
                text += Texts.Get(c.Language, "stats.topCharacters", ("list", List(statistics.TopCharacters(_settings.FrequencyTop))))
                    + Texts.Get(c.Language, "stats.topWords", ("list", List(statistics.TopWords(_settings.FrequencyTop))));
            if (statistics.WordTableFull) text += Texts.Get(c.Language, "stats.wordsCapped");
        }
        if (completed) return await ReportAsync("completed", NoticeLevel.Info, text, ct);
        var partial = Texts.Get(c.Language, "stats.partial", ("done", format.Format(done)), ("total", format.Format(total)));
        return await ReportAsync("incomplete", NoticeLevel.Warning, partial + text, ct);
    }

    /// <summary>
    /// The hash of the selections (or of the document). With <c>dialog</c> the editor shows the result in its own dialog (the one the hex view uses for its checksums, with Copy and Compare),
    /// so nothing is shown, copied or compared here; the answer carries <c>algorithm</c>, <c>selection</c> and <c>blocks</c> (<c>start</c>, <c>end</c> and <c>hash</c> of each part) for it.
    /// </summary>
    private async Task<Answer> RunHashAsync(Context c, CancellationToken ct)
    {
        var info = await InfoAsync(c, ct);
        bool dialog = Flag(c, "dialog");
        var expected = dialog ? null : Text(c, "expected");
        if (!dialog && Flag(c, "compare") && expected is null)
        {
            expected = await AskHashAsync(c, ct);
            if (expected is null) return "cancelled";
        }
        expected = expected is null ? null : Checksums.Digits(expected);
        var chosen = Text(c, "algorithm");
        if (expected is not null && chosen is null) chosen = BlockHash.IdForHexLength(expected.Length); // another length: the user picks the algorithm
        chosen ??= await PickAlgorithmAsync(c, ct);
        if (chosen.Length == 0) return "cancelled";
        if (!BlockHash.TryGet(chosen, out var algorithm))
            return await ReportAsync("failed", NoticeLevel.Error, Texts.Get(c.Language, "hash.unknown", ("algorithm", chosen)), ct);
        var selected = await SelectedAsync(c, ct);
        var ranges = selected.Count > 0 ? selected : [new Range(0, info.Length)];
        var (scan, cancel) = await BeginAsync(c, Texts.Get(c.Language, "hash.task", ("algorithm", algorithm.Name)), ct);
        await using var _ = scan;
        using var __ = cancel;
        var format = NumberFormat.For(c.Language);
        var lines = new List<string>();
        var hashes = new List<string>();
        var blocks = new List<string>();
        bool allMatch = true;
        foreach (var range in ranges)
        {
            long length = range.End - range.Start;
            var (hex, outcome) = await BlockHash.ComputeAsync(algorithm, range.Start, range.End, _settings.ChunkBytes, scan.Reader, scan.Progress(length, algorithm.Name), cancel.Token);
            ct.ThrowIfCancellationRequested();
            if (hex is null)
                return await ReportAsync("cancelled", NoticeLevel.Warning, Texts.Get(c.Language, "hash.cancelled", ("done", format.Format(outcome.Done)), ("total", format.Format(length))), ct);
            var line = (ranges.Count > 1 ? Texts.Get(c.Language, "hash.block", ("number", (lines.Count + 1).ToString())) : "")
                + Texts.Get(c.Language, "hash.result", ("algorithm", algorithm.Name), ("bytes", format.Format(length)), ("hash", hex));
            if (expected is not null)
            {
                bool match = Checksums.SameValue(hex, expected);
                allMatch &= match;
                line += Texts.Get(c.Language, match ? "hash.match" : "hash.differ");
            }
            lines.Add(line);
            hashes.Add(hex);
            blocks.Add($"{{\"start\":{range.Start},\"end\":{range.End},\"hash\":\"{Quote(hex)}\"}}");
        }
        string data = $",\"algorithm\":\"{Quote(algorithm.Name)}\",\"selection\":{(selected.Count > 0 ? "true" : "false")},\"blocks\":[{string.Join(',', blocks)}]";
        if (dialog) return new Answer("completed", data);
        var message = string.Join('\n', lines);
        if (Flag(c, "copy"))
        {
            await CallAsync(DocumentMethods.ClipboardWrite, $"{{\"document\":\"{c.Document}\",\"text\":\"{Quote(string.Join('\n', hashes))}\"}}", ct);
            message += Texts.Get(c.Language, "hash.copied");
        }
        return new Answer(await ReportAsync(allMatch ? "completed" : "mismatch", allMatch ? NoticeLevel.Info : NoticeLevel.Warning, message, ct), data);
    }

    /// <summary>Asks for the hash to compare with; null when the user dismisses the question.</summary>
    private async Task<string?> AskHashAsync(Context c, CancellationToken ct)
    {
        var answer = await CallAsync(UiMethods.Input,
            $"{{\"title\":\"{Quote(Texts.Get(c.Language, "hash.compareTitle"))}\",\"label\":\"{Quote(Texts.Get(c.Language, "hash.compareLabel"))}\",\"value\":\"\"}}", ct);
        return answer.GetProperty("value").ValueKind == JsonValueKind.String ? answer.GetProperty("value").GetString() : null;
    }

    /// <summary>The id of the algorithm the user picks, or an empty string when the pick is dismissed.</summary>
    private async Task<string> PickAlgorithmAsync(Context c, CancellationToken ct)
    {
        var items = string.Join(',', BlockHash.Algorithms.Select(x => $"{{\"id\":\"{Quote(x.Id)}\",\"label\":\"{Quote(x.Name)}\"}}"));
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

    /// <summary>
    /// The text a command works on: the only selection when it is short, else (with <paramref name="caretLine"/>) the line of the first caret. <c>End</c> is the byte
    /// offset behind it, where a result can be inserted; null when there is no such text.
    /// </summary>
    private async Task<(string Text, long End)?> SourceAsync(Context c, bool caretLine, CancellationToken ct)
    {
        var info = await InfoAsync(c, ct);
        var carets = await CaretsAsync(c, ct);
        var selected = carets.Where(s => s.Anchor != s.Caret).ToList();
        if (selected.Count == 1)
        {
            var r = new Range(Math.Min(selected[0].Anchor, selected[0].Caret), Math.Max(selected[0].Anchor, selected[0].Caret));
            return r.End - r.Start <= _settings.ChunkBytes ? (await ReadTextAsync(c, r, ct), r.End) : null;
        }
        if (selected.Count > 0 || !caretLine || carets.Count == 0) return null;
        try
        {
            long Offset(JsonElement e) => e.GetProperty("offset").GetInt64();
            var position = await CallAsync(DocumentMethods.OffsetToPosition, $"{{\"document\":\"{c.Document}\",\"revision\":{c.Revision},\"offset\":{carets[0].Caret}}}", ct);
            long line = position.GetProperty("line").GetInt64();
            string At(long n) => $"{{\"document\":\"{c.Document}\",\"revision\":{c.Revision},\"line\":{n},\"column\":0}}";
            long start = Offset(await CallAsync(DocumentMethods.PositionToOffset, At(line), ct)), end = info.Length;
            try { end = Offset(await CallAsync(DocumentMethods.PositionToOffset, At(line + 1), ct)); }
            catch (RpcException e) when (e.Code != DocumentErrorCodes.RevisionConflict) { }
            if (end <= start || end - start > _settings.ChunkBytes) return null;
            var text = (await ReadTextAsync(c, new Range(start, end), ct)).TrimEnd('\r', '\n');
            return (text, start + info.Codec.Encode(text).Length);
        }
        catch (RpcException e) when (e.Code != DocumentErrorCodes.RevisionConflict) { return null; }
    }

    /// <summary>Asks for a line of text, starting with <paramref name="start"/>; null when the user dismisses the question.</summary>
    private async Task<string?> AskAsync(Context c, string titleKey, string labelKey, string start, CancellationToken ct)
    {
        var answer = await CallAsync(UiMethods.Input,
            $"{{\"title\":\"{Quote(Texts.Get(c.Language, titleKey))}\",\"label\":\"{Quote(Texts.Get(c.Language, labelKey))}\",\"value\":\"{Quote(start.Length <= 1000 ? start : "")}\"}}", ct);
        return answer.GetProperty("value").ValueKind == JsonValueKind.String ? answer.GetProperty("value").GetString() : null;
    }

    /// <summary>
    /// Evaluates the typed text (the <c>text</c> parameter), or what the user confirms, starting with the selection or the line of the caret. With <c>insert</c> it takes
    /// that text without asking and writes " = result" behind it, in the plain invariant form so that the file does not depend on the language of the interface.
    /// </summary>
    private async Task<string> RunExpressionAsync(Context c, CancellationToken ct)
    {
        bool insert = Flag(c, "insert");
        var source = Text(c, "text") is null ? await SourceAsync(c, caretLine: true, ct) : null;
        var typed = Text(c, "text") ?? (insert ? source?.Text : await AskAsync(c, "expr.title", "expr.label", source?.Text ?? "", ct));
        if (typed is null && insert) return await ReportAsync("failed", NoticeLevel.Warning, Texts.Get(c.Language, "expr.noSource"), ct);
        if (typed is null) return "cancelled";
        var format = NumberFormat.For(c.Language);
        try
        {
            var value = ExpressionEvaluator.Evaluate(typed);
            if (insert && source is { } at)
                await CallAsync(DocumentMethods.EditApply,
                    $"{{\"document\":\"{c.Document}\",\"revision\":{c.Revision},\"batchId\":\"{Guid.NewGuid():N}\",\"edits\":[{{\"start\":{at.End},\"end\":{at.End},\"text\":\"{Quote(" = " + value.ToString("R", CultureInfo.InvariantCulture))}\"}}]}}", ct);
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

    /// <summary>Converts a whole number (decimal, 0x, 0b, 0o prefixed, or a Roman numeral) to the other bases, to a Roman numeral and to the date it is as Unix milliseconds.</summary>
    private async Task<string> RunBasesAsync(Context c, CancellationToken ct)
    {
        var typed = Text(c, "text") ?? await AskAsync(c, "base.title", "base.label", (await SourceAsync(c, caretLine: false, ct))?.Text ?? "", ct);
        if (typed is null) return "cancelled";
        if (!BaseConverter.TryParse(typed, out var value) && !BaseConverter.TryParseRoman(typed, out value))
            return await ReportAsync("failed", NoticeLevel.Error, Texts.Get(c.Language, "base.invalid", ("text", typed.Trim())), ct);
        static string Prefixed(BigInteger v, int radix, string prefix) => v.Sign < 0 ? "-" + prefix + BaseConverter.ToRadix(-v, radix) : prefix + BaseConverter.ToRadix(v, radix);
        var text = Texts.Get(c.Language, "base.result", ("dec", value.ToString(CultureInfo.InvariantCulture)),
            ("hex", Prefixed(value, 16, "0x")), ("oct", Prefixed(value, 8, "0o")), ("bin", Prefixed(value, 2, "0b")));
        if (BaseConverter.ToRoman(value) is { } roman) text += Texts.Get(c.Language, "base.roman", ("roman", roman));
        if (value >= DateTimeOffset.MinValue.ToUnixTimeMilliseconds() && value <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
            text += Texts.Get(c.Language, "base.date", ("date", DateTimeOffset.FromUnixTimeMilliseconds((long)value).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)));
        return await ReportAsync("completed", NoticeLevel.Info, text, ct);
    }

    // ---- copy the selected cells as ... ----

    private static IReadOnlyList<string> Values(JsonElement cells, int columns)
    {
        var values = new string[columns];
        Array.Fill(values, "");
        int i = 0;
        foreach (var cell in cells.EnumerateArray())
            if (i < columns) values[i++] = cell.GetProperty("value").GetString()!;
        return values;
    }

    /// <summary>
    /// Reads the cells selected in the table view at the revision of the command and puts them on the clipboard as Markdown, SQL INSERT, JSON or HTML (the HTML form
    /// also as rich HTML). The SQL form needs a table name: the <c>table</c> parameter, or what the user types, starting with the name of the file; the <c>dialect</c>
    /// parameter decides the quoting (<see cref="TableExport.Sql"/>).
    /// </summary>
    private async Task<string> RunCopyTableAsync(Context c, string command, CancellationToken ct)
    {
        var selection = await CallAsync(DocumentMethods.TableSelection, $"{{\"document\":\"{c.Document}\",\"revision\":{c.Revision}}}", ct);
        int columns = selection.GetProperty("columns").GetInt32();
        var header = selection.GetProperty("header") is { ValueKind: JsonValueKind.Array } h ? Values(h, columns) : null;
        var rows = selection.GetProperty("rows").EnumerateArray().Select(r => Values(r.GetProperty("cells"), columns)).ToList();
        var table = new TableData(selection.GetProperty("firstColumn").GetInt32(), columns, header, rows);

        string text, form;
        string? html = null;
        switch (command)
        {
            case CopyAsMarkdown: (text, form) = (TableExport.Markdown(table), "Markdown"); break;
            case CopyAsJson: (text, form) = (TableExport.Json(table), "JSON"); break;
            case CopyAsHtml: (text, form) = (html = TableExport.Html(table), "HTML"); break;
            default:
                var name = c.Parameters.TryGetProperty("table", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : await AskTableNameAsync(c, ct);
                if (string.IsNullOrWhiteSpace(name)) return "cancelled";
                var dialect = c.Parameters.TryGetProperty("dialect", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : "generic";
                (text, form) = (TableExport.Sql(table, name.Trim(), dialect), "SQL INSERT");
                break;
        }
        await CallAsync(DocumentMethods.ClipboardWrite, $"{{\"document\":\"{c.Document}\",\"text\":\"{Quote(text)}\"{(html is null ? "" : $",\"html\":\"{Quote(html)}\"")}}}", ct);
        return await ReportAsync("completed", NoticeLevel.Info, Texts.Get(c.Language, "table.copied", ("format", form), ("rows", rows.Count.ToString()), ("columns", columns.ToString())), ct);
    }

    /// <summary>Asks for the table name of the INSERT statements, starting with the name of the file without its extension; null when the user dismisses the question.</summary>
    private async Task<string?> AskTableNameAsync(Context c, CancellationToken ct)
    {
        var info = await CallAsync(DocumentMethods.Info, $"{{\"document\":\"{c.Document}\"}}", ct);
        var start = info.GetProperty("path") is { ValueKind: JsonValueKind.String } path ? Path.GetFileNameWithoutExtension(path.GetString()!) : "";
        var answer = await CallAsync(UiMethods.Input,
            $"{{\"title\":\"{Quote(Texts.Get(c.Language, "table.sqlTitle"))}\",\"label\":\"{Quote(Texts.Get(c.Language, "table.sqlLabel"))}\",\"value\":\"{Quote(start)}\"}}", ct);
        return answer.GetProperty("value").ValueKind == JsonValueKind.String ? answer.GetProperty("value").GetString() : null;
    }
}

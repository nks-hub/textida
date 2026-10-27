using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.Emmet;

/// <summary>Limits of the plugin; the editor passes them on the command line, so no number is fixed in the plugin.</summary>
/// <param name="ContextBytes">Bytes read in front of a caret to find the abbreviation, its line and the CSS block; must not exceed what one read returns.</param>
/// <param name="MaxChars">Longest expansion; the editor refuses a snippet longer than this.</param>
/// <param name="MaxCarets">Most carets one expansion places; the editor refuses more.</param>
public sealed record EmmetSettings(int ContextBytes, int MaxChars, int MaxCarets);

/// <summary>
/// The built-in Emmet: the abbreviation in front of the caret becomes markup (HTML) or declarations (CSS) when Tab is pressed, a preview shows the result first,
/// and a selection can be wrapped in an abbreviation. It answers the Tab key of the editor (<c>editor/tab</c>) and says "not handled" for everything it does not
/// understand, so the editor then indents as without it. Expansion is one snippet insertion into all carets, so it is one undo step.
/// The abbreviation expanders are written from the public Emmet syntax description; no code or data files of the Emmet projects are used.
/// </summary>
public sealed class EmmetPlugin : PluginBase
{
    public const string PreviewCommand = "emmet.preview";
    public const string WrapCommand = "emmet.wrap";

    private readonly EmmetSettings _settings;

    public EmmetPlugin(EmmetSettings settings) : base("emmet", apiVersion: 1)
    {
        _settings = settings;
        On("command/execute", ExecuteAsync);
        On(DocumentRequests.Tab, TabAsync);
    }

    /// <summary>Offers the Tab key to the editor on every start, so a restarted process answers it again.</summary>
    protected override async ValueTask OnResyncAsync(JsonElement state) => await CallAsync(DocumentMethods.TabRegister, "{\"enabled\":true}", CancellationToken.None);

    private async Task<JsonElement> CallAsync(string method, string paramsJson, CancellationToken ct) =>
        (await Rpc.RequestAsync(method, Encoding.UTF8.GetBytes(paramsJson), null, ct)).Clone();

    private static string Quote(string text) => JsonEncodedText.Encode(text).ToString();

    private async Task NotifyAsync(NoticeLevel level, string message, CancellationToken ct) =>
        await CallAsync(UiMethods.Notify, $"{{\"level\":\"{level.ToString().ToLowerInvariant()}\",\"message\":\"{Quote(message)}\"}}", ct);

    private static string? SyntaxOf(string? language) => language switch { "html" => "html", "css" => "css", _ => null };

    // ---- reading ----

    private sealed record Doc(string Id, long Revision, TextCodec Codec, int Bom);

    private async Task<Doc> InfoAsync(string id, long revision, CancellationToken ct)
    {
        var info = await CallAsync(DocumentMethods.Info, $"{{\"document\":\"{id}\"}}", ct);
        if (info.GetProperty("revision").GetInt64() != revision) throw new RpcException(DocumentErrorCodes.RevisionConflict, "Stale.");
        return new Doc(id, revision, TextCodec.ByName(info.GetProperty("codec").GetString()!) ?? TextCodec.Utf8, (int)info.GetProperty("bomLength").GetInt64());
    }

    private sealed record Before(string Text, bool Complete, int[] Invalid);

    /// <summary>The text in front of a position, at most the context size of it; the start moves up to a few bytes on when it does not fall on a character boundary.</summary>
    private async Task<Before?> ReadBeforeAsync(Doc d, long position, CancellationToken ct)
    {
        long start = Math.Max(d.Bom, position - _settings.ContextBytes);
        for (int shift = 0; shift < 8 && start + shift <= position; shift++)
        {
            try
            {
                var read = await CallAsync(DocumentMethods.ReadText, $"{{\"document\":\"{d.Id}\",\"revision\":{d.Revision},\"start\":{start + shift},\"end\":{position}}}", ct);
                return new Before(read.GetProperty("text").GetString()!, start + shift == d.Bom, [.. read.GetProperty("invalid").EnumerateArray().Select(i => i.GetInt32())]);
            }
            catch (RpcException e) when (e.Code == DocumentErrorCodes.InvalidRange) { }
        }
        return null;
    }

    private static string LineIndent(string line) => line[..(line.Length - line.TrimStart(' ', '\t').Length)];

    private static int LineStart(string text) => Math.Max(text.LastIndexOf('\n'), text.LastIndexOf('\r')) + 1;

    // ---- expansion ----

    private sealed record Item(long Start, long End, string Text, IReadOnlyList<int> Carets, string Abbreviation);

    /// <summary>The expansion at every caret; null when any caret has no abbreviation in front of it (then nothing is expanded anywhere).</summary>
    private async Task<List<Item>?> BuildItemsAsync(Doc d, IReadOnlyList<long> carets, string syntax, string indent, CancellationToken ct)
    {
        var items = new List<Item>();
        foreach (var caret in carets)
        {
            if (await ReadBeforeAsync(d, caret, ct) is not { } before) return null;
            int lineStart = LineStart(before.Text);
            if (lineStart == 0 && !before.Complete) return null;
            string line = before.Text[lineStart..];
            int at = AbbreviationText.Find(syntax, line);
            if (at < 0 || before.Invalid.Any(i => i >= lineStart + at)) return null;
            string abbreviation = line[at..];
            if (syntax == "css" && !CssExpander.InsideBlock(before.Text[..(lineStart + at)])) return null;
            var options = new ExpandOptions(indent, LineIndent(line), _settings.MaxChars, _settings.MaxCarets);
            var expansion = syntax == "css" ? CssExpander.Expand(abbreviation, options) : HtmlExpander.Expand(abbreviation, options);
            if (expansion is null) return null;
            items.Add(new Item(caret - d.Codec.WithBom(false).Encode(abbreviation).Length, caret, expansion.Text, expansion.Carets, abbreviation));
        }
        return items;
    }

    private async Task InsertAsync(Doc d, List<Item> items, CancellationToken ct)
    {
        var json = string.Join(',', items.Select(i => $"{{\"start\":{i.Start},\"end\":{i.End},\"text\":\"{Quote(i.Text)}\",\"carets\":[{string.Join(',', i.Carets)}]}}"));
        await CallAsync(DocumentMethods.SnippetInsert, $"{{\"document\":\"{d.Id}\",\"revision\":{d.Revision},\"items\":[{json}]}}", ct);
    }

    private static string IndentOf(JsonElement p) => p.TryGetProperty("indent", out var i) && i.ValueKind == JsonValueKind.String && i.GetString() is { Length: > 0 } text ? text : "\t";

    private async ValueTask<byte[]?> TabAsync(JsonElement p, CancellationToken ct)
    {
        bool handled = false;
        try
        {
            string? syntax = SyntaxOf(p.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null);
            var selections = p.GetProperty("selections");
            if (syntax is not null && selections.GetArrayLength() > 0 && selections.EnumerateArray().All(s => s.GetProperty("anchor").GetInt64() == s.GetProperty("caret").GetInt64()))
            {
                var carets = selections.EnumerateArray().Select(s => s.GetProperty("caret").GetInt64()).Distinct().Order().ToList();
                var d = await InfoAsync(p.GetProperty("document").GetString()!, p.GetProperty("revision").GetInt64(), ct);
                if (await BuildItemsAsync(d, carets, syntax, IndentOf(p), ct) is { } items)
                {
                    await InsertAsync(d, items, ct);
                    handled = true;
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) { } // anything unexpected leaves Tab to the editor
        return Encoding.UTF8.GetBytes(handled ? "{\"handled\":true}" : "{\"handled\":false}");
    }

    // ---- commands ----

    private static string Status(string status) => $"{{\"status\":\"{status}\"}}";

    private async ValueTask<byte[]?> ExecuteAsync(JsonElement p, CancellationToken ct)
    {
        string command = p.GetProperty("command").GetString()!, document = p.GetProperty("document").GetString()!;
        long revision = p.GetProperty("revision").GetInt64();
        string ui = p.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : "en";
        string? syntax = SyntaxOf(p.TryGetProperty("syntax", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null);
        string result;
        try
        {
            if (command is not (PreviewCommand or WrapCommand)) throw new RpcException(RpcErrorCodes.InvalidParams, "Unknown command.");
            if (syntax is null) result = Status("unsupported");
            else
            {
                var d = await InfoAsync(document, revision, ct);
                result = command == PreviewCommand ? await PreviewAsync(d, syntax, IndentOf(p), ui, ct) : await WrapAsync(d, syntax, IndentOf(p), ui, ct);
            }
        }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.RevisionConflict)
        {
            await NotifyAsync(NoticeLevel.Warning, Texts.Get(ui, "stale"), ct);
            result = Status("stale");
        }
        catch (RpcException e) when (e.Code is DocumentErrorCodes.UnknownDocument or DocumentErrorCodes.InvalidRange or DocumentErrorCodes.NoTextView or DocumentErrorCodes.PermissionDenied or RpcErrorCodes.InvalidParams)
        {
            await NotifyAsync(NoticeLevel.Error, Texts.Get(ui, "failed"), ct);
            result = Status("failed");
        }
        catch (ExpansionTooLargeException)
        {
            await NotifyAsync(NoticeLevel.Error, Texts.Get(ui, "tooLarge", ("max", _settings.MaxChars.ToString())), ct);
            result = Status("tooLarge");
        }
        return Encoding.UTF8.GetBytes(result);
    }

    private sealed record Selected(long Anchor, long Caret, long Start, long End);

    private async Task<(List<Selected> Items, int Primary)> SelectionsAsync(Doc d, CancellationToken ct)
    {
        var read = await CallAsync(DocumentMethods.Selections, $"{{\"document\":\"{d.Id}\"}}", ct);
        if (read.GetProperty("revision").GetInt64() != d.Revision) throw new RpcException(DocumentErrorCodes.RevisionConflict, "Stale.");
        var items = read.GetProperty("selections").EnumerateArray()
            .Select(x => (A: x.GetProperty("anchor").GetInt64(), C: x.GetProperty("caret").GetInt64()))
            .Select(x => new Selected(x.A, x.C, Math.Min(x.A, x.C), Math.Max(x.A, x.C))).ToList();
        return (items, read.GetProperty("primary").GetInt32());
    }

    private static string WithCaretMarks(string text, IReadOnlyList<int> carets)
    {
        var sb = new StringBuilder(text);
        foreach (var at in carets.OrderDescending()) sb.Insert(at, '|');
        return sb.ToString();
    }

    /// <summary>Shows what the abbreviation in front of the caret would become (carets as <c>|</c>) and expands it when the user agrees.</summary>
    private async Task<string> PreviewAsync(Doc d, string syntax, string indent, string ui, CancellationToken ct)
    {
        var (selections, primary) = await SelectionsAsync(d, ct);
        List<Item>? items = null;
        if (selections.Count > 0 && selections.All(x => x.Start == x.End))
        {
            try { items = await BuildItemsAsync(d, [.. selections.Select(x => x.Caret).Distinct().Order()], syntax, indent, ct); }
            catch (UnrepresentableCharactersException) { }
        }
        if (items is null)
        {
            await NotifyAsync(NoticeLevel.Info, Texts.Get(ui, "none"), ct);
            return Status("none");
        }
        var shown = items.FirstOrDefault(i => i.End == selections[Math.Clamp(primary, 0, selections.Count - 1)].Caret) ?? items[0];
        string text = WithCaretMarks(shown.Text, shown.Carets);
        if (text.Length > 3000) text = text[..3000] + "...";
        var answer = await CallAsync(UiMethods.Confirm,
            $"{{\"title\":\"{Quote(Texts.Get(ui, "preview.title"))}\",\"message\":\"{Quote(Texts.Get(ui, "preview.message", ("abbreviation", shown.Abbreviation), ("text", text)))}\"}}", ct);
        if (answer.GetProperty("confirmed").ValueKind != JsonValueKind.True) return Status("cancelled");
        await InsertAsync(d, items, ct);
        return Status("expanded");
    }

    /// <summary>Wraps every non-blank selection in the abbreviation the user types; the text goes into the innermost last element. One snippet insertion, one undo step.</summary>
    private async Task<string> WrapAsync(Doc d, string syntax, string indent, string ui, CancellationToken ct)
    {
        if (syntax != "html")
        {
            await NotifyAsync(NoticeLevel.Warning, Texts.Get(ui, "wrap.html"), ct);
            return Status("unsupported");
        }
        var (selections, _) = await SelectionsAsync(d, ct);
        var ranges = selections.Where(x => x.Start != x.End).Select(x => (x.Start, x.End)).Distinct().OrderBy(x => x.Start).ToList();
        if (ranges.Count == 0)
        {
            await NotifyAsync(NoticeLevel.Info, Texts.Get(ui, "wrap.empty"), ct);
            return Status("none");
        }
        var answer = await CallAsync(UiMethods.Input,
            $"{{\"title\":\"{Quote(Texts.Get(ui, "wrap.title"))}\",\"label\":\"{Quote(Texts.Get(ui, "wrap.label"))}\",\"value\":\"\"}}", ct);
        if (answer.GetProperty("value").ValueKind != JsonValueKind.String || answer.GetProperty("value").GetString()!.Trim() is not { Length: > 0 } abbreviation) return Status("cancelled");

        var codec = d.Codec.WithBom(false);
        var items = new List<Item>();
        foreach (var (start, end) in ranges)
        {
            if (end - start > _settings.ContextBytes)
            {
                await NotifyAsync(NoticeLevel.Error, Texts.Get(ui, "wrap.tooBig", ("max", _settings.ContextBytes.ToString())), ct);
                return Status("failed");
            }
            var read = await CallAsync(DocumentMethods.ReadText, $"{{\"document\":\"{d.Id}\",\"revision\":{d.Revision},\"start\":{start},\"end\":{end}}}", ct);
            string text = read.GetProperty("text").GetString()!;
            string content = text.Trim();
            if (content.Length == 0) continue;
            long from = start + codec.Encode(text[..(text.Length - text.TrimStart().Length)]).Length;
            long to = end - codec.Encode(text[text.TrimEnd().Length..]).Length;
            string indentation = "";
            if (await ReadBeforeAsync(d, from, ct) is { } before && (LineStart(before.Text) > 0 || before.Complete)) indentation = LineIndent(before.Text[LineStart(before.Text)..]);
            var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 1; i < lines.Length; i++) lines[i] = lines[i].StartsWith(indentation, StringComparison.Ordinal) ? lines[i][indentation.Length..] : lines[i].TrimStart(' ', '\t');
            var expansion = HtmlExpander.Wrap(abbreviation, string.Join('\n', lines), new ExpandOptions(indent, indentation, _settings.MaxChars, _settings.MaxCarets));
            if (expansion is null)
            {
                await NotifyAsync(NoticeLevel.Error, Texts.Get(ui, "invalid"), ct);
                return Status("failed");
            }
            items.Add(new Item(from, to, expansion.Text, expansion.Carets, abbreviation));
        }
        if (items.Count == 0)
        {
            await NotifyAsync(NoticeLevel.Info, Texts.Get(ui, "wrap.empty"), ct);
            return Status("none");
        }
        await InsertAsync(d, items, ct);
        return Status("wrapped");
    }
}

using System.Text;
using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Plugins.Json.Packages;
using NksHub.Textida.Plugins.Json.Schema;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.Json;

/// <summary>Limits of the formatter; the editor passes them on the command line, so no number is fixed in the plugin.</summary>
/// <param name="MaxBytes">Largest range (in bytes of the document) that is formatted; larger input is refused with a message.</param>
/// <param name="MaxDepth">Deepest nesting accepted.</param>
public sealed record FormatSettings(long MaxBytes, int Indent, int MaxDepth);

/// <summary>
/// The built-in JSON plugin, written on the public surface of the SDK only. Its commands work on the selections (the whole document when nothing is selected) through
/// <see cref="RangeTransform"/>: every range is read at the revision the command started on, and the results go back as one batch, one undo step, or as a proposal in the change preview.
/// Nothing is changed unless every range is valid, and no byte outside the ranges is touched, so the encoding and the line endings of the rest of the file stay as they are.
/// A file counts as JSONC (comments and trailing commas allowed) by its name, as the schema check does.
/// </summary>
public sealed class JsonPlugin : PluginBase
{
    public const string Format = "json.format";
    public const string Preview = "json.preview";
    public const string Minify = "json.minify";
    public const string SortKeys = "json.sortKeys";
    public const string FixCommas = "json.fixCommas";

    private static readonly SchemaCatalog Dialects = new([]);

    private readonly FormatSettings _settings;
    private readonly Lazy<SchemaService>? _schema;
    private readonly Lazy<PackageService>? _packages;

    private delegate bool Operation(string text, JsonStyle style, out string result, out JsonFormatter.SyntaxError error);

    /// <param name="schema">Makes the schema service once the connection to the editor exists; null when the schema options were not given.</param>
    /// <param name="packages">The same for the package registry service.</param>
    public JsonPlugin(FormatSettings settings, Func<JsonRpcDispatcher, SchemaService>? schema = null, Func<JsonRpcDispatcher, PackageService>? packages = null) : base("json-formatter", apiVersion: 1)
    {
        _settings = settings;
        _schema = schema is null ? null : new Lazy<SchemaService>(() => schema(Rpc));
        _packages = packages is null ? null : new Lazy<PackageService>(() => packages(Rpc));

        OnCommand(Format, (c, ct) => RunAsync(c, "format", preview: false, ct));
        OnCommand(Preview, (c, ct) => RunAsync(c, "format", preview: true, ct));
        OnCommand(Minify, (c, ct) => RunAsync(c, "minify", preview: c.Flag("preview"), ct));
        OnCommand(SortKeys, (c, ct) => RunAsync(c, "sortKeys", preview: c.Flag("preview"), ct));
        OnCommand(FixCommas, (c, ct) => RunAsync(c, "fixCommas", preview: c.Flag("preview"), ct));
        OnCommand(SchemaService.Check, async (c, _) => CommandResult.Raw(await (_schema?.Value ?? throw new RpcException(RpcErrorCodes.InvalidParams, "The schema options were not given.")).CheckAsync(c.Parameters)));
        OnCommand(PackageService.Check, async (c, _) => CommandResult.Raw(await (_packages?.Value ?? throw new RpcException(RpcErrorCodes.InvalidParams, "The registry options were not given.")).CheckAsync(c.Parameters)));
        On(DocumentRequests.Hover, async (p, _) => await (_schema?.Value ?? throw new RpcException(RpcErrorCodes.MethodNotFound, "The plugin has no schema options.")).HoverAsync(p));
        On(DocumentRequests.Completion, CompleteAsync);
    }

    protected override async ValueTask OnResyncAsync(JsonElement state)
    {
        if (_schema is null && _packages is null) return;
        try { await (_schema is not null ? _schema.Value.RegisterAsync() : _packages!.Value.RegisterAsync()); }
        catch (RpcException) { } // the editor refused the registration: the plugin still formats
    }

    private async ValueTask<byte[]?> CompleteAsync(JsonElement p, CancellationToken ct)
    {
        if (_schema is null && _packages is null) throw new RpcException(RpcErrorCodes.MethodNotFound, "The plugin has no schema or registry options.");
        var keys = _schema is null ? null : await _schema.Value.CompleteAsync(p);
        var packages = _packages is null ? null : await _packages.Value.CompleteAsync(p);
        return keys is null ? packages! : packages is null ? keys : MergeOffers(keys, packages);
    }

    /// <summary>Joins two completion answers of the same revision: the items of <paramref name="more"/> follow those of <paramref name="first"/>. Both are written by this plugin, as <c>{"revision":n,"items":[...]}</c>.</summary>
    private static byte[] MergeOffers(byte[] first, byte[] more)
    {
        const string Marker = "\"items\":[";
        string a = Encoding.UTF8.GetString(first), b = Encoding.UTF8.GetString(more);
        string extra = b[(b.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..^2];
        if (extra.Length == 0) return first;
        return Encoding.UTF8.GetBytes(a[..^2] + (a.EndsWith("[]}", StringComparison.Ordinal) ? "" : ",") + extra + "]}");
    }

    private static Operation OperationOf(string name) => name switch
    {
        "format" => JsonFormatter.TryFormat,
        "minify" => JsonFormatter.TryMinify,
        "sortKeys" => JsonFormatter.TrySortKeys,
        _ => (string text, JsonStyle style, out string result, out JsonFormatter.SyntaxError error) => JsonFormatter.TryFixTrailingCommas(text, style.MaxDepth, out result, out error),
    };

    /// <summary>A file named like a JSONC file (<c>.jsonc</c>, <c>tsconfig.json</c> and the like) may hold comments and trailing commas; a file without a name is read as strict JSON.</summary>
    public static bool IsJsonc(string? path) => path is not null && Dialects.Resolve(path).Dialect == DataDialect.Jsonc;

    private async ValueTask<CommandResult> RunAsync(CommandContext command, string operation, bool preview, CancellationToken ct)
    {
        var document = DocumentOf(command);
        var operate = OperationOf(operation);
        DocumentFacts? facts = null;
        var outcome = await RangeTransform.RunAsync(Rpc, command, _settings.MaxBytes, Texts.Get(command.Language, "title." + operation), preview, async range =>
        {
            facts ??= await document.InfoAsync(ct);
            var style = new JsonStyle(range.NewLine, _settings.Indent, IsJsonc(facts.Path), _settings.MaxDepth);
            if (operate(range.Text, style, out var result, out var error)) return RangeResult.Changed(result);
            var (shown, inSelection) = await PlaceAsync(document, range, facts, error, ct);
            return RangeResult.Failed(Texts.Syntax(command.Language, shown, inSelection, _settings.MaxDepth), null, error.Kind == JsonErrorKind.SelfCheck ? "failed" : "syntaxError");
        }, ct, Texts.ForOperation(operation));
        return new CommandResult(outcome.Status);
    }

    /// <summary>
    /// The error with its line and column counted in the document, not from the start of the selection. When the editor cannot say where the selection starts (its
    /// line index does not reach so far) the numbers stay relative to the selection and the message says so.
    /// </summary>
    private static async Task<(JsonFormatter.SyntaxError Error, bool InSelection)> PlaceAsync(DocumentClient document, RangeContext range, DocumentFacts facts, JsonFormatter.SyntaxError error, CancellationToken ct)
    {
        if (error.Kind == JsonErrorKind.SelfCheck || range.Start == facts.BomLength) return (error, false);
        if (await document.PositionAsync(range.Start, ct) is not { } start) return (error, true);
        return (error with { Line = start.Line + error.Line, Column = error.Line == 1 ? start.Column + error.Column : error.Column }, false);
    }
}

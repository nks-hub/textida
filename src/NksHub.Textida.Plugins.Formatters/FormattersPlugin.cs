using System.Text;
using System.Text.Json;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.Formatters;

/// <summary>Settings of the plugin; the editor passes them on the command line, so no number or option is fixed in the plugin.</summary>
/// <param name="MaxBytes">Largest range that is transformed; larger input is refused with a message.</param>
/// <param name="Sql">Letter case and indentation of the SQL formatter.</param>
/// <param name="TidyArguments">Command-line arguments of HTML Tidy, in front of nothing else: the text goes to its standard input.</param>
/// <param name="TidyTimeout">Time after which a Tidy that has not finished is ended.</param>
public sealed record FormattersSettings(long MaxBytes, SqlFormatOptions Sql, IReadOnlyList<string> TidyArguments, TimeSpan TidyTimeout);

/// <summary>
/// The built-in formatters: SQL layout, HTML entities and HTML Tidy. Each works on the selections (the whole document when nothing is selected) through
/// <see cref="RangeTransform"/>, so the result is one undo step, or a proposal in the change preview, and no byte outside the ranges changes.
/// </summary>
public sealed class FormattersPlugin : PluginBase
{
    public const string SqlFormat = "sql.format";
    public const string SqlPreview = "sql.preview";
    public const string HtmlEncode = "html.encode";
    public const string HtmlDecode = "html.decode";
    public const string HtmlEncodeAll = "html.encodeAll";
    public const string HtmlTidy = "html.tidy";

    private readonly FormattersSettings _settings;

    /// <summary>Everything happens inside a command, so the editor may end the process between commands.</summary>
    protected override bool IdleStop => true;

    public FormattersPlugin(FormattersSettings settings) : base("formatters", apiVersion: 1)
    {
        _settings = settings;
        On("command/execute", ExecuteAsync);
    }

    private async ValueTask<byte[]?> ExecuteAsync(JsonElement p, CancellationToken ct)
    {
        string command = p.GetProperty("command").GetString()!;
        string language = p.TryGetProperty("language", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString()! : "en";
        string Str(string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        RangeRun run;
        switch (command)
        {
            case SqlFormat:
            case SqlPreview:
                var dialect = Str("dialect");
                run = await RangeTransform.RunAsync(Rpc, p, _settings.MaxBytes, Texts.Get(language, "sql.title"), command == SqlPreview,
                    (text, newLine, _, _) => Task.FromResult(RangeResult.Changed(SqlFormatter.Format(text, dialect, newLine, _settings.Sql))), ct);
                break;
            case HtmlEncode:
                run = await RangeTransform.RunAsync(Rpc, p, _settings.MaxBytes, "", false, (text, _, _, _) => Task.FromResult(RangeResult.Changed(HtmlEntities.Encode(text))), ct);
                break;
            case HtmlEncodeAll:
                run = await RangeTransform.RunAsync(Rpc, p, _settings.MaxBytes, "", false, (text, _, _, _) => Task.FromResult(RangeResult.Changed(HtmlEntities.EncodeAll(text))), ct);
                break;
            case HtmlDecode:
                run = await RangeTransform.RunAsync(Rpc, p, _settings.MaxBytes, "", false, (text, _, _, _) => Task.FromResult(RangeResult.Changed(HtmlEntities.Decode(text))), ct);
                break;
            case HtmlTidy:
                run = await TidyAsync(p, language, Str("tidyPath"), ct);
                break;
            default:
                throw new RpcException(RpcErrorCodes.InvalidParams, "Unknown command.");
        }
        var diagnostics = string.Join(',', run.Diagnostics.Select(d => $"\"{JsonEncodedText.Encode(d)}\""));
        return Encoding.UTF8.GetBytes($"{{\"status\":\"{run.Status}\",\"diagnostics\":[{diagnostics}]}}");
    }

    /// <summary>Tidy always answers as a proposal: the user sees the result in the change preview and its messages in the output window.</summary>
    private async Task<RangeRun> TidyAsync(JsonElement p, string language, string configured, CancellationToken ct)
    {
        var found = Tidy.Locate(configured, AppContext.BaseDirectory, Environment.GetEnvironmentVariable("PATH"));
        if (found.Path is not { } program)
        {
            var places = string.Join("; ", found.Searched);
            await Rpc.RequestAsync(UiMethods.Notify, Encoding.UTF8.GetBytes($"{{\"level\":\"error\",\"message\":\"{JsonEncodedText.Encode(Texts.Get(language, "tidy.missing", ("places", places)))}\"}}"), null, ct);
            return new RangeRun("toolMissing", [.. found.Searched]);
        }
        return await RangeTransform.RunAsync(Rpc, p, _settings.MaxBytes, Texts.Get(language, "tidy.title"), preview: true, async (text, _, _, _) =>
        {
            try
            {
                var tidy = await Tidy.RunAsync(program, _settings.TidyArguments, text, _settings.TidyTimeout, ct);
                if (tidy.Output.Length == 0 && text.Length > 0) return RangeResult.Failed(Texts.Get(language, "tidy.noOutput", ("code", tidy.ExitCode.ToString())), tidy.Diagnostics);
                return RangeResult.Changed(tidy.Output, tidy.Diagnostics);
            }
            catch (TimeoutException)
            {
                return RangeResult.Failed(Texts.Get(language, "tidy.timeout", ("seconds", ((int)_settings.TidyTimeout.TotalSeconds).ToString())));
            }
            catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return RangeResult.Failed(Texts.Get(language, "tidy.start", ("reason", e.Message)));
            }
        }, ct);
    }
}

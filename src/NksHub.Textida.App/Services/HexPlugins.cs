using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.App.Services;

/// <summary>The four things a plugin adds to the hex view.</summary>
internal enum HexKind { Decoder, Hash, Exporter, Templates }

/// <summary>One contribution of an installed plugin: <see cref="Length"/> is the bytes a decoder is sent, <see cref="Extension"/> the extension of an exporter's document.</summary>
internal sealed record HexOffer(string PluginId, string PluginName, HexKind Kind, string Id, string Title, int Length = 0, string Extension = "");

/// <summary>The answer of a decoder: the same three states as a built-in inspector row.</summary>
internal sealed record HexDecoded(InspectorStatus Status, string Text, int Length);

/// <summary>A structure template a plugin offers: the name and the Kaitai Struct text.</summary>
internal sealed record HexTemplateText(string Name, string Ksy);

/// <summary>The plugin did not answer, or its answer was refused; the text says why in a sentence for the status line.</summary>
internal sealed class HexPluginException(string message) : Exception(message);

/// <summary>
/// The plugin points of the hex view (PZ.8.4). Every call hands the plugin a copy of the bytes (Base64, a hidden value already starred) and takes back text; nothing here writes to a file or to the document, so a
/// plugin that wants to change bytes uses the document API, where its edit comes back as a proposal in the change review. Only the plugins granted <c>hex.read</c> are offered.
/// </summary>
internal interface IHexPlugins
{
    IReadOnlyList<HexOffer> Offers(HexKind kind);
    Task<HexDecoded> DecodeAsync(HexOffer offer, byte[] data, bool bigEndian, CancellationToken ct = default);
    Task<string> HashAsync(HexOffer offer, byte[] data, CancellationToken ct = default);
    Task<(string Name, string Text)> ExportAsync(HexOffer offer, byte[] data, long offset, CancellationToken ct = default);
    Task<IReadOnlyList<HexTemplateText>> TemplatesAsync(HexOffer offer, CancellationToken ct = default);
}

/// <summary>The limits of what a plugin may send back or be sent, and the reading of its answers.</summary>
internal static class HexAnswers
{
    public const int MaxData = 16 << 20;
    public const int MaxDecodedChars = 4096;
    public const int MaxHashChars = 512;
    public const int MaxExportChars = 8 << 20;
    public const int MaxTemplates = 64;
    public const int MaxTemplateChars = 256 << 10;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static byte[] Request(Action<Utf8JsonWriter> fill, byte[] data)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            fill(w);
            w.WriteBase64String("data", data);
            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static string Text(JsonElement answer, string name, int max)
    {
        if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new HexPluginException($"the answer has no text '{name}'");
        string text = value.GetString() ?? "";
        if (text.Length > max) throw new HexPluginException($"'{name}' is longer than {max} characters");
        return text;
    }

    public static HexDecoded Decoded(JsonElement answer)
    {
        string status = Text(answer, "status", 16);
        var kind = status switch
        {
            "ok" => InspectorStatus.Ok,
            "incomplete" => InspectorStatus.Incomplete,
            "invalid" => InspectorStatus.Invalid,
            _ => throw new HexPluginException($"unknown status '{status}'"),
        };
        int length = answer.TryGetProperty("length", out var l) && l.TryGetInt32(out int n) && n is >= 0 and <= 64 ? n : 0;
        return new HexDecoded(kind, kind == InspectorStatus.Ok ? Text(answer, "text", MaxDecodedChars) : "", length);
    }

    public static string Hash(JsonElement answer) => Text(answer, "value", MaxHashChars);

    public static (string Name, string Text) Export(JsonElement answer, string extension)
    {
        string text = Text(answer, "text", MaxExportChars);
        string name = answer.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? Path.GetFileName(n.GetString() ?? "") : "";
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Length > 80) name = "export" + extension;
        return (name, text);
    }

    public static IReadOnlyList<HexTemplateText> Templates(JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty("templates", out var list) || list.ValueKind != JsonValueKind.Array)
            throw new HexPluginException("the answer has no list 'templates'");
        if (list.GetArrayLength() > MaxTemplates) throw new HexPluginException($"more than {MaxTemplates} templates");
        var result = new List<HexTemplateText>();
        foreach (var item in list.EnumerateArray())
        {
            string name = Text(item, "name", 80);
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new HexPluginException($"'{name}' is no template name");
            result.Add(new HexTemplateText(name, Text(item, "ksy", MaxTemplateChars)));
        }
        return result;
    }
}

internal sealed partial class PluginServices : IHexPlugins
{
    public IReadOnlyList<HexOffer> Offers(HexKind kind) =>
        [.. _app.Clips.HexOffers.Where(o => o.Kind == kind && Permissions.IsGranted(o.PluginId, DocumentPermissions.HexRead, GrantScope.All))];

    private static string ActivationOf(HexKind kind) => ActivationEvents.Hex(kind switch { HexKind.Decoder => "decode", HexKind.Hash => "hash", HexKind.Exporter => "export", _ => "templates" });

    private async Task<JsonElement> HexCallAsync(HexOffer offer, string method, byte[] request, CancellationToken ct)
    {
        if (!Permissions.IsGranted(offer.PluginId, DocumentPermissions.HexRead, GrantScope.All)) throw new HexPluginException($"{offer.PluginName}: the permission {DocumentPermissions.HexRead} is not granted");
        try
        {
            await _host.ActivateAsync(ActivationOf(offer.Kind)).ConfigureAwait(false);
            return (await _host.CallAsync(offer.PluginId, method, request, HexAnswers.Timeout, ct).ConfigureAwait(false)).Clone();
        }
        catch (Exception e) when (e is PluginCrashedException or PluginHandshakeException or PluginUnavailableException or RpcException or TimeoutException)
        {
            throw new HexPluginException($"{offer.PluginName}: {e.Message}");
        }
    }

    private static void CheckSize(HexOffer offer, byte[] data)
    {
        if (data.Length > HexAnswers.MaxData) throw new HexPluginException($"{offer.PluginName}: {data.Length} bytes are more than {HexAnswers.MaxData} that a plugin is sent");
    }

    private static T Read<T>(HexOffer offer, Func<T> read)
    {
        try { return read(); }
        catch (HexPluginException e) { throw new HexPluginException($"{offer.PluginName}: {e.Message}"); }
    }

    public async Task<HexDecoded> DecodeAsync(HexOffer offer, byte[] data, bool bigEndian, CancellationToken ct = default)
    {
        CheckSize(offer, data);
        var request = HexAnswers.Request(w => { w.WriteString("decoder", offer.Id); w.WriteBoolean("bigEndian", bigEndian); }, data);
        var answer = await HexCallAsync(offer, HexMethods.Decode, request, ct).ConfigureAwait(false);
        return Read(offer, () => HexAnswers.Decoded(answer));
    }

    public async Task<string> HashAsync(HexOffer offer, byte[] data, CancellationToken ct = default)
    {
        CheckSize(offer, data);
        var answer = await HexCallAsync(offer, HexMethods.Hash, HexAnswers.Request(w => w.WriteString("hash", offer.Id), data), ct).ConfigureAwait(false);
        return Read(offer, () => HexAnswers.Hash(answer));
    }

    public async Task<(string Name, string Text)> ExportAsync(HexOffer offer, byte[] data, long offset, CancellationToken ct = default)
    {
        CheckSize(offer, data);
        var answer = await HexCallAsync(offer, HexMethods.Export, HexAnswers.Request(w => { w.WriteString("exporter", offer.Id); w.WriteNumber("offset", offset); }, data), ct).ConfigureAwait(false);
        return Read(offer, () => HexAnswers.Export(answer, offer.Extension));
    }

    public async Task<IReadOnlyList<HexTemplateText>> TemplatesAsync(HexOffer offer, CancellationToken ct = default)
    {
        var answer = await HexCallAsync(offer, HexMethods.Templates, HexAnswers.Request(w => w.WriteString("source", offer.Id), []), ct).ConfigureAwait(false);
        return Read(offer, () => HexAnswers.Templates(answer));
    }
}

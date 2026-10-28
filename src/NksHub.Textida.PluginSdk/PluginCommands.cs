using System.Buffers;
using System.Text.Json;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginSdk;

/// <summary>
/// One <c>command/execute</c> request of the editor. <see cref="DocumentId"/> and <see cref="Revision"/> name the document and the revision the command was started on
/// (null for a command that has no document); <see cref="Language"/> is the language of the editor (cs, en, sk, de) for the texts the plugin shows.
/// </summary>
public sealed record CommandContext(string Command, string? DocumentId, long? Revision, string Language, JsonElement Parameters)
{
    /// <summary>Reads a request; a request without <c>command</c> is a protocol error.</summary>
    public static CommandContext From(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("command", out var command) || command.ValueKind != JsonValueKind.String)
            throw new RpcException(RpcErrorCodes.InvalidParams, "The request names no command.");
        return new CommandContext(command.GetString()!, Text(parameters, "document"),
            parameters.TryGetProperty("revision", out var revision) && revision.ValueKind == JsonValueKind.Number ? revision.GetInt64() : null,
            Text(parameters, "language") ?? "en", parameters);
    }

    /// <summary>A string member of the request, or null when it is absent or not a string.</summary>
    public string? String(string name) => Text(Parameters, name);

    /// <summary>True when the request has the member and it is <c>true</c>.</summary>
    public bool Flag(string name) => Parameters.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>How a command ended: a status word the editor reads, and lines for the output window.</summary>
public sealed record CommandResult(string Status, IReadOnlyList<string>? Diagnostics = null)
{
    private byte[]? _json;

    /// <summary>An answer the command wrote itself, for a command whose result is more than a status; the editor reads it as it is.</summary>
    public static CommandResult Raw(byte[] json) => new("", null) { _json = json };

    internal byte[] ToJson()
    {
        if (_json is not null) return _json;
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("status", Status);
            if (Diagnostics is not null)
            {
                w.WriteStartArray("diagnostics");
                foreach (var line in Diagnostics) w.WriteStringValue(line);
                w.WriteEndArray();
            }
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }
}

/// <summary>Runs one command; the token is cancelled when the editor cancels the request or the plugin stops.</summary>
public delegate ValueTask<CommandResult> CommandHandler(CommandContext context, CancellationToken cancellation);

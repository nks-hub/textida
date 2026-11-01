using System.Text;
using System.Text.Json;

namespace NksHub.Textida.Remote;

/// <summary>
/// What the user allowed the programs the editor starts (language servers) to reach, and the proxy of the network policy they must go through, kept in <c>tool-network.json</c> of the profile.
/// An entry is <c>tool|host</c>, so a consent is per tool and per host; <c>tool|*</c> is the consent of a tool whose author asks for the whole network. The editor and the plugin process read the same file
/// before every decision, so a consent given in one takes effect at once and a revoke ends the access of programs that are running. This file is linked into the language server plugin, which has no other
/// reference to the network code of the editor.
/// </summary>
public sealed class ToolNetworkConsent(string path)
{
    public string FilePath { get; } = path;

    /// <param name="Proxy">The HTTP proxy of the network policy (<c>http://host:port</c>), or null for none.</param>
    /// <param name="Blocked">The network policy cannot be honoured (it cannot be read, or its proxy is not an HTTP proxy without a login): nothing passes.</param>
    public sealed record State(List<string> Allowed, string? Proxy, bool Blocked);

    public State Read()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath, Encoding.UTF8));
            var root = doc.RootElement;
            var allowed = new List<string>();
            if (root.TryGetProperty("allowed", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var item in list.EnumerateArray()) if (item.ValueKind == JsonValueKind.String) allowed.Add(item.GetString()!);
            return new(allowed, root.TryGetProperty("proxy", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
                root.TryGetProperty("blocked", out var b) && b.ValueKind == JsonValueKind.True);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new([], null, false); }
    }

    private void Write(State state)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteStartArray("allowed");
            foreach (var entry in state.Allowed) json.WriteStringValue(entry);
            json.WriteEndArray();
            if (state.Proxy is not null) json.WriteString("proxy", state.Proxy);
            json.WriteBoolean("blocked", state.Blocked);
            json.WriteEndObject();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        string temp = FilePath + ".textida-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllBytes(temp, stream.ToArray());
        File.Move(temp, FilePath, overwrite: true);
    }

    public static string Id(string tool, string host) => tool + "|" + host.ToLowerInvariant();

    public bool IsAllowed(string tool, string host)
    {
        var state = Read();
        return state.Allowed.Contains(Id(tool, host)) || state.Allowed.Contains(Id(tool, "*"));
    }

    /// <summary>The user said no to the tool; the question is not asked again until the consents are revoked.</summary>
    public bool Declined(string tool) => Read().Allowed.Contains(Id(tool, "-"));

    public void Decline(string tool) => Allow(tool, ["-"]);

    public void Allow(string tool, IEnumerable<string> hosts)
    {
        var state = Read();
        foreach (var host in hosts)
            if (!state.Allowed.Contains(Id(tool, host))) state.Allowed.Add(Id(tool, host));
        Write(state);
    }

    /// <summary>Takes every consent back; the proxy stops passing at the next request.</summary>
    public void RevokeAll()
    {
        var state = Read();
        if (state.Allowed.Count > 0) Write(state with { Allowed = [] });
    }

    /// <summary>Records the proxy of the network policy, or why nothing may pass (<paramref name="blocked"/>).</summary>
    public void SetProxy(string? proxy, bool blocked)
    {
        var state = Read();
        if (state.Proxy != proxy || state.Blocked != blocked || !File.Exists(FilePath)) Write(state with { Proxy = proxy, Blocked = blocked });
    }
}

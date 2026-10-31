using System.Text.Json;
using System.Text.Json.Nodes;

namespace NksHub.Textida.App.Services;

/// <summary>
/// Which MCP clients may use the editor, by the name a client gives in its <c>clientInfo</c>. A client the user has not decided about has no permission: it sees no tools, and the editor asks the user once.
/// The decisions are kept in a file of the profile, so they hold after a restart; the user can take the permission of a single client away (or give it back) at any time. A name is not a proof of identity
/// (any program can claim one); the permission only keeps a client the user never heard of from reading the shared tabs by itself.
/// </summary>
internal sealed class McpClients(string file)
{
    private readonly object _gate = new();
    private Dictionary<string, bool>? _decisions;
    private readonly HashSet<string> _asked = [];

    /// <summary>A client without a decision connected; raised once per name until the user decides. Any thread.</summary>
    public event Action<string>? Asked;

    /// <summary>Every client with a decision, by name; true when it may use the editor.</summary>
    public IReadOnlyDictionary<string, bool> Decisions
    {
        get
        {
            lock (_gate) return new Dictionary<string, bool>(Load(), StringComparer.Ordinal);
        }
    }

    /// <summary>The gate of the MCP server: true for a client the user allowed; a client with no decision gets false and the user is asked.</summary>
    public bool IsAllowed(string name)
    {
        bool ask;
        lock (_gate)
        {
            if (Load().TryGetValue(name, out var allowed)) return allowed;
            ask = _asked.Add(name);
        }
        if (ask) Asked?.Invoke(name);
        return false;
    }

    /// <summary>Records the decision of the user for a client.</summary>
    public void Decide(string name, bool allowed)
    {
        lock (_gate)
        {
            Load()[name] = allowed;
            _asked.Remove(name);
            Save();
        }
    }

    /// <summary>Forgets the decision: the next time the client connects the user is asked again.</summary>
    public void Forget(string name)
    {
        lock (_gate)
        {
            if (Load().Remove(name)) Save();
            _asked.Remove(name);
        }
    }

    private Dictionary<string, bool> Load()
    {
        if (_decisions is not null) return _decisions;
        _decisions = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(file) && JsonNode.Parse(File.ReadAllText(file)) is JsonObject root && root["clients"] is JsonObject clients)
                foreach (var (name, value) in clients)
                    if (value is JsonValue v && v.TryGetValue<bool>(out var allowed)) _decisions[name] = allowed;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { /* a damaged file means nobody is allowed yet; the user is asked again */ }
        return _decisions;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var clients = new JsonObject();
            foreach (var (name, allowed) in _decisions!.OrderBy(d => d.Key, StringComparer.Ordinal)) clients[name] = allowed;
            var temp = file + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(temp, new JsonObject { ["clients"] = clients }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the decision holds for this run */ }
    }
}

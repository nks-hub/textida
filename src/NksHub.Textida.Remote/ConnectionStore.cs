using System.Text.Json;
using System.Text.Json.Serialization;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.Remote;

internal sealed record ConnectionsFile(int Version, List<RemoteConnection> Connections);

[JsonSerializable(typeof(ConnectionsFile))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
internal sealed partial class ConnectionsJson : JsonSerializerContext;

/// <summary>
/// The saved connections. The file and the exported backup contain <see cref="RemoteConnection"/> only, which has no password field; passwords and
/// passphrases go to the <see cref="ICredentialStore"/> and are never copied into either.
/// </summary>
public sealed class ConnectionStore(string path)
{
    private const int CurrentVersion = 1;

    public IReadOnlyList<RemoteConnection> Load()
    {
        if (!File.Exists(path)) return [];
        try { return Import(File.ReadAllText(path)); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return []; /* a damaged list starts empty; the file stays for inspection until the next save */ }
    }

    public void Save(IEnumerable<RemoteConnection> connections)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".textida-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(temp, Export(connections));
        FileSwap.Replace(temp, path);
    }

    /// <summary>Saves the connection and puts its password into the credential store. The password is never written to the connection file.</summary>
    public void Add(RemoteConnection connection, string? password, ICredentialStore secrets)
    {
        if (password is not null) secrets.Set(connection.SecretKey, password);
        Save(Load().Where(c => c.Id != connection.Id).Append(connection));
    }

    public void Remove(RemoteConnection connection, ICredentialStore secrets)
    {
        secrets.Delete(connection.SecretKey);
        secrets.Delete(connection.ProxySecretKey);
        Save(Load().Where(c => c.Id != connection.Id));
    }

    /// <summary>The backup text of the connections. It holds no password, passphrase or token, so it can be shared; the passwords are entered again after an import.</summary>
    public static string Export(IEnumerable<RemoteConnection> connections) =>
        JsonSerializer.Serialize(new ConnectionsFile(CurrentVersion, [.. connections]), ConnectionsJson.Default.ConnectionsFile);

    public static IReadOnlyList<RemoteConnection> Import(string json)
    {
        var file = JsonSerializer.Deserialize(json, ConnectionsJson.Default.ConnectionsFile) ?? throw new JsonException("Empty connection list.");
        if (file.Version > CurrentVersion) throw new JsonException($"The list was written by a newer editor (version {file.Version}).");
        return file.Connections;
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Http;

/// <summary>
/// The variables of a request: <c>{{name}}</c> is replaced by the value the file defines (<c>@name = value</c>), the chosen environment gives, or the system provides (<c>{{$guid}}</c>, <c>{{$timestamp}}</c>,
/// <c>{{$isoTimestamp}}</c>, <c>{{$randomInt}}</c>). A value may name other variables; a name that cannot be resolved is reported and left as it is, so a request is not sent with a hole in it.
/// </summary>
public static partial class HttpVariables
{
    [GeneratedRegex(@"\{\{\s*(?<name>[^{}\s]+)\s*\}\}")]
    private static partial Regex Reference();

    /// <summary>Replaces the variables of <paramref name="text"/>; the names no source knows are added to <paramref name="missing"/>. Values are expanded up to five levels deep (a variable that names itself ends there).</summary>
    public static string Expand(string text, Func<string, string?> lookup, List<string> missing, int depth = 0)
    {
        if (depth > 5) return text;
        return Reference().Replace(text, m =>
        {
            string name = m.Groups["name"].Value;
            string? value = name.StartsWith('$') ? System(name) : lookup(name);
            if (value is null)
            {
                if (!missing.Contains(name)) missing.Add(name);
                return m.Value;
            }
            return Expand(value, lookup, missing, depth + 1);
        });
    }

    private static string? System(string name) => name switch
    {
        "$guid" => Guid.NewGuid().ToString(),
        "$timestamp" => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        "$isoTimestamp" => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
        "$randomInt" => Random.Shared.Next(0, 1000).ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>The request with every variable replaced; <paramref name="missing"/> lists the names that could not be.</summary>
    public static HttpRequest Resolve(HttpRequest request, Func<string, string?> lookup, List<string> missing) =>
        request with
        {
            Url = Expand(request.Url, lookup, missing),
            Headers = [.. request.Headers.Select(h => (Expand(h.Name, lookup, missing), Expand(h.Value, lookup, missing)))],
            Body = Expand(request.Body, lookup, missing),
        };
}

/// <summary>
/// The environments of a folder: <c>http-client.env.json</c> (shared, for the repository) and <c>http-client.private.env.json</c> (private, with the passwords and tokens, kept out of the repository), each an object of
/// environments (<c>{"dev": {"host": "..."}}</c>); the environment <c>$shared</c> holds what every environment has. The private file wins over the shared one, and its values are what must not leak into
/// anything that is shared: <see cref="Values"/> says which they are.
/// </summary>
public sealed class HttpEnvironments(IReadOnlyDictionary<string, Dictionary<string, string>> shared, IReadOnlyDictionary<string, Dictionary<string, string>> priv)
{
    public const string SharedFileName = "http-client.env.json";
    public const string PrivateFileName = "http-client.private.env.json";

    /// <summary>The names of the environments of both files (without <c>$shared</c>), in alphabetical order.</summary>
    public IReadOnlyList<string> Names => [.. shared.Keys.Concat(priv.Keys).Where(n => n != "$shared").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>The values of <paramref name="environment"/> (null: only <c>$shared</c>) and the names of those that came from the private file.</summary>
    public (Dictionary<string, string> Values, HashSet<string> Private) Values(string? environment)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var secret = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (file, isPrivate) in new[] { (shared, false), (priv, true) })
            foreach (var scope in environment is null ? new[] { "$shared" } : new[] { "$shared", environment })
                if (file.TryGetValue(scope, out var set))
                    foreach (var (k, v) in set)
                    {
                        values[k] = v;
                        if (isPrivate) secret.Add(k); else secret.Remove(k);
                    }
        return (values, secret);
    }

    /// <summary>Reads the two files of <paramref name="folder"/>; a file that is missing or not valid JSON is the same as an empty one.</summary>
    public static HttpEnvironments Load(string folder) => new(Read(Path.Combine(folder, SharedFileName)), Read(Path.Combine(folder, PrivateFileName)));

    private static Dictionary<string, Dictionary<string, string>> Read(string path)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var environment in doc.RootElement.EnumerateObject())
            {
                if (environment.Value.ValueKind != JsonValueKind.Object) continue;
                var set = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var item in environment.Value.EnumerateObject())
                    set[item.Name] = item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString()! : item.Value.GetRawText();
                result[environment.Name] = set;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { /* no environments from this file */ }
        return result;
    }

    /// <summary>The text with every value of the private file replaced by <c>***</c>, so a request can be shown or kept without the secrets in it.</summary>
    public static string Mask(string text, IEnumerable<string> secretValues)
    {
        foreach (var value in secretValues.Where(v => v.Length >= 3).OrderByDescending(v => v.Length)) text = text.Replace(value, "***", StringComparison.Ordinal);
        return text;
    }
}
